// ProfileSeatMapper.cs  (data-driven version)
// 4 profile slots are FIXED at the screen corners and never move.
// On board rotation, each slot is re-BOUND to the player whose yard is now
// at that corner. Attach to any UI object, assign slots + player data.
//
// Corner layout (unrotated board):
//   P0 Blue = Down-Left, P1 Yellow = Up-Left,
//   P2 Red  = Up-Right,  P3 Green  = Down-Right
// A 180° flip swaps corners diagonally: UR<->DL, UL<->DR.

using UnityEngine;

public class ProfileSeatMapper : MonoBehaviour
{
    public enum Corner { DownLeft, UpLeft, UpRight, DownRight }

    [System.Serializable]
    public class PlayerDisplayData
    {
        public string playerName = "Player";
        public Sprite avatar;
    }

    [Header("Fixed slots at the 4 screen corners")]
    public PlayerProfileSlot slotDownLeft;
    public PlayerProfileSlot slotUpLeft;
    public PlayerProfileSlot slotUpRight;
    public PlayerProfileSlot slotDownRight;

    [Header("Player data, index = player index (0=Blue,1=Yellow,2=Red,3=Green)")]
    public PlayerDisplayData[] players = new PlayerDisplayData[4];

    [Tooltip("Each player's corner when the board is NOT rotated")]
    public Corner[] homeCorner =
        { Corner.DownLeft, Corner.UpLeft, Corner.UpRight, Corner.DownRight };

    private LudoGameManager manager;

    /// Called by LudoGameManager in Start().
    public void Init(LudoGameManager mgr)
    {
        manager = mgr;
        foreach (var s in AllSlots()) s.Init(mgr);
        BoardOrientation.OnBoardRotated += Rebind;
        Rebind();
    }

    void OnDestroy() => BoardOrientation.OnBoardRotated -= Rebind;

    /// Re-assign every slot to the player whose yard is at its corner now.
    public void Rebind()
    {
        bool rotated = BoardOrientation.IsRotated;

        for (int p = 0; p < players.Length; p++)
        {
            Corner c = homeCorner[p];
            if (rotated) c = Diagonal(c);
            GetSlot(c).Bind(p, players[p].playerName, players[p].avatar);
        }

        // Restore turn state onto the (re)bound buttons.
        if (manager != null) manager.RefreshTurnState();
    }

    // ------------------------------------------- API used by GameManager

    public PlayerProfileSlot GetSlotForPlayer(int playerIndex)
    {
        foreach (var s in AllSlots())
            if (s.BoundPlayer == playerIndex) return s;
        return null;
    }

    public void SetCurrentPlayer(int playerIndex)
    {
        foreach (var s in AllSlots())
            s.SetActiveTurn(s.BoundPlayer == playerIndex);
    }

    public void DisableAll()
    {
        foreach (var s in AllSlots()) s.SetActiveTurn(false);
    }

    // --------------------------------------------------------- internals

    private PlayerProfileSlot[] AllSlots() =>
        new[] { slotDownLeft, slotUpLeft, slotUpRight, slotDownRight };

    private static Corner Diagonal(Corner c)
    {
        switch (c)
        {
            case Corner.DownLeft:  return Corner.UpRight;
            case Corner.UpRight:   return Corner.DownLeft;
            case Corner.UpLeft:    return Corner.DownRight;
            default:               return Corner.UpLeft;   // DownRight
        }
    }

    private PlayerProfileSlot GetSlot(Corner c)
    {
        switch (c)
        {
            case Corner.DownLeft:  return slotDownLeft;
            case Corner.UpLeft:    return slotUpLeft;
            case Corner.UpRight:   return slotUpRight;
            default:               return slotDownRight;
        }
    }
}
