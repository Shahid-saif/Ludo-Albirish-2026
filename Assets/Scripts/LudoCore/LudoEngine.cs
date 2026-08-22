// LudoEngine.cs
// Pure C#, fully deterministic rules engine.
// Dice randomness is INJECTED (ApplyRoll takes the value) — the engine never
// generates randomness itself. Locally you roll with System.Random; in
// multiplayer the Cloud Code function rolls and calls the same logic.
//
// Flow per turn:
//   1. ApplyRoll(state, diceValue)        -> RollResult
//   2. If RollResult.MustMove: pick from LegalMoves, call ApplyMove(...)
//      Else: turn already advanced (no legal move, or 3rd six forfeit).

using System;
using System.Collections.Generic;

namespace LudoCore
{
    public struct LudoMove
    {
        public int TokenIndex;   // 0..3
        public int FromPos;      // relative
        public int ToPos;        // relative
        public bool LeavesBase;
        public bool ReachesHome;
    }

    public class RollResult
    {
        public int DiceValue;
        public bool MustMove;             // player has at least one legal move
        public bool TurnForfeited;        // third consecutive six
        public List<LudoMove> LegalMoves = new List<LudoMove>();
    }

    public class MoveResult
    {
        public LudoMove Move;
        public bool CapturedOpponent;
        /// All tokens captured by this move (player, tokenIndex).
        /// More than one entry if opponent tokens were stacked on the cell.
        public List<(int player, int token)> Captured = new List<(int, int)>();
        public bool TokenReachedHome;
        public bool PlayerFinished;
        public bool GameOver;
        public bool ExtraTurn;
        public int NextPlayer;
    }

    public static class LudoEngine
    {
        // ------------------------------------------------------------ ROLL

        public static RollResult ApplyRoll(LudoGameState s, int diceValue)
        {
            if (diceValue < 1 || diceValue > 6)
                throw new ArgumentException("Dice value must be 1-6");
            if (s.PendingRoll != -1)
                throw new InvalidOperationException("A roll is already pending a move");
            if (s.IsGameOver)
                throw new InvalidOperationException("Game is over");

            var result = new RollResult { DiceValue = diceValue };

            if (diceValue == 6)
            {
                s.ConsecutiveSixes++;
                if (LudoConfig.ThreeSixesForfeitsTurn && s.ConsecutiveSixes >= 3)
                {
                    result.TurnForfeited = true;
                    AdvanceTurn(s);
                    s.TurnNumber++;
                    return result;
                }
            }
            else
            {
                s.ConsecutiveSixes = 0;
            }

            result.LegalMoves = GetLegalMoves(s, s.CurrentPlayer, diceValue);

            if (result.LegalMoves.Count == 0)
            {
                // No legal move: a six still grants a re-roll, otherwise pass.
                if (!(diceValue == 6 && LudoConfig.ExtraTurnOnSix))
                    AdvanceTurn(s);
                s.TurnNumber++;
                return result;
            }

            result.MustMove = true;
            s.PendingRoll = diceValue;
            return result;
        }

        // ----------------------------------------------------- LEGAL MOVES

        public static List<LudoMove> GetLegalMoves(LudoGameState s, int player, int dice)
        {
            var moves = new List<LudoMove>();

            for (int t = 0; t < LudoConfig.TokensPerPlayer; t++)
            {
                int pos = s.GetToken(player, t);

                if (pos == LudoConfig.HomePosition) continue; // already home

                if (pos == 0) // in base
                {
                    if (!LudoConfig.RequireSixToLeaveBase || dice == 6)
                    {
                        moves.Add(new LudoMove
                        {
                            TokenIndex = t, FromPos = 0, ToPos = 1, LeavesBase = true
                        });
                    }
                    continue;
                }

                int target = pos + dice;

                if (target > LudoConfig.HomePosition)
                {
                    if (LudoConfig.RequireExactRollHome) continue; // overshoot
                    target = LudoConfig.HomePosition;
                }

                moves.Add(new LudoMove
                {
                    TokenIndex = t,
                    FromPos = pos,
                    ToPos = target,
                    ReachesHome = target == LudoConfig.HomePosition
                });
            }
            return moves;
        }

        // ------------------------------------------------------------ MOVE

        public static MoveResult ApplyMove(LudoGameState s, int tokenIndex)
        {
            if (s.PendingRoll == -1)
                throw new InvalidOperationException("Roll the dice first");

            int player = s.CurrentPlayer;
            int dice = s.PendingRoll;

            // Re-derive legality — never trust the caller (vital for Cloud Code).
            var legal = GetLegalMoves(s, player, dice);
            LudoMove move = default;
            bool found = false;
            foreach (var m in legal)
            {
                if (m.TokenIndex == tokenIndex) { move = m; found = true; break; }
            }
            if (!found)
                throw new InvalidOperationException($"Token {tokenIndex} has no legal move");

            var result = new MoveResult { Move = move };

            s.SetToken(player, tokenIndex, move.ToPos);
            s.PendingRoll = -1;

            // Capture check — only on the shared main track.
            if (move.ToPos >= 1 && move.ToPos <= LudoConfig.TrackEnd)
            {
                int abs = LudoConfig.ToAbsolute(player, move.ToPos);
                if (!LudoConfig.IsSafeCell(abs))
                    TryCapture(s, player, abs, result);
            }

            if (move.ReachesHome)
            {
                result.TokenReachedHome = true;
                if (s.HasPlayerFinished(player))
                {
                    result.PlayerFinished = true;
                    s.FinishedPlayers.Add(player);
                    result.GameOver = s.IsGameOver;
                }
            }

            // Extra-turn logic (finished players never get extra turns).
            bool extra =
                !result.PlayerFinished &&
                ((dice == 6 && LudoConfig.ExtraTurnOnSix) ||
                 (result.CapturedOpponent && LudoConfig.ExtraTurnOnCapture) ||
                 (result.TokenReachedHome && LudoConfig.ExtraTurnOnTokenHome));

            if (result.GameOver)
                extra = false;
            else if (!extra)
                AdvanceTurn(s);

            result.ExtraTurn = extra;
            result.NextPlayer = s.CurrentPlayer;
            s.TurnNumber++;
            return result;
        }

        // --------------------------------------------------------- HELPERS

        private static void TryCapture(LudoGameState s, int mover, int absCell, MoveResult result)
        {
            for (int p = 0; p < s.PlayerCount; p++)
            {
                if (p == mover) continue;
                for (int t = 0; t < LudoConfig.TokensPerPlayer; t++)
                {
                    int pos = s.GetToken(p, t);
                    if (pos < 1 || pos > LudoConfig.TrackEnd) continue; // base/home column
                    if (LudoConfig.ToAbsolute(p, pos) == absCell)
                    {
                        s.SetToken(p, t, 0); // back to base
                        result.CapturedOpponent = true;
                        result.Captured.Add((p, t));
                        // no early return: capture ALL stacked opponent tokens
                    }
                }
            }
        }

        private static void AdvanceTurn(LudoGameState s)
        {
            s.ConsecutiveSixes = 0;
            s.PendingRoll = -1;
            do
            {
                s.CurrentPlayer = (s.CurrentPlayer + 1) % s.PlayerCount;
            } while (s.HasPlayerFinished(s.CurrentPlayer) && !s.IsGameOver);
        }
    }
}
