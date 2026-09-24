// MatchModels.cs
// Client-side mirrors of the Cloud Code module's data. Field names must
// match LudoCloudModule.cs exactly (JSON round-trip).

using System;
using System.Collections.Generic;
using LudoCore;

[Serializable]
public class MatchAction
{
    public long Seq;
    public string T;       // "R" roll, "M" move
    public int Seat;
    public int Dice;
    public int Token;
}

[Serializable]
public class MatchDoc
{
    public string MatchId;
    public string Status;              // waiting | active | finished
    public int MaxPlayers;
    public List<string> PlayerIds = new();
    public List<string> PlayerNames = new();
    public List<bool> Bots = new();
    public LudoGameState State;
    public int LastRoll = -1;
    public long UpdatedAt;
    public long TurnDeadline;
    public List<MatchAction> Actions = new();
    public long ActionSeq;
    // PityLevels intentionally NOT mirrored — the client must never see them.

    public int SeatOf(string playerId) => PlayerIds.IndexOf(playerId);
    public bool IsActive   => Status == "active";
    public bool IsFinished => Status == "finished";
}

[Serializable]
public class RollResponse
{
    public MatchDoc match;
    public int roll;
    public bool mustMove;
    public List<LudoMove> legalMoves;
}

[Serializable]
public class MoveResponse
{
    public MatchDoc match;
    public MoveResult result;
}
