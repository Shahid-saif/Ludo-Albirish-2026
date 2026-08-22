// LudoTokenView.cs  (UI version)
// Attach to each piece (UI Image under the Canvas). No collider needed.
// IMPORTANT: put ALL 16 pieces under ONE shared parent (e.g. "PiecesContainer")
// so stacking order (sibling index) works across players.

using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class LudoTokenView : MonoBehaviour, IPointerClickHandler
{
    private LudoGameManager manager;
    private int player;
    private int tokenIndex;

    public int Player => player;

    [Tooltip("Optional: object enabled when this piece can be moved (e.g. a glow ring)")]
    public GameObject highlight;

    [Tooltip("Hop height in pixels during movement. 0 = slide flat.")]
    public float hopHeight = 12f;

    /// Set by the manager on every move — later arrival renders on top.
    [HideInInspector] public int ArrivalOrder;

    private Image image;
    private Vector3 baseScale;

    public void Init(LudoGameManager mgr, int p, int t)
    {
        manager = mgr;
        player = p;
        tokenIndex = t;
        image = GetComponent<Image>();
        baseScale = transform.localScale;
        SetHighlight(false);

        // A child highlight Image must never swallow clicks.
        if (highlight != null)
        {
            var hlImg = highlight.GetComponent<Image>();
            if (hlImg != null) hlImg.raycastTarget = false;
        }
    }

    public void SetHighlight(bool on)
    {
        if (highlight != null) highlight.SetActive(on);
    }

    /// Called by the manager whenever the turn changes.
    /// Current player's pieces: bigger + clickable.
    /// Everyone else's pieces: normal size + INVISIBLE to raycasts, so an
    /// opponent piece stacked on top can never block your tap.
    public void SetTurnState(bool isCurrentPlayersTurn, float activeScale)
    {
        transform.localScale = baseScale * (isCurrentPlayersTurn ? activeScale : 1f);
        if (image != null) image.raycastTarget = isCurrentPlayersTurn;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (manager != null)
            manager.OnTokenClicked(player, tokenIndex);
    }

    public IEnumerator MoveTo(Vector3 target, float duration)
    {
        Vector3 start = transform.position;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            Vector3 pos = Vector3.Lerp(start, target, Mathf.Clamp01(t));
            pos.y += Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * hopHeight;
            transform.position = pos;
            yield return null;
        }
        transform.position = target;
    }
}