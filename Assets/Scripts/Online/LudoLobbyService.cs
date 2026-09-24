// LudoLobbyService.cs
// Phase 3: lobby/matchmaking via the Multiplayer Services SDK (Sessions API,
// package com.unity.services.multiplayer). Sessions here are LOBBY-ONLY:
// no Relay/netcode network — gameplay stays on Cloud Code.
//
// Flow:
//   QuickJoin(n)  -> find any session of that size with a free seat, or
//                    create one after the timeout.
//   CreateRoom(n) -> private session; share session.Code with friends.
//   JoinByCode(c) -> join a friend's room.
//   When the session is full (or host presses Start with bots):
//     HOST: CreateMatch on Cloud Code -> writes matchId into a session
//           property. MEMBERS: see the property change -> JoinMatch.
//   OnMatchReady fires on everyone with the joined MatchDoc; the session
//   is left behind.
//
// NOTE: If your SDK version names differ slightly (e.g. property/index
// enums), only the marked lines need adjusting — the flow is version-proof.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;
using UnityEngine;

public class LudoLobbyService : MonoBehaviour
{
    public CloudMatchService matchService;

    private const string MatchIdProp = "matchId";
    private const string SessionType = "ludo";

    public ISession Session { get; private set; }
    public bool IsHost => Session != null && Session.IsHost;

    /// Session state changed (player joined/left) — refresh lobby UI.
    public event Action OnLobbyChanged;
    /// Match created & joined — switch to the game.
    public event Action<MatchDoc> OnMatchReady;

    private bool matchStarted;

    private string MyName =>
        "Player-" + AuthenticationService.Instance.PlayerId.Substring(0, 4);

    // ------------------------------------------------------------ ENTRY

    public async Task QuickJoin(int playerCount)
    {
        // Mode is matched via an INDEXED session property (String1) — the
        // quick-join backend only accepts indexed filter fields.
        string modeTag = "mode" + playerCount;

        var quickJoinOptions = new QuickJoinOptions
        {
            Filters = new List<FilterOption>
            {
                new(FilterField.AvailableSlots, "1", FilterOperation.GreaterOrEqual),
                new(FilterField.StringIndex1, modeTag, FilterOperation.Equal),
            },
            Timeout = TimeSpan.FromSeconds(8),
            CreateSession = true            // nobody waiting? open a session
        };
        var sessionOptions = new SessionOptions
        {
            MaxPlayers = playerCount,
            Type = SessionType,
            IsPrivate = false,
            // The session we might CREATE must carry the same indexed tag,
            // or later quick-joiners can never find it.
            SessionProperties = new Dictionary<string, SessionProperty>
            {
                { "mode", new SessionProperty(modeTag,
                      VisibilityPropertyOptions.Public, PropertyIndex.String1) }
            }
        };

        var s = await MultiplayerService.Instance
            .MatchmakeSessionAsync(quickJoinOptions, sessionOptions);
        Adopt(s);
    }

    public async Task CreateRoom(int playerCount)
    {
        var options = new SessionOptions
        {
            MaxPlayers = playerCount,
            Type = SessionType,
            IsPrivate = true                // joinable only via Session.Code
        };
        var s = await MultiplayerService.Instance.CreateSessionAsync(options);
        Adopt(s);
    }

    public async Task JoinByCode(string code)
    {
        var s = await MultiplayerService.Instance
            .JoinSessionByCodeAsync(code.Trim().ToUpperInvariant());
        Adopt(s);
    }

    /// Host-only: start now, filling empty seats with server bots.
    public async Task StartWithBots()
    {
        if (!IsHost || matchStarted) return;
        int bots = Session.MaxPlayers - Session.Players.Count;
        await HostCreateMatch(bots);
    }

    public async Task LeaveLobby()
    {
        if (Session == null) return;
        try { await Session.LeaveAsync(); } catch { }
        Cleanup();
    }

    // ------------------------------------------------------- INTERNALS

