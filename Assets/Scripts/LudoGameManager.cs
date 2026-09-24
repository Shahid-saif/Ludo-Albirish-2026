// LudoGameManager.cs
// Drives the board in two modes:
//   Offline — LudoLocalGame runs everything on-device (unchanged behavior).
//   Online  — the server is the truth: dice press => SubmitRoll,
//             piece tap => SubmitMove, and ALL movement (mine and my
//             opponents') animates via one path: diff the incoming state
//             against the last rendered state.
//
// Online setup: mode = Online, assign `online` (CloudMatchService) and
// `orientation` (BoardOrientation with applyOnStart OFF). The board
// initializes itself when the match document turns "active".

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LudoCore;

public class LudoGameManager : MonoBehaviour
{
    public enum GameMode { Offline, Online }

    [Header("Mode")]
    public GameMode mode = GameMode.Offline;
    public CloudMatchService online;         // required in Online mode
    public BoardOrientation orientation;     // used in Online mode

    [Header("Players")]
    [Range(2, 4)] public int playerCount = 4;

    [Header("Board points (52, index 0 = player 0 start cell)")]
    public Transform[] boardTrack;

    [System.Serializable]
    public class PlayerPoints
    {
        public Transform[] yardPoints;   // 4
        public Transform[] winPoints;    // 6 (positions 52..57)
        public LudoTokenView[] tokens;   // 4
        public bool isBot;               // OFFLINE ONLY (server bots come later)
    }
    public PlayerPoints[] players;       // size 4; playerCount used at runtime

    [Header("Profiles")]
    public ProfileSeatMapper profiles;

    [Header("Animation")]
    public float stepDuration = 0.18f;
    [Tooltip("How long a remote player's dice value shows before their move animates")]
    public float remoteDiceShowTime = 0.6f;

    [Header("Stacking")]
    public Vector2 stackOffset = new Vector2(8f, 8f);

    [Header("Turn styling")]
    public float activeScale = 1.15f;

    [Header("Gameplay helpers")]
    public bool autoMoveWhenNoChoice = true;
    public float autoMoveDelay = 0.3f;

    [Header("Bots (offline only)")]
    public float botRollDelay = 0.7f;
    public float botMoveDelay = 0.5f;

    // ---- internals
    private LudoLocalGame game;              // offline only
    private RollResult pendingRoll;
    private bool animating;
    private int arrivalCounter;
    private bool gameOver;
    private bool botThinking;

    private bool onlineReady;
    private LudoGameState lastRendered;      // online: what the board shows
    private bool processingRemote;
    private long lastActionSeq;              // last replayed action

    /// The authoritative state for the current mode.
    private LudoGameState S =>
        mode == GameMode.Online ? online.Current?.State : game?.State;

    private int MySeat => mode == GameMode.Online ? online.MySeat : -1;

    // ============================================================ START

    void Start()
    {
        if (mode == GameMode.Offline)
        {
            game = new LudoLocalGame(playerCount);
            InitViews();
            profiles.Init(this);
            RefreshBoardVisuals();
            RefreshTurnState();
        }
        else
        {
            profiles.Init(this);
            online.OnMatchUpdated += OnMatchDoc;
            if (online.Current != null) OnMatchDoc(online.Current);
        }
    }

    private void InitViews()
    {
        for (int p = 0; p < playerCount; p++)
            for (int t = 0; t < LudoConfig.TokensPerPlayer; t++)
            {
                players[p].tokens[t].Init(this, p, t);
                players[p].tokens[t].ArrivalOrder = ++arrivalCounter;
            }
    }

    // ================================================== ONLINE UPDATES

    private void OnMatchDoc(MatchDoc doc)
    {
        if (!doc.IsActive && !doc.IsFinished) return;   // still waiting

        if (!onlineReady)
        {
            // First time the match is active: build the board for it.
            onlineReady = true;
            playerCount = doc.MaxPlayers;
            InitViews();
            profiles.SetOnlineData(doc.PlayerNames);
            if (orientation != null)
                orientation.SetLocalPlayer(online.MySeat);
            lastRendered = doc.State.Clone();
            lastActionSeq = doc.ActionSeq;
            RefreshBoardVisuals();
            RefreshTurnState();
            return;
        }

        if (!processingRemote)
            StartCoroutine(ProcessRemote());
    }

