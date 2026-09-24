// LobbyMenuController.cs
// Wires your existing menu (create room / join code / quick join, 2p / 4p)
// to LudoLobbyService. Self-wires all buttons in code, per project style.
// Replaces OnlineMatchTester (you can delete that now).
//
// Scene: object with LudoLobbyService + this. UI references:
//  - menuPanel: the whole menu root (hidden when the match starts)
//  - mode toggle: two buttons (2p / 4p) — selected one is disabled
//  - quickJoinButton, createRoomButton, joinButton + joinCodeInput
//  - lobbyStatusText: shows "waiting 1/4" + the room code to share
//  - startWithBotsButton: host-only, visible while waiting

using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyMenuController : MonoBehaviour
{
    public LudoLobbyService lobby;
    public LudoGameManager gameManager;

    [Header("UI")]
    public GameObject menuPanel;
    public Button mode2pButton;
    public Button mode4pButton;
    public Button quickJoinButton;
    public Button createRoomButton;
    public Button joinButton;
    public TMP_InputField joinCodeInput;
    public TMP_Text lobbyStatusText;
    public Button startWithBotsButton;

    [Header("Room code")]
    public GameObject roomCodePanel;     // hidden until a session has a code
    public TMP_Text roomCodeText;
    public Button copyCodeButton;

    private int selectedPlayers = 2;
    private Coroutine copiedFlash;

    void Start()
    {
        mode2pButton.onClick.AddListener(() => SelectMode(2));
        mode4pButton.onClick.AddListener(() => SelectMode(4));
        quickJoinButton.onClick.AddListener(QuickJoin);
        createRoomButton.onClick.AddListener(CreateRoom);
        joinButton.onClick.AddListener(JoinByCode);
        startWithBotsButton.onClick.AddListener(StartWithBots);
        copyCodeButton.onClick.AddListener(CopyRoomCode);

        lobby.OnLobbyChanged += RefreshLobbyUI;
        lobby.OnMatchReady += OnMatchReady;

        SelectMode(2);
        startWithBotsButton.gameObject.SetActive(false);
        roomCodePanel.SetActive(false);
        lobbyStatusText.text = "";
    }

    // ------------------------------------------------------------ INPUT

    private void SelectMode(int players)
    {
        selectedPlayers = players;
        mode2pButton.interactable = players != 2;
        mode4pButton.interactable = players != 4;
    }

    private async void QuickJoin()
    {
        UIManager.I.ShowLoading("loading.connecting");
        try { await lobby.QuickJoin(selectedPlayers); }
        catch (System.Exception e)
        {
            Debug.LogWarning($"QuickJoin failed: {e.Message}");
            UIManager.I.ShowError("error.network");
        }
        UIManager.I.HideLoading();
    }

    private async void CreateRoom()
    {
        UIManager.I.ShowLoading("loading.connecting");
        try { await lobby.CreateRoom(selectedPlayers); }
        catch (System.Exception e)
        {
            Debug.LogWarning($"CreateRoom failed: {e.Message}");
            UIManager.I.ShowError("error.network");
        }
        UIManager.I.HideLoading();
    }

    private async void JoinByCode()
    {
        if (string.IsNullOrWhiteSpace(joinCodeInput.text)) return;
        UIManager.I.ShowLoading("loading.connecting");
        try { await lobby.JoinByCode(joinCodeInput.text); }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Join failed: {e.Message}");
            UIManager.I.ShowError("error.network");
        }
        UIManager.I.HideLoading();
    }

    private async void StartWithBots()
    {
        startWithBotsButton.interactable = false;
        await lobby.StartWithBots();
        startWithBotsButton.interactable = true;
    }

    // ----------------------------------------------------------- EVENTS

    private void RefreshLobbyUI()
    {
        var s = lobby.Session;
        if (s == null)
        {
            lobbyStatusText.text = "";
            roomCodePanel.SetActive(false);
            startWithBotsButton.gameObject.SetActive(false);
            return;
        }

        lobbyStatusText.text = $"Waiting {s.Players.Count}/{s.MaxPlayers}";

        bool hasCode = !string.IsNullOrEmpty(s.Code);
        roomCodePanel.SetActive(hasCode);
        if (hasCode && copiedFlash == null)
            roomCodeText.text = s.Code;

        // Host can start early with bot fill (only meaningful if not full).
        startWithBotsButton.gameObject.SetActive(
            lobby.IsHost && s.Players.Count < s.MaxPlayers);
    }

    private void CopyRoomCode()
    {
        var s = lobby.Session;
        if (s == null || string.IsNullOrEmpty(s.Code)) return;

        GUIUtility.systemCopyBuffer = s.Code;

        if (copiedFlash != null) StopCoroutine(copiedFlash);
        copiedFlash = StartCoroutine(CopiedFlash(s.Code));
    }

    private System.Collections.IEnumerator CopiedFlash(string code)
    {
        roomCodeText.text = Loc.Get("lobby.copied");
        yield return new WaitForSeconds(1f);
        roomCodeText.text = code;
        copiedFlash = null;
    }

    private void OnMatchReady(MatchDoc doc)
    {
        Debug.Log($"Match ready: {doc.MatchId}, my seat = {gameManager.online.MySeat}");
        menuPanel.SetActive(false);

        // Waiting match (host created, guests still joining): show loading
        // until the document turns active and the board builds itself.
        if (!doc.IsActive)
        {
            UIManager.I.ShowLoading("loading.connecting");
            gameManager.online.OnMatchUpdated += HideLoadingWhenActive;
        }
    }

    private void HideLoadingWhenActive(MatchDoc doc)
    {
        if (!doc.IsActive && !doc.IsFinished) return;
        gameManager.online.OnMatchUpdated -= HideLoadingWhenActive;
        UIManager.I.HideLoading();
    }
}
