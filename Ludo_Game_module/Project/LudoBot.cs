// LudoBot.cs  (v2 — threat-delta scoring)
// Pure C# — no Unity. Chooses a move for a player given the legal moves.
//
// Key idea vs v1: threats are PROBABILITIES, not booleans, and every move is
// scored by how much it REDUCES the moved piece's capture risk:
//     riskDelta = P(captured at From) - P(captured at To)
// A piece under fire from 3 attackers (~42% per round) generates a large
// positive riskDelta when it escapes — so rescuing it outscores nudging an
// already-safe piece, even if the escape square isn't perfectly safe.
//
// Priorities (via weights): capture > finish > big rescue > leave yard >
// safe landing > progress.

using System;
using System.Collections.Generic;

namespace LudoCore
{
    public static class LudoBot
    {
        const float W_CAPTURE_BASE = 1000f;
        const float W_CAPTURE_PROG = 8f;     // x victim's relative position
        const float W_CAPTURE_LEADER = 60f;    // x victim owner's finished pieces
        const float W_FINISH = 900f;
        const float W_RISK = 900f;   // x (P(from) - P(to)) x progress factor
        const float W_LEAVE_YARD = 380f;
        const float W_SAFE_LANDING = 100f;
        const float W_PROGRESS = 1.5f;   // x destination position

        public static int ChooseMove(LudoGameState s, int player, List<LudoMove> moves)
        {
            int bestToken = moves[0].TokenIndex;
            float bestScore = float.MinValue;

            foreach (var m in moves)
            {
                float score = ScoreMove(s, player, m);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestToken = m.TokenIndex;
                }
            }
            return bestToken;
        }

        // ------------------------------------------------------------------

        private static float ScoreMove(LudoGameState s, int player, LudoMove m)
        {
            float score = 0f;

            bool onTrackAfter = m.ToPos >= 1 && m.ToPos <= LudoConfig.TrackEnd;
            int absTo = onTrackAfter ? LudoConfig.ToAbsolute(player, m.ToPos) : -1;
            bool toSafe = onTrackAfter && LudoConfig.IsSafeCell(absTo);

            // ---- Captures
            if (onTrackAfter && !toSafe)
            {
                foreach (var (vp, vRel) in OpponentsOnCell(s, player, absTo))
                {
                    score += W_CAPTURE_BASE
                           + vRel * W_CAPTURE_PROG
                           + FinishedCount(s, vp) * W_CAPTURE_LEADER;
                }
            }

            // ---- Finishing
            if (m.ToPos == LudoConfig.HomePosition)
                score += W_FINISH;

            // ---- Risk delta: how much safer does the moved piece become?
            // From-risk: pieces in the yard have none. To-risk: home column,
            // finish, and safe cells have none.
            float riskFrom = (m.FromPos >= 1 && m.FromPos <= LudoConfig.TrackEnd)
                ? CaptureProbability(s, player,
                      LudoConfig.ToAbsolute(player, m.FromPos))
                : 0f;

            float riskTo = (onTrackAfter && !toSafe)
                ? CaptureProbability(s, player, absTo)
                : 0f;

            // Progress factor: risk on an advanced piece matters more.
            float progressFactor = 0.5f + Math.Max(m.FromPos, 1) / 51f;
            score += (riskFrom - riskTo) * W_RISK * progressFactor;

            // ---- Leaving the yard
            if (m.LeavesBase)
                score += W_LEAVE_YARD;

            // ---- Safe landing / progress
            if (toSafe)
                score += W_SAFE_LANDING;
            score += m.ToPos * W_PROGRESS;

            return score;
        }

        /// P(at least one opponent piece captures this cell next round)
        /// ~= 1 - (5/6)^k where k = number of pieces within one dice throw.
        /// 1 attacker ~ 17%, 2 ~ 31%, 3 ~ 42%, 4 ~ 52%.
        private static float CaptureProbability(LudoGameState s, int player, int absCell)
        {
            if (LudoConfig.IsSafeCell(absCell)) return 0f;

            int attackers = 0;
            for (int p = 0; p < s.PlayerCount; p++)
            {
                if (p == player) continue;
                for (int t = 0; t < LudoConfig.TokensPerPlayer; t++)
                {
                    int pos = s.GetToken(p, t);
                    if (pos < 1 || pos > LudoConfig.TrackEnd) continue;

                    int absOpp = LudoConfig.ToAbsolute(p, pos);
                    int dist = (absCell - absOpp + LudoConfig.TrackLength)
                               % LudoConfig.TrackLength;

                    if (dist >= 1 && dist <= 6 &&
                        pos + dist <= LudoConfig.TrackEnd)
                        attackers++;
                }
            }
            if (attackers == 0) return 0f;
            return 1f - (float)Math.Pow(5.0 / 6.0, attackers);
        }

        private static IEnumerable<(int, int)> OpponentsOnCell(
            LudoGameState s, int player, int absCell)
        {
            for (int p = 0; p < s.PlayerCount; p++)
            {
                if (p == player) continue;
                for (int t = 0; t < LudoConfig.TokensPerPlayer; t++)
                {
                    int pos = s.GetToken(p, t);
                    if (pos >= 1 && pos <= LudoConfig.TrackEnd &&
                        LudoConfig.ToAbsolute(p, pos) == absCell)
                        yield return (p, pos);
                }
            }
        }

        private static int FinishedCount(LudoGameState s, int player)
        {
            int n = 0;
            for (int t = 0; t < LudoConfig.TokensPerPlayer; t++)
                if (s.GetToken(player, t) == LudoConfig.HomePosition) n++;
            return n;
        }
    }
}