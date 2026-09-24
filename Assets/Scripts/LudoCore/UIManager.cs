// UIManager.cs
// Unified UI service: loading overlay + queued popups. Singleton that
// survives scene loads. Every system (auth, lobby, match) talks to this —
// never builds its own dialogs.
//
// Scene setup (one Canvas, sorting order ABOVE the game canvas):
//   UIRoot (this script)
//   ├── LoadingPanel   (full-screen block: dim Image + spinner + TMP message)
//   └── PopupPanel     (dim Image + window: title TMP, message TMP, 3 Buttons
//                       each with a TMP_Text label)
// Both panels start INACTIVE. Buttons' OnClick lists stay empty (self-wired).
//
// Usage:
//   UIManager.I.ShowLoading("loading.signin");
//   UIManager.I.HideLoading();
//   UIManager.I.ShowError("error.signin", onRetry: TrySignIn);
//   UIManager.I.ShowPopup(new PopupData { ... });   // full control

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PopupData
{
    public string TitleKey;
    public string MessageKey;
    /// Raw text overrides (already-localized or dynamic strings). If set,
    /// the corresponding key is ignored.
    public string TitleRaw;
    public string MessageRaw;

    public string Btn0Key;               // primary (required)
    public Action Btn0Action;
    public string Btn1Key;               // optional
    public Action Btn1Action;
    public string Btn2Key;               // optional
    public Action Btn2Action;
}

public class UIManager : MonoBehaviour
{
    public static UIManager I { get; private set; }

    [Header("Loading")]
    public GameObject loadingPanel;
    public TMP_Text loadingText;

    [Header("Popup")]
    public GameObject popupPanel;
    public TMP_Text popupTitle;
    public TMP_Text popupMessage;
    public Button[] popupButtons = new Button[3];      // 0=primary
    public TMP_Text[] popupButtonLabels = new TMP_Text[3];

    private readonly Queue<PopupData> popupQueue = new();
    private PopupData currentPopup;
    private string currentLoadingKey;

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);

        for (int i = 0; i < popupButtons.Length; i++)
        {
            int idx = i;
            popupButtons[i].onClick.AddListener(() => OnPopupButton(idx));
        }

        loadingPanel.SetActive(false);
        popupPanel.SetActive(false);

        Loc.OnLanguageChanged += RefreshTexts;
    }

    void OnDestroy()
    {
        if (I == this) Loc.OnLanguageChanged -= RefreshTexts;
    }

    // ---------------------------------------------------------- LOADING

    public void ShowLoading(string messageKey = "loading.generic")
    {
        currentLoadingKey = messageKey;
        loadingText.text = Loc.Get(messageKey);
        loadingPanel.SetActive(true);
    }

    public void HideLoading()
    {
        currentLoadingKey = null;
        loadingPanel.SetActive(false);
    }

    // ----------------------------------------------------------- POPUPS

    /// Queue a popup. Shows immediately if none is open.
    public void ShowPopup(PopupData data)
    {
        popupQueue.Enqueue(data);
        if (currentPopup == null) ShowNext();
    }

    /// Shorthand: error with OK, or Retry/Cancel when onRetry is provided.
    public void ShowError(string messageKey, Action onRetry = null)
    {
        // Diagnostic: identify who requested this popup.
        Debug.LogWarning($"[UIManager] ShowError('{messageKey}') requested by:\n"
                         + new System.Diagnostics.StackTrace(1, true));

        ShowPopup(new PopupData
        {
            TitleKey   = "popup.error.title",
            MessageKey = messageKey,
            Btn0Key    = onRetry != null ? "btn.retry" : "btn.ok",
            Btn0Action = onRetry,
            Btn1Key    = onRetry != null ? "btn.cancel" : null,
        });
    }

    /// Shorthand: yes/no confirmation.
    public void ShowConfirm(string messageKey, Action onYes, Action onNo = null)
    {
        ShowPopup(new PopupData
        {
            TitleKey   = "popup.info.title",
            MessageKey = messageKey,
            Btn0Key    = "btn.yes",
            Btn0Action = onYes,
            Btn1Key    = "btn.no",
            Btn1Action = onNo,
        });
    }

    private void ShowNext()
    {
        if (popupQueue.Count == 0) { currentPopup = null; popupPanel.SetActive(false); return; }

        currentPopup = popupQueue.Dequeue();
        ApplyPopupTexts();
        popupPanel.SetActive(true);
    }

    private void ApplyPopupTexts()
    {
        var d = currentPopup;
        popupTitle.text   = d.TitleRaw   ?? Loc.Get(d.TitleKey   ?? "popup.info.title");
        popupMessage.text = d.MessageRaw ?? Loc.Get(d.MessageKey ?? "");

        SetupButton(0, d.Btn0Key);
        SetupButton(1, d.Btn1Key);
        SetupButton(2, d.Btn2Key);
    }

    private void SetupButton(int i, string key)
    {
        bool used = !string.IsNullOrEmpty(key);
        popupButtons[i].gameObject.SetActive(used);
        if (used) popupButtonLabels[i].text = Loc.Get(key);
    }

    private void OnPopupButton(int index)
    {
        var d = currentPopup;
        popupPanel.SetActive(false);
        currentPopup = null;

        // Invoke AFTER closing so the action can open its own popup/loading.
        Action a = index == 0 ? d.Btn0Action : index == 1 ? d.Btn1Action : d.Btn2Action;
        a?.Invoke();

        ShowNext();
    }

    // ------------------------------------------------------ LOCALIZATION

    /// Re-apply texts when language switches while UI is visible.
    private void RefreshTexts()
    {
        if (currentLoadingKey != null) loadingText.text = Loc.Get(currentLoadingKey);
        if (currentPopup != null) ApplyPopupTexts();
    }
}
