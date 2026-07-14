using Gua.Core;

namespace Gua.Testing.Unity;

public sealed class UnitySceneTestHostOptions
{
    public static UnitySceneTestHostOptions Default { get; } = new();
    public static UnitySceneTestHostOptions StrictIsolation { get; } = new() { StartupResetPolicy = GuaResetPolicy.Strict, TeardownResetPolicy = GuaResetPolicy.Strict, CaptureDiagnosticsBeforeTeardown = true };
    public string? UnityExecutablePath { get; init; }
    public string? ProjectPath { get; init; }
    public string BridgeUrl { get; init; } = "ws://127.0.0.1:8765";
    public bool UseAvailableBridgePort { get; init; } = true;
    public bool KillProcessOnDispose { get; init; } = true;
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan SceneTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public IReadOnlyList<string> AdditionalArguments { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; } = new Dictionary<string, string>();
    public GuaResetOptions? StartupReset { get; init; }
    public GuaResetPolicy StartupResetPolicy { get; init; } = GuaResetPolicy.Disabled;
    public GuaResetPolicy TeardownResetPolicy { get; init; } = GuaResetPolicy.Disabled;
    public bool CaptureDiagnosticsBeforeTeardown { get; init; }
    public string? DiagnosticsOutputDirectory { get; init; }
    public string DiagnosticsTestName { get; init; } = "unity-scene-teardown";
}

public sealed class UnityPlayerBuildOptions
{
    public string? UnityExecutablePath { get; init; }
    public string? ProjectPath { get; init; }
    public string? OutputPath { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);
    public IReadOnlyList<string> AdditionalArguments { get; init; } = Array.Empty<string>();
}