    private void Adopt(ISession s)
    {
        Session = s;
        matchStarted = false;

        s.PlayerJoined += _ => OnSessionChanged();
        s.PlayerLeaving += _ => OnSessionChanged();
        s.SessionPropertiesChanged += OnPropertiesChanged;
        s.RemovedFromSession += Cleanup;

        OnLobbyChanged?.Invoke();
        OnSessionChanged();                 // maybe already full (quick join)
    }

    private async void OnSessionChanged()
    {
        OnLobbyChanged?.Invoke();

        // Host starts the match the moment the room is full.
        if (IsHost && !matchStarted &&
            Session.Players.Count >= Session.MaxPlayers)
        {
            await HostCreateMatch(0);
        }

        // Guests: the matchId property may arrive bundled with other session
        // events — check it on every change, not only on PropertiesChanged.
        TryJoinFromProperty();
    }

    private async Task HostCreateMatch(int botCount)
    {
        if (matchStarted) return;
        matchStarted = true;

        try
        {
            var doc = await matchService.CreateMatch(
                Session.MaxPlayers, MyName, botCount);

            // Publish the match id to all members.  << adjust here if your
            // SDK version exposes host properties differently.
            var host = Session.AsHost();
            host.SetProperty(MatchIdProp,
                new SessionProperty(doc.MatchId, VisibilityPropertyOptions.Member));
            await host.SavePropertiesAsync();

            EnterMatch(doc);
        }
        catch (Exception e)
        {
            matchStarted = false;
            Debug.LogError($"Host match creation failed: {e.Message}");
            UIManager.I.ShowError("error.network");
        }
    }

    private void OnPropertiesChanged() => TryJoinFromProperty();

    private async void TryJoinFromProperty()
    {
        if (matchStarted || Session == null || IsHost) return;
        if (Session.Properties == null ||
            !Session.Properties.TryGetValue(MatchIdProp, out var prop) ||
            string.IsNullOrEmpty(prop?.Value))
            return;

        matchStarted = true;
        UIManager.I.ShowLoading("loading.connecting");

        // The freshly written match doc can take several seconds to become
        // visible to OTHER players' requests (storage propagation). This is
        // expected, not an error — wait patiently with growing delays.
        int[] delaysMs = { 500, 1000, 1500, 2000, 2500, 3000 };
        for (int i = 0; i <= delaysMs.Length; i++)
        {
            try
            {
                var doc = await matchService.JoinMatch(prop.Value, MyName);
                UIManager.I.HideLoading();
                EnterMatch(doc);
                return;
            }
            catch (Exception e)
            {
                Debug.Log($"Join attempt {i + 1}: match not visible yet ({e.Message})");
                if (i < delaysMs.Length)
                    await Task.Delay(delaysMs[i]);
            }
        }

        matchStarted = false;
        UIManager.I.HideLoading();
        UIManager.I.ShowError("error.network");
    }

    private void EnterMatch(MatchDoc doc)
    {
        // Push messages deliver updates in ~300ms; polling every 12s is
        // only the safety net for missed pushes.
        matchService.StartPolling(12f);
        bool wasHost = IsHost;
        OnMatchReady?.Invoke(doc);

        if (!wasHost)
        {
            // Guest has joined the match — the session served its purpose.
            _ = LeaveLobby();
        }
        else
        {
            // HOST MUST STAY in the session until every guest has read the
            // matchId property and joined — leaving early destroys the
            // handoff. Leave once the match turns active (all seats filled).
            matchService.OnMatchUpdated += LeaveWhenActive;
            if (doc.IsActive) LeaveWhenActive(doc);
        }
    }

    private void LeaveWhenActive(MatchDoc doc)
    {
        if (!doc.IsActive && !doc.IsFinished) return;
        matchService.OnMatchUpdated -= LeaveWhenActive;
        _ = LeaveLobby();
    }

    private void Cleanup()
    {
        Session = null;
        OnLobbyChanged?.Invoke();
    }
}
