// PlayerProfileSlot.cs
// One fixed profile box on screen (4 total, one per corner). Never moves.
// Shows whichever player is bound to it: name, avatar, dice button, result.
// Attach to each profile panel and assign the UI references.

using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerProfileSlot : MonoBehaviour
{
    [Header("UI references")]
    public TMP_Text nameText;
    public Image avatarImage;
    public Button diceButton;
    public TMP_Text diceLabel;

    /// Engine player index currently shown by this slot. -1 = unbound.
    public int BoundPlayer { get; private set; } = -1;

    private LudoGameManager manager;

    public void Init(LudoGameManager mgr)
    {
        manager = mgr;
        diceButton.onClick.AddListener(() =>
        {
            if (BoundPlayer >= 0)
                manager.OnDiceButtonPressed(BoundPlayer);
        });
    }

    /// Assign which player this slot displays and controls.
    public void Bind(int playerIndex, string playerName, Sprite avatar)
    {
        BoundPlayer = playerIndex;
        if (nameText != null) nameText.text = playerName;
        if (avatarImage != null && avatar != null) avatarImage.sprite = avatar;
        if (diceLabel != null) diceLabel.text = "";
    }

    public void SetDiceValue(int value)
    {
        if (diceLabel != null) diceLabel.text = value.ToString();
    }

    public void SetActiveTurn(bool active)
    {
        diceButton.interactable = active;
    }
}
