// LudoGameManager.cs
// Attach to an empty GameObject named "GameManager".
//
// Setup checklist:
//  - boardTrack: 52 transforms, index 0 = player 0 (Blue) start cell, travel order
//  - players[p]: 4 yard points, 6 win points (52..57), 4 LudoTokenView
//    (dice UI now lives in PlayerProfileSlot, not here)
//  - profiles: the ProfileSeatMapper with the 4 fixed corner slots
//  - Put ALL pieces under one shared parent so stack sorting works.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LudoCore;

public class LudoGameManager : MonoBehaviour
{
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
        public bool isBot;               // seat driven by LudoBot
    }
    public PlayerPoints[] players;       // size = playerCount

    [Header("Profiles")]
    public ProfileSeatMapper profiles;

    [Header("Animation")]
    public float stepDuration = 0.18f;

    [Header("Stacking")]
    [Tooltip("Pixel offset per piece when several share a cell (up & right)")]
    public Vector2 stackOffset = new Vector2(8f, 8f);

    [Header("Turn styling")]
    [Tooltip("Scale multiplier for the current player's pieces")]
    public float activeScale = 1.15f;

    [Header("Gameplay helpers")]
    [Tooltip("Auto-play when the player has no meaningful choice (single move, or all moves identical e.g. stacked pieces / all leaving the yard)")]
    public bool autoMoveWhenNoChoice = true;
    [Tooltip("Delay before the auto-move so the player reads the dice first")]
    public float autoMoveDelay = 0.3f;

    [Header("Bots")]
    [Tooltip("Thinking time before a bot presses the dice")]
    public float botRollDelay = 0.7f;
    [Tooltip("Thinking time before a bot picks its piece")]
    public float botMoveDelay = 0.5f;

    private LudoLocalGame game;
    private RollResult pendingRoll;
    private bool animating;
    private int arrivalCounter;
    private bool gameOver;
    private bool botThinking;

    void Start()
    {
        game = new LudoLocalGame(playerCount);

        for (int p = 0; p < playerCount; p++)
            for (int t = 0; t < LudoConfig.TokensPerPlayer; t++)
            {
                players[p].tokens[t].Init(this, p, t);
                players[p].tokens[t].ArrivalOrder = ++arrivalCounter;
            }

        profiles.Init(this);   // wires slot dice buttons + first Rebind
        RefreshBoardVisuals();
        RefreshTurnState();
    }

    // ------------------------------------------------------------ UI HOOKS

    /// Called by PlayerProfileSlot when its dice button is pressed.
    public void OnDiceButtonPressed(int player)
    {
        if (gameOver || animating || pendingRoll != null) return;
        if (player != game.State.CurrentPlayer) return;

        pendingRoll = game.RollDice();
        ShowDice(player, pendingRoll.DiceValue);

        if (!pendingRoll.MustMove)
        {
            pendingRoll = null;   // no legal move / 3rd six: turn already advanced
            RefreshTurnState();
            return;
        }

        foreach (var m in pendingRoll.LegalMoves)
            players[player].tokens[m.TokenIndex].SetHighlight(true);

        if (autoMoveWhenNoChoice && IsChoiceMeaningless(pendingRoll.LegalMoves))
            StartCoroutine(AutoMoveSingle(player, pendingRoll.LegalMoves[0].TokenIndex));
    }

    /// True when every legal move has the same From->To (stacked pieces,
    /// all-in-yard six, or literally one option): nothing to decide.
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
        // Still valid? (paranoia: state can't change here, but cheap to check)
        if (pendingRoll != null && player == game.State.CurrentPlayer)
            OnTokenClicked(player, tokenIndex);
    }

    public void OnTokenClicked(int player, int tokenIndex)
    {
        if (gameOver || animating || pendingRoll == null) return;
        if (player != game.State.CurrentPlayer) return;

        bool legal = false;
        foreach (var m in pendingRoll.LegalMoves)
            if (m.TokenIndex == tokenIndex) legal = true;
        if (!legal) return;

        ClearHighlights(player);
        var result = game.MoveToken(tokenIndex);
        pendingRoll = null;

        players[player].tokens[tokenIndex].ArrivalOrder = ++arrivalCounter;
        StartCoroutine(AnimateMove(player, tokenIndex, result));
    }

    // ----------------------------------------------------------- ANIMATION

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

    // ------------------------------------------------- VISUAL REFRESH PASS

    /// Re-seats every piece on its point, offsetting and sorting stacks.
    /// Public: BoardOrientation calls this after rotating the board.
    public void RefreshBoardVisuals()
    {
        var groups = new Dictionary<Transform, List<LudoTokenView>>();

        for (int p = 0; p < playerCount; p++)
        {
            for (int t = 0; t < LudoConfig.TokensPerPlayer; t++)
            {
                var point = GetWorldPoint(p, t, game.State.GetToken(p, t));
                if (!groups.TryGetValue(point, out var list))
                    groups[point] = list = new List<LudoTokenView>();
                list.Add(players[p].tokens[t]);
            }
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

    /// Enables the right slot's dice button and styles pieces for the turn.
    /// Public: ProfileSeatMapper re-calls this after every rebind.
    public void RefreshTurnState()
    {
        if (game == null || gameOver) return;
        int cur = game.State.CurrentPlayer;

        profiles.SetCurrentPlayer(cur);

        for (int p = 0; p < playerCount; p++)
            foreach (var tok in players[p].tokens)
                tok.SetTurnState(p == cur, activeScale);

        CheckBotTurn();
    }

    // -------------------------------------------------------------- BOTS

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

        // Roll produced a meaningful choice? Pick with the bot brain.
        // (No-choice rolls are handled by the auto-move; rolls with no legal
        //  move already advanced the turn.)
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
        // If the turn already passed to another bot (e.g. roll had no legal
        // move), chain into it.
        if (!animating && pendingRoll == null)
            CheckBotTurn();
    }

    // ------------------------------------------------------------- HELPERS

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