    /// Replay server actions one by one: show the actor's dice, pause so it
    /// can be read, then animate that exact move via the local engine
    /// (deterministic replay). Falls back to a snap-diff if the action
    /// history has a gap (e.g. long disconnect).
    private IEnumerator ProcessRemote()
    {
        processingRemote = true;

        var doc = online.Current;
        while (doc != null &&
               (lastActionSeq < doc.ActionSeq || !StatesEqual(lastRendered, S)))
        {
            var pending = new List<MatchAction>();
            foreach (var a in doc.Actions)
                if (a.Seq > lastActionSeq) pending.Add(a);
            pending.Sort((a, b) => a.Seq.CompareTo(b.Seq));

            bool gap = pending.Count == 0 || pending[0].Seq != lastActionSeq + 1;

            if (!gap)
            {
                var sim = lastRendered.Clone();
                foreach (var a in pending)
                {
                    if (a.T == "R")
                    {
                        Debug.Log($"[replay] seat {a.Seat} rolled {a.Dice}");
                        var slot = profiles.GetSlotForPlayer(a.Seat);
                        if (slot == null)
                            Debug.LogWarning($"[replay] no slot bound for seat {a.Seat}!");
                        else slot.SetDiceValue(a.Dice);
                        if (a.Seat != MySeat)
                            yield return new WaitForSeconds(remoteDiceShowTime);
                        try { LudoEngine.ApplyRoll(sim, a.Dice); }
                        catch { gap = true; break; }
                    }
                    else
                    {
                        Debug.Log($"[replay] seat {a.Seat} moves token {a.Token}");
                        MoveResult r;
                        try { r = LudoEngine.ApplyMove(sim, a.Token); }
                        catch { gap = true; break; }
                        yield return AnimateSingleMove(a.Seat, r);
                    }
                    lastActionSeq = a.Seq;
                }
                if (!gap) lastRendered = sim;
            }

            if (gap)
            {
                Debug.LogWarning("[replay] history gap — snapping to latest state");
                var target = S.Clone();
                yield return AnimateDiff(lastRendered, target);
                lastRendered = target;
                lastActionSeq = doc.ActionSeq;
            }

            doc = online.Current;   // a newer doc may have arrived meanwhile
        }

        RefreshBoardVisuals();

        if (doc != null && doc.IsFinished && !gameOver)
        {
            gameOver = true;
            Debug.Log($"GAME OVER! Finish order: {string.Join(", ", S.FinishedPlayers)}");
            profiles.DisableAll();
            online.StopPolling();
        }
        else
        {
            RefreshTurnState();
            RestorePendingTurn();
        }

        processingRemote = false;
    }

    /// Animate one engine-validated move (mover steps, captures fly home).
    private IEnumerator AnimateSingleMove(int player, MoveResult r)
    {
        animating = true;
        var view = players[player].tokens[r.Move.TokenIndex];
        view.ArrivalOrder = ++arrivalCounter;
        view.transform.SetAsLastSibling();

        if (r.Move.LeavesBase)
        {
            yield return view.MoveTo(GetWorldPoint(player, r.Move.TokenIndex, r.Move.ToPos).position, stepDuration);
        }
        else
        {
            for (int pos = r.Move.FromPos + 1; pos <= r.Move.ToPos; pos++)
                yield return view.MoveTo(GetWorldPoint(player, r.Move.TokenIndex, pos).position, stepDuration);
        }

        foreach (var (cp, ct) in r.Captured)
        {
            var capView = players[cp].tokens[ct];
            capView.ArrivalOrder = ++arrivalCounter;
            yield return capView.MoveTo(GetWorldPoint(cp, ct, 0).position, stepDuration * 2f);
        }

        animating = false;
    }

    private IEnumerator AnimateDiff(LudoGameState from, LudoGameState to)
    {
        animating = true;

        // Movers first (forward changes), then captures (pieces sent home).
        for (int p = 0; p < playerCount; p++)
            for (int t = 0; t < LudoConfig.TokensPerPlayer; t++)
            {
                int o = from.GetToken(p, t), n = to.GetToken(p, t);
                if (n <= o) continue;

                var view = players[p].tokens[t];
                view.ArrivalOrder = ++arrivalCounter;
                view.transform.SetAsLastSibling();

                if (o == 0)
                {
                    yield return view.MoveTo(GetWorldPoint(p, t, n).position, stepDuration);
                }
                else
                {
                    for (int pos = o + 1; pos <= n; pos++)
                        yield return view.MoveTo(GetWorldPoint(p, t, pos).position, stepDuration);
                }
            }

        for (int p = 0; p < playerCount; p++)
            for (int t = 0; t < LudoConfig.TokensPerPlayer; t++)
            {
                int o = from.GetToken(p, t), n = to.GetToken(p, t);
                if (!(n == 0 && o > 0)) continue;

                var view = players[p].tokens[t];
                view.ArrivalOrder = ++arrivalCounter;
                yield return view.MoveTo(GetWorldPoint(p, t, 0).position, stepDuration * 2f);
            }

        animating = false;
    }

