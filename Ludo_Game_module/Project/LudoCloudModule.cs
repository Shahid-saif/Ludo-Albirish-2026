// LudoCloudModule.cs  (v2 — server-side bots)
// Module project files: this + UNCHANGED LudoConfig.cs, LudoGameState.cs,
// LudoEngine.cs, and now LudoBot.cs (copy from Unity, unchanged).
//
// Bots are SEATS marked in MatchDoc.Bots. The server is request-driven, so
// bot turns run lazily ("RunBotsIfNeeded"): whenever any endpoint touches
// the match and the current player is a bot, the server plays the bot's
// turn(s) right there. The waiting human's next poll both triggers and
// receives the bot's moves — the client needs zero changes.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudSave.Model;
using LudoCore;

public class MatchAction
{
    public long Seq;       // global, increasing
    public string T;       // "R" = roll, "M" = move
    public int Seat;
    public int Dice;
    public int Token;      // -1 for rolls
}

public class MatchDoc
{
    public string MatchId;
    public string Status = "waiting";          // waiting | active | finished
    public int MaxPlayers;
    public List<string> PlayerIds = new();
    public List<string> PlayerNames = new();
    public List<bool> Bots = new();
    public LudoGameState State;
    public int[] PityLevels;
    public int LastRoll = -1;
    public long UpdatedAt;
    public long TurnDeadline;                  // unused until AFK phase

    /// Recent action history (ring buffer) — lets clients REPLAY turns
    /// action-by-action (show dice, then animate) instead of snapping.
    public List<MatchAction> Actions = new();
    public long ActionSeq;
}

public class LudoCloudModule
{
    private const string KeyPrefix = "match-";
    private const int MaxBotActionsPerRequest = 60;   // safety cap

    private static readonly string[] BotNames =
        { "Bot Omar", "Bot Sara", "Bot Zaid", "Bot Lina" };

    // ------------------------------------------------------------ CREATE

