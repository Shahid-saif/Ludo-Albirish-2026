// IMatchService.cs
// The transport seam: the game talks ONLY to this interface, never to
// Cloud Code directly. CloudMatchService implements it for online;
// a LocalMatchService can wrap LudoLocalGame for offline with the same
// shape. Swapping backends (e.g. Firebase someday) = one new class.

using System;
using System.Threading.Tasks;

public interface IMatchService
{
    /// Latest known match document (null before create/join).
    MatchDoc Current { get; }

    /// My seat index in the current match (-1 if unknown).
    int MySeat { get; }

    /// Fired whenever a NEWER match state arrives (my own calls or polling).
    event Action<MatchDoc> OnMatchUpdated;

    Task<MatchDoc> CreateMatch(int maxPlayers, string playerName, int botCount = 0);
    Task<MatchDoc> JoinMatch(string matchId, string playerName);
    Task<MatchDoc> Refresh();                       // one-shot GetMatch
    Task<RollResponse> SubmitRoll();
    Task<MoveResponse> SubmitMove(int tokenIndex);

    /// Poll GetMatch on an interval while it's NOT my turn / match waiting.
    void StartPolling(float intervalSeconds);
    void StopPolling();
}