    /// If it's my turn and a roll is already pending on the server (resume
    /// mid-turn after reconnect), rebuild the local choice UI.
    private void RestorePendingTurn()
    {
        if (S == null || pendingRoll != null) return;
        if (S.CurrentPlayer != MySeat || S.PendingRoll == -1) return;

        pendingRoll = new RollResult
        {
            DiceValue = S.PendingRoll,
            MustMove = true,
            LegalMoves = LudoEngine.GetLegalMoves(S, MySeat, S.PendingRoll)
        };
        ShowChoices(MySeat);
    }

    private static bool StatesEqual(LudoGameState a, LudoGameState b)
    {
        if (a == null || b == null) return false;
        if (a.TurnNumber != b.TurnNumber) return false;
        for (int i = 0; i < a.Tokens.Length; i++)
            if (a.Tokens[i] != b.Tokens[i]) return false;
        return true;
    }

    // ======================================================== UI HOOKS

    public async void OnDiceButtonPressed(int player)
    {
        if (gameOver || animating || pendingRoll != null || S == null) return;
        if (player != S.CurrentPlayer) return;

        if (mode == GameMode.Online)
        {
            if (player != MySeat) return;      // can only roll for myself

            try
            {
                var resp = await online.SubmitRoll();
                var slot = profiles.GetSlotForPlayer(player);
                if (slot != null) slot.SetDiceValue(resp.roll);

                if (!resp.mustMove) return;    // turn advanced server-side

                pendingRoll = new RollResult
                {
                    DiceValue = resp.roll,
                    MustMove = true,
                    LegalMoves = resp.legalMoves
                };
                ShowChoices(player);
            }
            catch { UIManager.I.ShowError("error.network"); }
            return;
        }

        // ---- offline
        pendingRoll = game.RollDice();
        ShowDice(player, pendingRoll.DiceValue);

        if (!pendingRoll.MustMove)
        {
            pendingRoll = null;
            RefreshTurnState();
            return;
        }
        ShowChoices(player);
    }

    private void ShowChoices(int player)
    {
        foreach (var m in pendingRoll.LegalMoves)
            players[player].tokens[m.TokenIndex].SetHighlight(true);

        if (autoMoveWhenNoChoice && IsChoiceMeaningless(pendingRoll.LegalMoves))
            StartCoroutine(AutoMoveSingle(player, pendingRoll.LegalMoves[0].TokenIndex));
    }

    private static bool IsChoiceMeaningless(List<LudoMove> moves)
    {
        for (int i = 1; i < moves.Count; i++)
            if (moves[i].FromPos != moves[0].FromPos ||
                moves[i].ToPos   != moves[0].ToPos)
                return false;
        return true;
    }

    private IEnumerator AutoMoveSingle(int player, int tokenIndex)
    {
        yield return new WaitForSeconds(autoMoveDelay);
        if (pendingRoll != null && S != null && player == S.CurrentPlayer)
            OnTokenClicked(player, tokenIndex);
    }

    public async void OnTokenClicked(int player, int tokenIndex)
    {
        if (gameOver || animating || pendingRoll == null || S == null) return;
        if (player != S.CurrentPlayer) return;
        if (mode == GameMode.Online && player != MySeat) return;

        bool legal = false;
        foreach (var m in pendingRoll.LegalMoves)
            if (m.TokenIndex == tokenIndex) legal = true;
        if (!legal) return;

        ClearHighlights(player);

        if (mode == GameMode.Online)
        {
            var roll = pendingRoll;
            pendingRoll = null;
            try
            {
                await online.SubmitMove(tokenIndex);
                // Animation happens via ProcessRemote when the doc applies.
            }
            catch
            {
                pendingRoll = roll;            // restore choice on failure
                ShowChoices(player);
                UIManager.I.ShowError("error.network");
            }
            return;
        }

        // ---- offline
        var result = game.MoveToken(tokenIndex);
        pendingRoll = null;
        players[player].tokens[tokenIndex].ArrivalOrder = ++arrivalCounter;
        StartCoroutine(AnimateMove(player, tokenIndex, result));
    }

    // ============================================== OFFLINE ANIMATION