    [CloudCodeFunction("CreateMatch")]
    public async Task<string> CreateMatch(IExecutionContext ctx, IGameApiClient api,
        int maxPlayers, string playerName, int botCount)
    {
        if (maxPlayers < 2 || maxPlayers > 4)
            throw new Exception("maxPlayers must be 2-4");
        botCount = Math.Clamp(botCount, 0, maxPlayers - 1);

        var doc = new MatchDoc
        {
            MatchId = "M-" + Guid.NewGuid().ToString("N").Substring(0, 8),
            MaxPlayers = maxPlayers,
            UpdatedAt = Now()
        };
        doc.PlayerIds.Add(ctx.PlayerId);
        doc.PlayerNames.Add(Sanitize(playerName));
        doc.Bots.Add(false);

        for (int i = 0; i < botCount; i++)
        {
            doc.PlayerIds.Add("BOT-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            doc.PlayerNames.Add(BotNames[i % BotNames.Length]);
            doc.Bots.Add(true);
        }

        ActivateIfFull(doc);
        RunBotsIfNeeded(doc);      // in case seat 0... never a bot, but cheap
        await Save(ctx, api, doc);
        return JsonConvert.SerializeObject(doc);
    }

    // -------------------------------------------------------------- JOIN

    [CloudCodeFunction("JoinMatch")]
    public async Task<string> JoinMatch(IExecutionContext ctx, IGameApiClient api,
        IPushClient pushClient, string matchId, string playerName)
    {
        var doc = await Load(ctx, api, matchId);

        if (!doc.PlayerIds.Contains(ctx.PlayerId))
        {
            if (doc.Status != "waiting") throw new Exception("Match already started");
            if (doc.PlayerIds.Count >= doc.MaxPlayers) throw new Exception("Match full");

            doc.PlayerIds.Add(ctx.PlayerId);
            doc.PlayerNames.Add(Sanitize(playerName));
            doc.Bots.Add(false);

            ActivateIfFull(doc);
            RunBotsIfNeeded(doc);
            doc.UpdatedAt = Now();
            await Save(ctx, api, doc);
            await NotifyOthers(ctx, pushClient, doc);
        }
        return JsonConvert.SerializeObject(doc);
    }

    // --------------------------------------------------------------- GET

    [CloudCodeFunction("GetMatch")]
    public async Task<string> GetMatch(IExecutionContext ctx, IGameApiClient api,
        IPushClient pushClient, string matchId)
    {
        var doc = await Load(ctx, api, matchId);

        // Lazy bot execution: a waiting human's poll advances bot turns.
        if (RunBotsIfNeeded(doc))
        {
            doc.UpdatedAt = Now();
            await Save(ctx, api, doc);
            await NotifyOthers(ctx, pushClient, doc);
        }
        return JsonConvert.SerializeObject(doc);
    }

    // -------------------------------------------------------------- ROLL

    [CloudCodeFunction("SubmitRoll")]
    public async Task<string> SubmitRoll(IExecutionContext ctx, IGameApiClient api,
        IPushClient pushClient, string matchId)
    {
        var doc = await Load(ctx, api, matchId);
        int seat = RequireMyTurn(ctx, doc);
        if (doc.State.PendingRoll != -1) throw new Exception("Move pending");

        int dice = GenerateDice(doc, seat);
        UpdatePity(doc, seat, dice);
        var rollResult = LudoEngine.ApplyRoll(doc.State, dice);
        doc.LastRoll = dice;
        Record(doc, "R", seat, dice, -1);

        // No legal move => turn may have advanced onto a bot.
        if (!rollResult.MustMove) RunBotsIfNeeded(doc);

        doc.UpdatedAt = Now();
        await Save(ctx, api, doc);
        await NotifyOthers(ctx, pushClient, doc);

        return JsonConvert.SerializeObject(new
        {
            match = doc,
            roll = dice,
            mustMove = rollResult.MustMove,
            legalMoves = rollResult.LegalMoves
        });
    }

    // -------------------------------------------------------------- MOVE

    [CloudCodeFunction("SubmitMove")]
    public async Task<string> SubmitMove(IExecutionContext ctx, IGameApiClient api,
        IPushClient pushClient, string matchId, int tokenIndex)
    {
        var doc = await Load(ctx, api, matchId);
        int seat = RequireMyTurn(ctx, doc);
        if (doc.State.PendingRoll == -1) throw new Exception("Roll first");

        int pendingDice = doc.State.PendingRoll;
        var moveResult = LudoEngine.ApplyMove(doc.State, tokenIndex);
        Record(doc, "M", seat, pendingDice, tokenIndex);
        if (moveResult.GameOver) doc.Status = "finished";

        // My move may hand the turn to a bot: play it now so my own response
        // already contains the bot's reply.
        RunBotsIfNeeded(doc);

        doc.UpdatedAt = Now();
        await Save(ctx, api, doc);
        await NotifyOthers(ctx, pushClient, doc);

        return JsonConvert.SerializeObject(new { match = doc, result = moveResult });
    }

    /// Best-effort push "match-updated" to every OTHER human in the match.
    /// The caller gets the fresh state in their own response; bots need none.
    private static async Task NotifyOthers(IExecutionContext ctx, IPushClient push,
        MatchDoc doc)
    {
        for (int i = 0; i < doc.PlayerIds.Count; i++)
        {
            if (doc.Bots[i]) continue;
            string pid = doc.PlayerIds[i];
            if (pid == ctx.PlayerId) continue;
            try
            {
                await push.SendPlayerMessageAsync(ctx, doc.MatchId,
                    "match-updated", pid);
            }
            catch { /* push is best-effort; polling is the safety net */ }
        }
    }

    // ====================================================== BOT ENGINE

    /// While the current player is a bot, play its full turn(s) — including
    /// extra turns and bot-after-bot chains. Returns true if anything changed.
    private static bool RunBotsIfNeeded(MatchDoc doc)
    {
        if (doc.Status != "active") return false;

        bool changed = false;
        int safety = 0;

        while (doc.Status == "active"
               && doc.Bots[doc.State.CurrentPlayer]
               && safety++ < MaxBotActionsPerRequest)
        {
            int seat = doc.State.CurrentPlayer;

            int dice = GenerateDice(doc, seat);
            UpdatePity(doc, seat, dice);
            var roll = LudoEngine.ApplyRoll(doc.State, dice);
            doc.LastRoll = dice;
            Record(doc, "R", seat, dice, -1);
            changed = true;

            if (!roll.MustMove) continue;   // forfeit/no-move: turn advanced

            int token = LudoBot.ChooseMove(doc.State, seat, roll.LegalMoves);
            var move = LudoEngine.ApplyMove(doc.State, token);
            Record(doc, "M", seat, dice, token);
            if (move.GameOver) doc.Status = "finished";
        }
        return changed;
    }

    private static void Record(MatchDoc doc, string t, int seat, int dice, int token)
    {
        doc.Actions.Add(new MatchAction
        {
            Seq = ++doc.ActionSeq, T = t, Seat = seat, Dice = dice, Token = token
        });
        const int keep = 40;
        if (doc.Actions.Count > keep)
            doc.Actions.RemoveRange(0, doc.Actions.Count - keep);
    }

    private static void ActivateIfFull(MatchDoc doc)
    {
        if (doc.Status == "waiting" && doc.PlayerIds.Count == doc.MaxPlayers)
        {
            doc.Status = "active";
            doc.State = LudoGameState.NewGame(doc.MaxPlayers);
            doc.PityLevels = new int[doc.MaxPlayers];
        }
    }

    // ===================================================== SERVER DICE

    private static int GenerateDice(MatchDoc doc, int seat)
    {
        var rng = Random.Shared;
        if (LudoConfig.PityDiceEnabled && IsStuck(doc.State, seat)
            && doc.PityLevels[seat] > 0)
        {
            var curve = LudoConfig.PityCurve;
            double pSix = curve[Math.Min(doc.PityLevels[seat], curve.Length - 1)];
            if (rng.NextDouble() < pSix) return 6;
            return rng.Next(1, 6);
        }
        return rng.Next(1, 7);
    }

    private static void UpdatePity(MatchDoc doc, int seat, int dice)
    {
        if (dice == 6) doc.PityLevels[seat] = 0;
        else if (IsStuck(doc.State, seat)) doc.PityLevels[seat]++;
    }

    private static bool IsStuck(LudoGameState s, int player)
    {
        bool hasYardPiece = false;
        for (int t = 0; t < LudoConfig.TokensPerPlayer; t++)
        {
            int pos = s.GetToken(player, t);
            if (pos == 0) hasYardPiece = true;
            else if (pos != LudoConfig.HomePosition) return false;
        }
        return hasYardPiece;
    }

    // ========================================================= HELPERS

    private static int RequireMyTurn(IExecutionContext ctx, MatchDoc doc)
    {
        if (doc.Status != "active") throw new Exception("Match not active");
        int seat = doc.PlayerIds.IndexOf(ctx.PlayerId);
        if (seat < 0) throw new Exception("Not in this match");
        if (seat != doc.State.CurrentPlayer) throw new Exception("Not your turn");
        if (doc.Bots[seat]) throw new Exception("Bot seat");
        return seat;
    }

    private static async Task<MatchDoc> Load(IExecutionContext ctx, IGameApiClient api,
        string matchId)
    {
        var resp = await api.CloudSaveData.GetCustomItemsAsync(
            ctx, ctx.ServiceToken, ctx.ProjectId, matchId,
            new List<string> { KeyPrefix + matchId });

        var items = resp.Data.Results;
        if (items == null || items.Count == 0) throw new Exception("Match not found");
        return JsonConvert.DeserializeObject<MatchDoc>(items[0].Value.ToString());
    }

    private static async Task Save(IExecutionContext ctx, IGameApiClient api,
        MatchDoc doc)
    {
        await api.CloudSaveData.SetCustomItemAsync(
            ctx, ctx.ServiceToken, ctx.ProjectId, doc.MatchId,
            new SetItemBody(KeyPrefix + doc.MatchId,
                JsonConvert.SerializeObject(doc)));
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Player";
        name = name.Trim();
        return name.Length > 20 ? name.Substring(0, 20) : name;
    }
}
