// LudoGameState.cs
// Pure C# — the complete state of a match. Deliberately flat and
// JSON-friendly so the exact same shape can live in Cloud Save later.

using System;
using System.Collections.Generic;

namespace LudoCore
{
    [Serializable]
    public class LudoGameState
    {
        public int PlayerCount;

        /// Tokens[player * 4 + tokenIndex] = relative position (see LudoConfig).
        /// Flat array (not jagged) so JsonUtility / Cloud Save serialize it cleanly.
        public int[] Tokens;

        public int CurrentPlayer;

        /// The pending dice value the current player must act on. -1 = must roll first.
        public int PendingRoll = -1;

        public int ConsecutiveSixes;

        /// Players in finishing order. Game over when Count == PlayerCount - 1.
        public List<int> FinishedPlayers = new List<int>();

        /// Monotonic counter — every applied action increments it.
        /// In multiplayer this catches "stale client" writes.
        public int TurnNumber;

        public static LudoGameState NewGame(int playerCount)
        {
            if (playerCount < 2 || playerCount > LudoConfig.MaxPlayers)
                throw new ArgumentException("Player count must be 2-4");

            return new LudoGameState
            {
                PlayerCount = playerCount,
                Tokens = new int[playerCount * LudoConfig.TokensPerPlayer], // all 0 = base
                CurrentPlayer = 0,
                PendingRoll = -1,
                ConsecutiveSixes = 0,
                TurnNumber = 0
            };
        }

        public int GetToken(int player, int tokenIndex)
            => Tokens[player * LudoConfig.TokensPerPlayer + tokenIndex];

        public void SetToken(int player, int tokenIndex, int pos)
            => Tokens[player * LudoConfig.TokensPerPlayer + tokenIndex] = pos;

        public bool HasPlayerFinished(int player)
        {
            for (int t = 0; t < LudoConfig.TokensPerPlayer; t++)
                if (GetToken(player, t) != LudoConfig.HomePosition) return false;
            return true;
        }

        public bool IsGameOver => FinishedPlayers.Count >= PlayerCount - 1;

        public LudoGameState Clone()
        {
            return new LudoGameState
            {
                PlayerCount = PlayerCount,
                Tokens = (int[])Tokens.Clone(),
                CurrentPlayer = CurrentPlayer,
                PendingRoll = PendingRoll,
                ConsecutiveSixes = ConsecutiveSixes,
                FinishedPlayers = new List<int>(FinishedPlayers),
                TurnNumber = TurnNumber
            };
        }
    }
}
