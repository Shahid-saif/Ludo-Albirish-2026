// GameBootstrap.cs
// Phase 0: initialize Unity Gaming Services + anonymous sign-in, with
// loading feedback and error/retry through UIManager.
// Attach to a "Bootstrap" GameObject in your first scene.
//
// PREREQUISITES (one-time, in the editor):
//  1. Project Settings -> Services: create/link a Unity project ID.
//  2. Package Manager: install "Authentication" (com.unity.services.authentication)
//     — it pulls in Core automatically.
//  3. UGS Dashboard (cloud.unity.com): the project appears automatically;
//     nothing to configure for anonymous auth.

using System;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;

public class GameBootstrap : MonoBehaviour
{
    /// Fired once sign-in succeeds — later phases (lobby, match) hook here.
    public static event Action OnServicesReady;

    public static bool IsReady { get; private set; }
    public static string PlayerId =>
        IsReady ? AuthenticationService.Instance.PlayerId : null;

    async void Start()
    {
        await InitializeAndSignIn();
    }

    private async Task InitializeAndSignIn()
    {
        UIManager.I.ShowLoading("loading.signin");
        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            IsReady = true;
            UIManager.I.HideLoading();
            Debug.Log($"UGS ready. PlayerId: {PlayerId}");
            OnServicesReady?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogError($"UGS sign-in failed: {e}");
            UIManager.I.HideLoading();
            UIManager.I.ShowError("error.signin",
                onRetry: async () => await InitializeAndSignIn());
        }
    }
}
