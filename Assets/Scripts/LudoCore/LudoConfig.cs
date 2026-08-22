// LudoConfig.cs
// Pure C# — no Unity dependencies. Board layout constants and rule toggles.
//
// Position encoding (per-token, RELATIVE to its owner):
//   0        = base (yard, not yet on the board)
//   1..51    = main track (1 = the player's own start cell)
//   52..56   = home column (5 colored cells)
//   57       = home (finished)
//
// Absolute track index (for captures) = (StartOffset[player] + rel - 1) % 52

namespace LudoCore
{
    public static class LudoConfig
    {
        public const int TokensPerPlayer = 4;
        public const int TrackLength = 52;   // shared main track cells
        public const int TrackEnd = 51;   // last relative track position
        public const int HomeColumnStart = 52;   // first home-column position
        public const int HomePosition = 57;   // finished
        public const int MaxPlayers = 4;

        // Where each player's "start" cell sits on the absolute track.
        // Player 0 = Red, 1 = Green, 2 = Yellow, 3 = Blue (adjust to taste).
        public static readonly int[] StartOffset = { 0, 13, 26, 39 };

        // Absolute cells where tokens can never be captured:
        // the four start cells + the four star cells.
        public static readonly int[] SafeCells = { 0, 8, 13, 21, 26, 34, 39, 47 };

        // ---- Rule toggles (keep identical in Cloud Code later) ----
        public const bool ExtraTurnOnSix = true;
        public const bool ExtraTurnOnCapture = true;
        public const bool ExtraTurnOnTokenHome = true;
        public const bool ThreeSixesForfeitsTurn = true;
        public const bool RequireSixToLeaveBase = true;
        public const bool RequireExactRollHome = true;

        // ---- Pity dice (lives in the DICE layer, never in LudoEngine) ----
        // Only when a player is "stuck" (all unfinished pieces in the yard).
        // PityCurve[pityLevel] = P(six) for that stuck roll; levels beyond the
        // end use the last value. ANY six resets pity to zero.
        // Level 0 is the FIRST stuck roll — kept fair on purpose.
        public const bool PityDiceEnabled = true;
        public static readonly float[] PityCurve = { 1f / 6f, 0.30f, 0.60f, 0.90f };

        public static bool IsSafeCell(int absoluteCell)
        {
            for (int i = 0; i < SafeCells.Length; i++)
                if (SafeCells[i] == absoluteCell) return true;
            return false;
        }

        /// Convert a relative track position (1..51) to an absolute cell (0..51).
        public static int ToAbsolute(int player, int relativePos)
        {
            return (StartOffset[player] + relativePos - 1) % TrackLength;
        }
    }
}