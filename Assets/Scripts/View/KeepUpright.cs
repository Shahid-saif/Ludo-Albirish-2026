// KeepUpright.cs
// Attach to any element INSIDE boardRoot that must stay visually upright
// when the board rotates (safe-star icons, cell decorations, arrows with
// text, etc.). Pieces don't need this — they live outside boardRoot.

using UnityEngine;

public class KeepUpright : MonoBehaviour
{
    void Start() => Apply();

    void OnEnable() => BoardOrientation.OnBoardRotated += Apply;
    void OnDisable() => BoardOrientation.OnBoardRotated -= Apply;

    private void Apply()
    {
        // Cancel all inherited rotation: world rotation = identity.
        transform.rotation = Quaternion.identity;
    }
}
