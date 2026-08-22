// BoardOrientation.cs
// Rotates the board so the local player's yard is on the BOTTOM of the
// portrait screen. Attach anywhere (e.g. the GameManager object).
//
// Corner layout (unrotated):  UR = P0 Red, UL = P3 Yellow,
//                             DR = P1 Green, DL = P2 Blue
// -> players 0 and 3 are on the TOP side and need the 180° flip.
//
// In multiplayer (step 2+): call SetLocalPlayer(myPlayerIndex) once the
// match starts. For offline testing: tick "applyOnStart" and pick
// testLocalPlayer, or change it during Play and use the component's
// context menu -> "Apply Test Rotation".

using System;
using System.Collections;
using UnityEngine;

public class BoardOrientation : MonoBehaviour
{
    [Header("References")]
    public Transform boardRoot;          // rotates: board art + all points
    public LudoGameManager manager;      // re-seats pieces after rotation

    [Header("Layout")]
    [Tooltip("Player indices whose yard is on the TOP half when unrotated")]
    public int[] topSidePlayers = { 0, 3 };

    [Header("Offline testing")]
    public bool applyOnStart = true;
    [Range(0, 3)] public int testLocalPlayer = 0;

    /// Fired after every rotation — KeepUpright and ProfileSeatMapper listen.
    public static event Action OnBoardRotated;

    /// True when the board is currently flipped 180°.
    public static bool IsRotated { get; private set; }

    private IEnumerator Start()
    {
        // Wait one frame so LudoGameManager.Start has initialized everything.
        yield return null;
        if (applyOnStart)
            SetLocalPlayer(testLocalPlayer);
    }

    /// Rotate the board so this player's corner is at the bottom.
    public void SetLocalPlayer(int playerIndex)
    {
        bool onTop = Array.IndexOf(topSidePlayers, playerIndex) >= 0;
        IsRotated = onTop;
        boardRoot.localRotation = Quaternion.Euler(0f, 0f, onTop ? 180f : 0f);

        OnBoardRotated?.Invoke();          // let marked elements re-upright
        if (manager != null)
            manager.RefreshBoardVisuals(); // re-seat pieces on moved points

        Debug.Log($"Board oriented for player {playerIndex} (rotated: {onTop})");
    }

    [ContextMenu("Apply Test Rotation")]
    private void ApplyTestRotation() => SetLocalPlayer(testLocalPlayer);
}