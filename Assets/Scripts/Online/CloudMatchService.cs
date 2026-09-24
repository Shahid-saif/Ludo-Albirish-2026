// CloudMatchService.cs
// IMatchService implementation over UGS Cloud Code module endpoints.
// Attach to a persistent GameObject (e.g. the Bootstrap object).
//
// Requires: Cloud Code package + Newtonsoft Json (com.unity.nuget.newtonsoft-json,
// usually already pulled in by UGS packages).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Services.Authentication;
using Unity.Services.CloudCode;
using Unity.Services.CloudCode.Subscriptions;
using UnityEngine;

public class CloudMatchService : MonoBehaviour, IMatchService
{
    [Tooltip("Must match the deployed module name exactly")]
    public string moduleName = "Ludo_Game_module";

    public MatchDoc Current { get; private set; }
    public int MySeat =>
        Current?.SeatOf(AuthenticationService.Instance.PlayerId) ?? -1;

    public event Action<MatchDoc> OnMatchUpdated;

    private Coroutine pollRoutine;

    // ------------------------------------------------------------ CALLS

    public async Task<MatchDoc> CreateMatch(int maxPlayers, string playerName, int botCount = 0)
    {
        var json = await Call("CreateMatch", new()
        {
            ["maxPlayers"] = maxPlayers,
            ["playerName"] = playerName,
            ["botCount"] = botCount
        });
        return Apply(JsonConvert.DeserializeObject<MatchDoc>(json));
    }

    public async Task<MatchDoc> JoinMatch(string matchId, string playerName)
    {
        var json = await Call("JoinMatch", new()
        {
            ["matchId"] = matchId,
            ["playerName"] = playerName
        });
        return Apply(JsonConvert.DeserializeObject<MatchDoc>(json));
    }

    public async Task<MatchDoc> Refresh()
    {
        RequireMatch();
        var json = await Call("GetMatch", new() { ["matchId"] = Current.MatchId });
        return Apply(JsonConvert.DeserializeObject<MatchDoc>(json));
    }

    public async Task<RollResponse> SubmitRoll()
    {
        RequireMatch();
        var json = await Call("SubmitRoll", new() { ["matchId"] = Current.MatchId });
        var resp = JsonConvert.DeserializeObject<RollResponse>(json);
        Apply(resp.match);
        return resp;
    }

    public async Task<MoveResponse> SubmitMove(int tokenIndex)
    {
        RequireMatch();
        var json = await Call("SubmitMove", new()
        {
            ["matchId"] = Current.MatchId,
            ["tokenIndex"] = tokenIndex
        });
        var resp = JsonConvert.DeserializeObject<MoveResponse>(json);
        Apply(resp.match);
        return resp;
    }

    // ---------------------------------------------------------- POLLING

    /// With push messages active, polling is only a SLOW SAFETY NET
    /// (push is best-effort). Suggested interval: 10-15s.
    public void StartPolling(float intervalSeconds)
    {
        StopPolling();
        pollRoutine = StartCoroutine(PollLoop(intervalSeconds));
        _ = EnablePush();
    }

    public void StopPolling()
    {
        if (pollRoutine != null) StopCoroutine(pollRoutine);
        pollRoutine = null;
    }

    // ------------------------------------------------------------- PUSH

    private bool pushSubscribed;
    private volatile bool pushRefreshQueued;

    /// Subscribe once to Cloud Code player push messages. The module sends
    /// "match-updated" with the matchId after every state change.
    private async Task EnablePush()
    {
        if (pushSubscribed) return;
        pushSubscribed = true;
        try
        {
            var callbacks = new SubscriptionEventCallbacks();
            callbacks.MessageReceived += e =>
            {
                if (e.MessageType == "match-updated" &&
                    Current != null && e.Message == Current.MatchId)
                    pushRefreshQueued = true;   // consumed on main thread
            };
            callbacks.Error += err =>
                Debug.LogWarning($"Push subscription error: {err}");

            await CloudCodeService.Instance.SubscribeToPlayerMessagesAsync(callbacks);
            Debug.Log("Push messages: subscribed");
        }
        catch (Exception e)
        {
            pushSubscribed = false;
            Debug.LogWarning($"Push subscribe failed ({e.Message}) — polling only");
        }
    }

    void Update()
    {
        if (pushRefreshQueued && Current != null && !Current.IsFinished)
        {
            pushRefreshQueued = false;
            _ = PushRefresh();
        }
    }

    private async Task PushRefresh()
    {
        try { await Refresh(); }
        catch (Exception e)
        { Debug.LogWarning($"Push-triggered refresh failed: {e.Message}"); }
    }

    private IEnumerator PollLoop(float interval)
    {
        var wait = new WaitForSeconds(interval);
        while (true)
        {
            yield return wait;
            if (Current == null) continue;
            if (Current.IsFinished) yield break;

            // Skip polling while it's my turn — I'm the source of changes.
            if (Current.IsActive && Current.State != null &&
                Current.State.CurrentPlayer == MySeat) continue;

            var task = Refresh();
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted)
                Debug.LogWarning($"Poll failed: {task.Exception?.GetBaseException().Message}");
        }
    }

    // ---------------------------------------------------------- HELPERS

    private async Task<string> Call(string fn, Dictionary<string, object> args)
    {
        try
        {
            return await CloudCodeService.Instance
                .CallModuleEndpointAsync<string>(moduleName, fn, args);
        }
        catch (CloudCodeException e)
        {
            // Surface the server's throw message (e.g. "Not your turn").
            Debug.LogWarning($"{fn} failed: {e.Message}");
            throw;
        }
    }

    /// Adopt a document only if it's newer than what we have; notify.
    private MatchDoc Apply(MatchDoc doc)
    {
        if (doc == null) return Current;

        bool newer = Current == null
            || doc.UpdatedAt > Current.UpdatedAt
            || (doc.State != null && Current.State != null &&
                doc.State.TurnNumber > Current.State.TurnNumber);

        if (newer)
        {
            Current = doc;
            OnMatchUpdated?.Invoke(doc);
        }
        return Current;
    }

    private void RequireMatch()
    {
        if (Current == null) throw new InvalidOperationException("No match joined");
    }
}
