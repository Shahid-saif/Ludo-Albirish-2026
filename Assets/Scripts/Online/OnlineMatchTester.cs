// OnlineMatchTester.cs
// Phase 1 TEST HARNESS — proves the whole online loop with Console logs and
// three UI buttons, BEFORE touching the game board. Delete after Phase 2.
//
// Scene: a "Tester" object with CloudMatchService + this script. Simple UI:
//   - Create button  -> CreateBtn()
//   - TMP_InputField + Join button -> JoinBtn()
//   - Roll button    -> RollBtn()
// Run two editor instances (ParrelSync clone): Create on one (match ID is
// logged), type it in the other and Join, then alternate Roll on whichever
// logs "YOUR TURN".

using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class OnlineMatchTester : MonoBehaviour
{
    public CloudMatchService service;
    public TMP_InputField matchIdInput;

    [Header("Buttons (self-wired in Start)")]
    public Button createButton;
    public Button joinButton;
    public Button rollButton;

    [Tooltip("Parent of Create/Join/input UI — hidden once the match starts")]
    public GameObject joinPanel;

    [Header("Create settings")]
    [Range(2, 4)] public int createMaxPlayers = 2;
    [Range(0, 3)] public int createBotCount = 1;

    void Start()
    {
        service.OnMatchUpdated += OnUpdated;

        createButton.onClick.AddListener(CreateBtn);
        joinButton.onClick.AddListener(JoinBtn);
        rollButton.onClick.AddListener(RollBtn);
    }

    public async void CreateBtn()
    {
        UIManager.I.ShowLoading("loading.connecting");
        try
        {
            var doc = await service.CreateMatch(createMaxPlayers, "Host", createBotCount);
            Debug.Log($"=== MATCH CREATED: {doc.MatchId} === (give this ID to the other editor)");
            service.StartPolling(2f);
        }
        catch { UIManager.I.ShowError("error.network"); }
        UIManager.I.HideLoading();
    }

    public async void JoinBtn()
    {
        UIManager.I.ShowLoading("loading.connecting");
        try
        {
            var doc = await service.JoinMatch(matchIdInput.text.Trim(), "Guest");
            Debug.Log($"=== JOINED {doc.MatchId}, status={doc.Status}, seat={service.MySeat} ===");
            service.StartPolling(2f);
        }
        catch { UIManager.I.ShowError("error.network"); }
        UIManager.I.HideLoading();
    }

    public async void RollBtn()
    {
        try
        {
            var resp = await service.SubmitRoll();
            Debug.Log($"Rolled {resp.roll}. mustMove={resp.mustMove} " +
                      $"moves=[{string.Join(",", resp.legalMoves.Select(m => $"{m.TokenIndex}:{m.FromPos}->{m.ToPos}"))}]");

            if (resp.mustMove)
            {
                // Test harness: no board yet, just play the first legal move.
                var mv = await service.SubmitMove(resp.legalMoves[0].TokenIndex);
                Debug.Log($"Moved token {mv.result.Move.TokenIndex} to {mv.result.Move.ToPos}. " +
                          $"captured={mv.result.Captured.Count} extraTurn={mv.result.ExtraTurn}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Roll rejected: {e.Message}"); // e.g. "Not your turn"
        }
    }

    private void OnUpdated(MatchDoc doc)
    {
        // Hide create/join UI once the match is running.
        if (joinPanel != null && (doc.IsActive || doc.IsFinished))
            joinPanel.SetActive(false);

        if (doc.State == null)
        {
            Debug.Log($"[update] waiting for players: {doc.PlayerIds.Count}/{doc.MaxPlayers}");
            return;
        }

        string turn = doc.State.CurrentPlayer == service.MySeat
            ? ">>> YOUR TURN <<<"
            : $"waiting for seat {doc.State.CurrentPlayer}";

        Debug.Log($"[update] turn#{doc.State.TurnNumber} lastRoll={doc.LastRoll} {turn} " +
                  $"status={doc.Status} actions={doc.Actions?.Count ?? -1} seq={doc.ActionSeq}");

        if (doc.IsFinished)
            Debug.Log($"=== GAME OVER. Finish order: {string.Join(",", doc.State.FinishedPlayers)} ===");
    }
}