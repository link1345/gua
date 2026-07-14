using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Gua.Core;
using Gua.Testing;

namespace Gua.Testing.Unity;

public sealed class UnitySceneTestHost : IDisposable, IAsyncDisposable
{
    private readonly Process process;
    private readonly UnitySceneTestHostOptions options;
    private readonly string bridgeUrl;
    private readonly string logPath;
    private bool disposed;
    private UnitySceneTestHost(Process process, GuaWebSocketContext context, UnitySceneTestHostOptions options, string bridgeUrl, string logPath)
    { this.process = process; Context = context; this.options = options; this.bridgeUrl = bridgeUrl; this.logPath = logPath; }

    public IGuaContext Context { get; }
    public GuaWebSocketContext RemoteContext => (GuaWebSocketContext)Context;
    public int ProcessId => process.Id;
    public string BridgeUrl => bridgeUrl;
    public string LogPath => logPath;

    public static UnitySceneTestHost LoadPlayer(string playerPath, UnitySceneTestHostOptions? options = null) => StartPlayer(playerPath, true, options ?? UnitySceneTestHostOptions.Default);
    public static UnitySceneTestHost LoadRenderedPlayer(string playerPath, UnitySceneTestHostOptions? options = null) => StartPlayer(playerPath, false, options ?? UnitySceneTestHostOptions.Default);
    public static UnitySceneTestHost BuildAndLoadPlayer(string scenePath, UnitySceneTestHostOptions? options = null, bool rendered = false)
    {
        options ??= UnitySceneTestHostOptions.Default;
        var player = UnityPlayerBuilder.Build(scenePath, new UnityPlayerBuildOptions { UnityExecutablePath = options.UnityExecutablePath, ProjectPath = options.ProjectPath });
        return StartPlayer(player, !rendered, options);
    }
    public static UnitySceneTestHost LoadEditor(string scenePath, UnitySceneTestHostOptions? options = null)
    {
        options ??= UnitySceneTestHostOptions.Default; var project = UnityPaths.ResolveProject(scenePath, options.ProjectPath); var scene = UnityPaths.ToAssetPath(scenePath, project);
        var unity = UnityPaths.ResolveEditor(options.UnityExecutablePath); var bridge = Bridge(options); var log = Path.Combine(Path.GetTempPath(), $"gua-unity-editor-{Guid.NewGuid():N}.log");
        var args = new List<string> { "-projectPath", project, "-executeMethod", "Gua.Unity.Editor.GuaUnityEditorCommands.StartPlayMode", "-guaScene", scene, "-logFile", log };
        args.AddRange(options.AdditionalArguments);
        UnityPaths.EnsureProjectUnlocked(project, unity, args);
        return Start(UnityPaths.StartInfo(unity, project, args, Environment(options, bridge)), options, bridge, log, $"Unity Editor scene '{scene}'");
    }
    private static UnitySceneTestHost StartPlayer(string playerPath, bool headless, UnitySceneTestHostOptions options)
    {
        var player = Path.GetFullPath(playerPath); if (!File.Exists(player)) throw new FileNotFoundException("Unity player executable was not found.", player);
        var bridge = Bridge(options); var log = Path.Combine(Path.GetTempPath(), $"gua-unity-player-{Guid.NewGuid():N}.log");
        var args = new List<string>(); if (headless) { args.Add("-batchmode"); args.Add("-nographics"); } args.Add("-logFile"); args.Add(log); args.AddRange(options.AdditionalArguments);
        return Start(UnityPaths.StartInfo(player, Path.GetDirectoryName(player)!, args, Environment(options, bridge)), options, bridge, log, $"Unity player '{player}'");
    }
    private static UnitySceneTestHost Start(ProcessStartInfo info, UnitySceneTestHostOptions options, string bridge, string log, string description)
    {
        var process = Process.Start(info) ?? throw new InvalidOperationException($"Failed to start {description}."); var context = new GuaWebSocketContext(bridge, options.RequestTimeout);
        try
        {
            context.WaitUntilAvailable(options.ConnectTimeout);
            if (options.StartupResetPolicy.Mode != GuaResetMode.Disabled || options.StartupReset != null)
            {
                var requested = options.StartupReset ?? new GuaResetOptions(options.StartupResetPolicy.Targets, options.StartupResetPolicy.Mode == GuaResetMode.Strict); var status = context.GetContextStatus();
                var report = context.Reset(requested with { ExpectedSessionEpoch = requested.ExpectedSessionEpoch ?? status.SessionEpoch });
                if (report.Result != GuaResetResult.Succeeded) throw new InvalidOperationException($"Unity startup reset failed: {report.Result}.");
            }
            var version = context.GetVersion();
            if (version.AdapterVersions == null || !version.AdapterVersions.ContainsKey("unity")) throw new InvalidOperationException("Connected Gua runtime did not report a Unity adapter version.");
            return new(process, context, options, bridge, log);
        }
        catch (Exception error)
        {
            context.Dispose(); TryKill(process); throw new InvalidOperationException($"Failed to start {description}. bridge='{bridge}', log='{log}'.\n{Read(log)}", error);
        }
    }

