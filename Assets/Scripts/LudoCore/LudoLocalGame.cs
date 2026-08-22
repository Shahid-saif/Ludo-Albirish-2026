// LudoLocalGame.cs
// Offline wrapper. Owns the dice — including the PITY system.
// In step 2, Cloud Code replaces this class with the same logic server-side;
// LudoEngine stays byte-for-byte identical.
//
// Pity rules (see constants in LudoConfig):
//  - Applies ONLY while a player is stuck: all unfinished pieces in the yard.
//  - Each stuck roll that isn't a six raises that player's P(six).
//  - ANY six (natural or boosted) resets that player's pity to 0.
//  - Players with pieces on the board always roll fair dice.

using System;
using System.Collections.Generic;

namespace LudoCore
{
    public class LudoLocalGame
    {
        public LudoGameState State { get; private set; }
        private readonly Random _rng;
        private readonly int[] _pityLevel;   // per player

        public LudoLocalGame(int playerCount, int? seed = null)
        {
            State = LudoGameState.NewGame(playerCount);
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();
            _pityLevel = new int[playerCount];
        }

        public RollResult RollDice()
        {
            int player = State.CurrentPlayer;
            int dice = GenerateDice(player);
            UpdatePity(player, dice);
            return LudoEngine.ApplyRoll(State, dice);
        }

        public MoveResult MoveToken(int tokenIndex)
        {
            return LudoEngine.ApplyMove(State, tokenIndex);
        }

        // ------------------------------------------------------------ DICE

        private int GenerateDice(int player)
        {
            if (LudoConfig.PityDiceEnabled && IsStuck(player) && _pityLevel[player] > 0)
            {
                var curve = LudoConfig.PityCurve;
                double pSix = curve[Math.Min(_pityLevel[player], curve.Length - 1)];

                if (_rng.NextDouble() < pSix) return 6;
                return _rng.Next(1, 6);          // uniform 1..5
            }
            return _rng.Next(1, 7);              // fair 1..6
        }

        private void UpdatePity(int player, int dice)
        {
            if (dice == 6)
                _pityLevel[player] = 0;          // any six resets
            else if (IsStuck(player))
                _pityLevel[player]++;            // suffered another stuck turn
        }

        /// Stuck = has yard pieces and nothing movable on the board.
        private bool IsStuck(int player)
        {
            bool hasYardPiece = false;
            for (int t = 0; t < LudoConfig.TokensPerPlayer; t++)
            {
                int pos = State.GetToken(player, t);
                if (pos == 0) hasYardPiece = true;
                else if (pos != LudoConfig.HomePosition) return false; // on board
            }
            return hasYardPiece;
        }
    }

    /// Plays full games with random legal choices — regression test.
    public static class LudoSimulator
    {
        public static void RunMany(int games, int playerCount, Action<string> log = null)
        {
            log ??= Console.WriteLine;
            var rng = new Random(12345);
            long totalTurns = 0;
            int maxTurns = 0;

            for (int g = 0; g < games; g++)
            {
                var game = new LudoLocalGame(playerCount, seed: rng.Next());
                int safety = 0;

                while (!game.State.IsGameOver)
                {
                    if (++safety > 20000)
                        throw new Exception($"Game {g} did not terminate — rules bug!");

                    var roll = game.RollDice();
                    if (roll.MustMove)
                    {
                        var pick = roll.LegalMoves[rng.Next(roll.LegalMoves.Count)];
                        game.MoveToken(pick.TokenIndex);
                    }
                }

                totalTurns += game.State.TurnNumber;
                if (game.State.TurnNumber > maxTurns) maxTurns = game.State.TurnNumber;
            }

            log($"Simulated {games} games ({playerCount} players). " +
                $"Avg actions/game: {totalTurns / games}, max: {maxTurns}. All terminated OK.");
        }
    }
}