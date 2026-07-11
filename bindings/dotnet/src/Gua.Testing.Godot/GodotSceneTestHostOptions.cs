namespace Gua.Testing.Godot;

using Gua.Core;

public sealed class GodotSceneTestHostOptions
{
    public static GodotSceneTestHostOptions Default { get; } = new();

    public string? GodotExecutablePath { get; init; }

    public string? ProjectPath { get; init; }

    public string BridgeUrl { get; init; } = "ws://127.0.0.1:8765";

    public bool UseAvailableBridgePort { get; init; }

    public bool Headless { get; init; } = true;

    public bool KillProcessOnDispose { get; init; } = true;

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan SceneTimeout { get; init; } = TimeSpan.FromSeconds(3);

    public IReadOnlyList<string> AdditionalArguments { get; init; } = [];

    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; } = new Dictionary<string, string>();

    public GuaResetOptions? StartupReset { get; init; }

    public GuaResetOptions? TeardownReset { get; init; }
}