    public GuaCapturedScreenshot CaptureScreenshot(TimeSpan? timeout = null) { ThrowIfDisposed(); return RemoteContext.CaptureScreenshot(timeout ?? options.SceneTimeout, RemoteContext.GetContextStatus().FrameSequence); }
    public GuaDiagnosticsSession CreateDiagnosticsSession(string testName, string? outputDirectory = null, bool captureScreenshot = false, Action<GuaDiagnosticFile>? attachmentSink = null)
    {
        ThrowIfDisposed(); return new(Context, new GuaDiagnosticOptions { TestName = testName, OutputDirectory = outputDirectory ?? Path.Combine("artifacts", "gua"), ScreenshotCapture = captureScreenshot ? () => CaptureScreenshot().DecodePng() : null,
            AttachmentSink = attachmentSink, Environment = new Dictionary<string, string> { ["host"] = "Unity", ["processId"] = process.Id.ToString(), ["bridgeUrl"] = bridgeUrl },
            TextArtifacts = new Dictionary<string, Func<string>> { ["unity-process.log"] = () => Read(logPath), ["unity-process.json"] = () => System.Text.Json.JsonSerializer.Serialize(new { process.Id, bridgeUrl, logPath }) } });
    }
    public void Dispose()
    {
        if (disposed) return; Exception? failure = null;
        try
        {
            if (options.TeardownResetPolicy.Mode != GuaResetMode.Disabled)
            {
                var status = RemoteContext.GetContextStatus(); var requested = new GuaResetOptions(options.TeardownResetPolicy.Targets, options.TeardownResetPolicy.Mode == GuaResetMode.Strict);
                var report = RemoteContext.Reset(requested with { ExpectedSessionEpoch = requested.ExpectedSessionEpoch ?? status.SessionEpoch });
                if (report.Result != GuaResetResult.Succeeded) throw new InvalidOperationException($"Unity teardown reset failed: {report.Result}.");
            }
        }
        catch (Exception error) { failure = error; if (options.CaptureDiagnosticsBeforeTeardown) try { CreateDiagnosticsSession(options.DiagnosticsTestName, options.DiagnosticsOutputDirectory).Capture(error); } catch { } }
        finally { disposed = true; RemoteContext.Dispose(); if (options.KillProcessOnDispose) TryKill(process); process.Dispose(); }
        if (failure != null) throw failure;
    }
    public ValueTask DisposeAsync() { Dispose(); return default; }
    private void ThrowIfDisposed() { if (disposed) throw new ObjectDisposedException(nameof(UnitySceneTestHost)); }
    private static Dictionary<string, string> Environment(UnitySceneTestHostOptions options, string bridge) { var result = new Dictionary<string, string>(options.EnvironmentVariables); result["GUA_BRIDGE_PORT"] = new Uri(bridge).Port.ToString(); return result; }
    private static string Bridge(UnitySceneTestHostOptions options) => options.UseAvailableBridgePort ? $"ws://127.0.0.1:{ReservePort()}" : options.BridgeUrl;
    private static int ReservePort() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); try { return ((IPEndPoint)listener.LocalEndpoint).Port; } finally { listener.Stop(); } }
    private static string Read(string path) { try { return File.Exists(path) ? File.ReadAllText(path) : ""; } catch { return ""; } }
    private static void TryKill(Process process) { try { if (!process.HasExited) process.Kill(); } catch { } }
}

internal static class UnityPaths
{
    internal static void EnsureProjectUnlocked(string project, string unity, IEnumerable<string> arguments)
    {
        var lockFile = Path.Combine(project, "Temp", "UnityLockfile");
        if (!File.Exists(lockFile)) return;
        try { using var stream = new FileStream(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error)
        {
            throw new InvalidOperationException($"Unity project is already open and locked: '{project}'. command='{Quote(unity)} {string.Join(" ", arguments.Select(Quote))}', lock='{lockFile}'.", error);
        }
    }
    internal static string ResolveEditor(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        var environment = Environment.GetEnvironmentVariable("UNITY_EXECUTABLE"); if (!string.IsNullOrWhiteSpace(environment)) return Path.GetFullPath(environment);
        var root = @"C:\Program Files\Unity\Hub\Editor";
        var candidate = Directory.Exists(root) ? Directory.EnumerateDirectories(root).OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase).Select(path => Path.Combine(path, "Editor", "Unity.exe")).FirstOrDefault(File.Exists) : null;
        return candidate ?? throw new FileNotFoundException("Could not locate Unity.exe. Set UNITY_EXECUTABLE or UnityExecutablePath.");
    }
    internal static string ResolveProject(string scenePath, string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        var current = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(scenePath))!);
        while (current != null) { if (File.Exists(Path.Combine(current.FullName, "ProjectSettings", "ProjectVersion.txt"))) return current.FullName; current = current.Parent; }
        throw new InvalidOperationException($"Could not locate a Unity project for scene '{scenePath}'. Set ProjectPath explicitly.");
    }
    internal static string ToAssetPath(string scenePath, string project)
    {
        if (scenePath.Replace('\\', '/').StartsWith("Assets/", StringComparison.Ordinal)) return scenePath.Replace('\\', '/');
        var relative = Path.GetRelativePath(project, Path.GetFullPath(scenePath)).Replace('\\', '/'); if (!relative.StartsWith("Assets/", StringComparison.Ordinal)) throw new InvalidOperationException($"Unity scene '{scenePath}' is outside project '{project}'."); return relative;
    }
    internal static ProcessStartInfo StartInfo(string executable, string workingDirectory, IEnumerable<string> arguments, IReadOnlyDictionary<string, string>? environment)
    {
        var info = new ProcessStartInfo { FileName = executable, WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true, Arguments = string.Join(" ", arguments.Select(Quote)) };
        if (environment != null) foreach (var pair in environment) info.EnvironmentVariables[pair.Key] = pair.Value;
        return info;
    }
    private static string Quote(string value) => value.Length > 0 && value.All(ch => !char.IsWhiteSpace(ch) && ch != '"') ? value : "\"" + value.Replace("\"", "\\\"") + "\"";
}
