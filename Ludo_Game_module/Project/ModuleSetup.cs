// ModuleSetup.cs — Cloud Code module dependency registration.
// Uses the modern ICloudCodeConfig extension methods (the old
// GameApiClient.Create()/PushClient.Create() factories are obsolete/removed).
// For Com.Unity.Services.CloudCode.Core 0.0.5 / Apis 0.0.26.

using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Apis.Extensions;
using Unity.Services.CloudCode.Core;

public class ModuleConfig : ICloudCodeSetup
{
    public void Setup(ICloudCodeConfig config)
    {
        config.AddGameApiClient();   // injects IGameApiClient (Cloud Save etc.)
        // IPushClient needs no registration — the runtime injects it
        // automatically in these package versions.
    }
}