    private IEnumerator AnimateMove(int player, int tokenIndex, MoveResult r)
    {
        animating = true;
        var view = players[player].tokens[tokenIndex];
        view.transform.SetAsLastSibling();

        if (r.Move.LeavesBase)
        {
            yield return view.MoveTo(GetWorldPoint(player, tokenIndex, 1).position, stepDuration);
        }
        else
        {
            for (int pos = r.Move.FromPos + 1; pos <= r.Move.ToPos; pos++)
                yield return view.MoveTo(GetWorldPoint(player, tokenIndex, pos).position, stepDuration);
        }

        foreach (var (cp, ct) in r.Captured)
        {
            var capView = players[cp].tokens[ct];
            capView.ArrivalOrder = ++arrivalCounter;
            yield return capView.MoveTo(GetWorldPoint(cp, ct, 0).position, stepDuration * 2f);
        }

        animating = false;
        RefreshBoardVisuals();

        if (r.GameOver)
        {
            gameOver = true;
            Debug.Log($"GAME OVER! Finish order: {string.Join(", ", game.State.FinishedPlayers)}");
            profiles.DisableAll();
            yield break;
        }

        RefreshTurnState();
    }

    // ============================================= VISUAL REFRESH PASS

    public void RefreshBoardVisuals()
    {
        if (S == null) return;
        var shown = mode == GameMode.Online && lastRendered != null ? lastRendered : S;

        var groups = new Dictionary<Transform, List<LudoTokenView>>();
        for (int p = 0; p < playerCount; p++)
            for (int t = 0; t < LudoConfig.TokensPerPlayer; t++)
            {
                var point = GetWorldPoint(p, t, shown.GetToken(p, t));
                if (!groups.TryGetValue(point, out var list))
                    groups[point] = list = new List<LudoTokenView>();
                list.Add(players[p].tokens[t]);
            }

        foreach (var kv in groups)
        {
            var list = kv.Value;
            list.Sort((a, b) => a.ArrivalOrder.CompareTo(b.ArrivalOrder));
            for (int i = 0; i < list.Count; i++)
            {
                list[i].transform.position =
                    kv.Key.position + (Vector3)(stackOffset * i);
                list[i].transform.SetAsLastSibling();
            }
        }
    }

    public void RefreshTurnState()
    {
        if (S == null || gameOver) return;
        int cur = S.CurrentPlayer;

        profiles.SetCurrentPlayer(cur, mode == GameMode.Online ? MySeat : -1);

        for (int p = 0; p < playerCount; p++)
            foreach (var tok in players[p].tokens)
                tok.SetTurnState(p == cur, activeScale);

        if (mode == GameMode.Offline) CheckBotTurn();
    }

    // ============================================== BOTS (OFFLINE ONLY)

    private void CheckBotTurn()
    {
        if (gameOver || botThinking || animating || pendingRoll != null) return;
        int cur = game.State.CurrentPlayer;
        if (players[cur].isBot)
            StartCoroutine(BotPlay(cur));
    }

    private IEnumerator BotPlay(int player)
    {
        botThinking = true;

        yield return new WaitForSeconds(botRollDelay);
        if (gameOver || player != game.State.CurrentPlayer)
        { botThinking = false; yield break; }

        OnDiceButtonPressed(player);

        if (pendingRoll != null && !IsChoiceMeaningless(pendingRoll.LegalMoves))
        {
            yield return new WaitForSeconds(botMoveDelay);
            if (pendingRoll != null && player == game.State.CurrentPlayer)
            {
                int token = LudoBot.ChooseMove(game.State, player, pendingRoll.LegalMoves);
                OnTokenClicked(player, token);
            }
        }

        botThinking = false;
        if (!animating && pendingRoll == null)
            CheckBotTurn();
    }

    // ========================================================= HELPERS

    public Transform GetWorldPoint(int player, int tokenIndex, int enginePos)
    {
        if (enginePos == 0)
            return players[player].yardPoints[tokenIndex];
        if (enginePos <= LudoConfig.TrackEnd)
            return boardTrack[(LudoConfig.StartOffset[player] + enginePos - 1)
                              % LudoConfig.TrackLength];
        return players[player].winPoints[enginePos - LudoConfig.HomeColumnStart];
    }

    private void ShowDice(int player, int value)
    {
        var slot = profiles.GetSlotForPlayer(player);
        if (slot != null) slot.SetDiceValue(value);
        Debug.Log($"Player {player} rolled {value}");
    }

    private void ClearHighlights(int player)
    {
        foreach (var t in players[player].tokens) t.SetHighlight(false);
    }
}
