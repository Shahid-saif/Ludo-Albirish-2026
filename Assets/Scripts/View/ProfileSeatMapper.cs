// ProfileSeatMapper.cs  (online-aware)
// 4 fixed corner slots; each is BOUND to the player whose yard is at that
// corner. Online additions:
//   - SetOnlineData(names): fill slots from the match document; slots for
//     unused seats (2-3 player matches) hide themselves.
//   - SetCurrentPlayer(cur, localSeat): when localSeat >= 0 (online), only
//     the LOCAL player's own button can ever be interactable.

using System.Collections.Generic;
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
    private int activeCount = 4;

    public void Init(LudoGameManager mgr)
    {
        manager = mgr;
        foreach (var s in AllSlots()) s.Init(mgr);
        BoardOrientation.OnBoardRotated += Rebind;
        Rebind();
    }

    void OnDestroy() => BoardOrientation.OnBoardRotated -= Rebind;

    /// Online: fill names from the match document (index = seat).
    public void SetOnlineData(IList<string> names)
    {
        activeCount = Mathf.Clamp(names.Count, 2, 4);
        for (int i = 0; i < players.Length; i++)
            if (i < names.Count) players[i].playerName = names[i];
        Rebind();
    }

    public void Rebind()
    {
        bool rotated = BoardOrientation.IsRotated;

        for (int p = 0; p < players.Length; p++)
        {
            Corner c = homeCorner[p];
            if (rotated) c = Diagonal(c);
            var slot = GetSlot(c);

            if (p >= activeCount)
            {
                slot.gameObject.SetActive(false);
                continue;
            }
            slot.gameObject.SetActive(true);
            slot.Bind(p, players[p].playerName, players[p].avatar);
        }

        if (manager != null) manager.RefreshTurnState();
    }

    // ------------------------------------------- API used by GameManager

    public PlayerProfileSlot GetSlotForPlayer(int playerIndex)
    {
        foreach (var s in AllSlots())
            if (s.gameObject.activeSelf && s.BoundPlayer == playerIndex) return s;
        return null;
    }

    /// localSeat >= 0: online mode — only the local player's button may
    /// become interactable (remote players' buttons stay off on my screen).
    public void SetCurrentPlayer(int playerIndex, int localSeat = -1)
    {
        foreach (var s in AllSlots())
        {
            bool active = s.BoundPlayer == playerIndex
                       && (localSeat < 0 || s.BoundPlayer == localSeat);
            s.SetActiveTurn(active);
        }
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
            case Corner.DownLeft: return Corner.UpRight;
            case Corner.UpRight: return Corner.DownLeft;
            case Corner.UpLeft: return Corner.DownRight;
            default: return Corner.UpLeft;   // DownRight
        }
    }

    private PlayerProfileSlot GetSlot(Corner c)
    {
        switch (c)
        {
            case Corner.DownLeft: return slotDownLeft;
            case Corner.UpLeft: return slotUpLeft;
            case Corner.UpRight: return slotUpRight;
            default: return slotDownRight;
        }
    }
}