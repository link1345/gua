using System.Diagnostics;
using System.Text;
using System.Net;
using System.Net.Sockets;
using Gua.Core;
using Gua.Testing;

namespace Gua.Testing.Godot;

public sealed class GodotSceneTestHost : IDisposable
{
    private readonly GodotSceneTestHostOptions _options;
    private readonly Process _process;
    private readonly string _bridgeUrl;
    private bool _disposed;

    private GodotSceneTestHost(Process process, GuaRemoteContext context, GodotSceneTestHostOptions options, string bridgeUrl)
    {
        _process = process;
        Context = context;
        _options = options;
        _bridgeUrl = bridgeUrl;
    }

    public IGuaContext Context { get; }

    public GuaRemoteContext RemoteContext => (GuaRemoteContext)Context;

    public int ProcessId => _process.Id;

    public GuaDiagnosticOptions CreateDiagnosticOptions(string testName, string? outputDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testName);
        return new GuaDiagnosticOptions
        {
            TestName = testName,
            OutputDirectory = outputDirectory ?? Path.Combine("artifacts", "gua"),
            Environment = new Dictionary<string, string>
            {
                ["host"] = "Godot",
                ["processId"] = ProcessId.ToString(),
                ["bridgeUrl"] = _bridgeUrl,
                ["projectPath"] = _options.ProjectPath ?? string.Empty,
            },
        };
    }

    public static GodotSceneTestHost Load(string scenePath, GodotSceneTestHostOptions? options = null)
    {
        options ??= GodotSceneTestHostOptions.Default;
        var projectPath = ResolveProjectPath(scenePath, options.ProjectPath);
        var godotPath = ResolveGodotExecutable(options.GodotExecutablePath);
        var godotScenePath = ToGodotScenePath(scenePath, projectPath);
        var bridgeUrl = options.UseAvailableBridgePort ? $"ws://127.0.0.1:{ReserveLoopbackPort()}" : options.BridgeUrl;
        var process = StartGodot(godotPath, projectPath, godotScenePath, bridgeUrl, options);
        var output = new ProcessOutput(process);
        var context = new GuaRemoteContext(bridgeUrl, options.RequestTimeout);

        try
        {
            output.Start();
            context.WaitUntilAvailable(options.ConnectTimeout);
            if (options.StartupReset is { } resetOptions)
            {
                var status = context.GetContextStatus();
                var report = context.Reset(resetOptions with { ExpectedSessionEpoch = status.SessionEpoch });
                if (report.Result == GuaResetResult.StaleEpoch)
                    throw new InvalidOperationException($"Godot startup reset rejected stale session epoch {status.SessionEpoch}.");
                if (report.Result == GuaResetResult.Dirty)
                    throw new InvalidOperationException($"Godot startup strict reset rejected dirty context: pending={report.PendingRequestCount}, inFlight={report.InFlightRequestCount}, events={report.UnconsumedEventCount}.");
                if (report.Result != GuaResetResult.Succeeded)
                    throw new InvalidOperationException($"Godot startup reset failed: {report.Result}.");
                var clean = context.GetContextStatus();
                if (!clean.IsClean)
                    throw new InvalidOperationException($"Godot startup reset left queued work: pending={clean.PendingRequestCount}, inFlight={clean.InFlightRequestCount}, events={clean.UnconsumedEventCount}.");
            }
            return new GodotSceneTestHost(process, context, options, bridgeUrl);
        }
        catch (Exception error)
        {
            KillProcess(process);
            context.Dispose();
            throw new InvalidOperationException(
                $"Failed to start Godot scene test host. executable='{godotPath}', project='{projectPath}', scene='{godotScenePath}', bridge='{bridgeUrl}'. stdout/stderr:\n{output.Read()}",
                error);
        }
    }

    public static GodotSceneTestHost LoadRendered(string scenePath, GodotSceneTestHostOptions? options = null)
    {
        options ??= GodotSceneTestHostOptions.Default;
        return Load(scenePath, CloneWithHeadless(options, false));
    }

    public void Click(string nodeId, string? nextScene = null, TimeSpan? timeout = null)
    {
        ThrowIfDisposed();
        var previousScreen = nextScene is null
            ? null
            : ((GuaRemoteContext)Context).GetCurrentScreen();

        if (!Context.EnqueueClick(nodeId))
        {
            throw new InvalidOperationException($"Failed to click Gua node: {nodeId}");
        }

        if (nextScene is not null)
        {
            ((GuaRemoteContext)Context).WaitForScreenTransition(
                ToExpectedScreens(nextScene),
                previousScreen,
                IsScenePath(nextScene),
                timeout ?? _options.SceneTimeout);
        }
    }

    public async Task<string> WaitForScreenshotAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var limit = timeout ?? _options.SceneTimeout;
        var deadline = DateTimeOffset.UtcNow + limit;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var screenshot = ((GuaRemoteContext)Context).GetScreenshotJson();
            if (screenshot.Contains("data:image/png;base64,", StringComparison.OrdinalIgnoreCase)) return screenshot;
            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        } while (DateTimeOffset.UtcNow < deadline);
        throw new TimeoutException($"Godot did not publish an opt-in viewport screenshot within {limit:g}.");
    }

    public GuaScreenshot GetScreenshot()
    {
        ThrowIfDisposed();
        return GuaScreenshot.Parse(RemoteContext.GetScreenshotJson());
    }

    public GuaSavedScreenshot SaveScreenshot(string directory, string testName)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(testName);
        var screenshot = GetScreenshot();
        var bytes = screenshot.DecodePng();
        var absoluteDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(absoluteDirectory);
        var safeName = string.Concat(testName.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        var path = Path.Combine(absoluteDirectory, $"{safeName}-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, bytes);
        return new GuaSavedScreenshot(screenshot.Width, screenshot.Height, Path.GetFullPath(path));
    }

    public GuaContextStatus GetContextStatus() => RemoteContext.GetContextStatus();

    public GuaVersion GetVersion() => RemoteContext.GetVersion();

    public Task<GuaVersion> GetVersionAsync(CancellationToken cancellationToken = default) =>
        RemoteContext.GetVersionAsync(cancellationToken);

    public GuaResetReport ResetContext(GuaResetOptions? options = null)
    {
        ThrowIfDisposed();
        var status = RemoteContext.GetContextStatus();
        var requested = options ?? new GuaResetOptions();
        var report = RemoteContext.Reset(requested with { ExpectedSessionEpoch = requested.ExpectedSessionEpoch ?? status.SessionEpoch });
        if (report.Result == GuaResetResult.Succeeded && !RemoteContext.GetContextStatus().IsClean)
            throw new InvalidOperationException("Godot context reset succeeded but pending requests or events remain.");
        return report;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Exception? teardownError = null;
        try
        {
            if (_options.TeardownReset is { } resetOptions)
            {
                var report = ResetContext(resetOptions);
                if (report.Result != GuaResetResult.Succeeded)
                    throw new InvalidOperationException($"Godot teardown reset failed: {report.Result}; pending={report.PendingRequestCount}, inFlight={report.InFlightRequestCount}, events={report.UnconsumedEventCount}.");
            }
        }
        catch (Exception error)
        {
            teardownError = error;
        }
        _disposed = true;
        ((IDisposable)Context).Dispose();
        if (_options.KillProcessOnDispose)
        {
            KillProcess(_process);
        }
        if (teardownError is not null) throw teardownError;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static Process StartGodot(
        string godotPath,
        string projectPath,
        string scenePath,
        string bridgeUrl,
        GodotSceneTestHostOptions options)
    {
        var arguments = new List<string>();
        if (options.Headless)
        {
            arguments.Add("--headless");
        }

        arguments.Add("--path");
        arguments.Add(projectPath);
        arguments.Add(scenePath);
        arguments.AddRange(options.AdditionalArguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = godotPath,
            WorkingDirectory = projectPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in options.EnvironmentVariables)
        {
            startInfo.Environment[name] = value;
        }
        startInfo.Environment["GUA_BRIDGE_PORT"] = new Uri(bridgeUrl).Port.ToString();

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start Godot executable: {godotPath}");
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private static GodotSceneTestHostOptions CloneWithHeadless(GodotSceneTestHostOptions source, bool headless) => new()
    {
        GodotExecutablePath = source.GodotExecutablePath,
        ProjectPath = source.ProjectPath,
        BridgeUrl = source.BridgeUrl,
        UseAvailableBridgePort = source.UseAvailableBridgePort,
        Headless = headless,
        KillProcessOnDispose = source.KillProcessOnDispose,
        ConnectTimeout = source.ConnectTimeout,
        RequestTimeout = source.RequestTimeout,
        SceneTimeout = source.SceneTimeout,
        AdditionalArguments = source.AdditionalArguments,
        EnvironmentVariables = source.EnvironmentVariables,
        StartupReset = source.StartupReset,
        TeardownReset = source.TeardownReset,
    };

    private static string ResolveGodotExecutable(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        var environmentPath = Environment.GetEnvironmentVariable("GODOT_EXECUTABLE");
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            return environmentPath;
        }

        return "godot";
    }

    private static string ResolveProjectPath(string scenePath, string? configuredProjectPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredProjectPath))
        {
            return Path.GetFullPath(configuredProjectPath);
        }

        var normalized = scenePath.Replace('\\', '/');
        if (normalized.StartsWith("res://", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "GodotSceneTestHostOptions.ProjectPath is required when loading a res:// scene path.");
        }

        var sceneFullPath = Path.GetFullPath(scenePath);
        var sceneDirectory = Path.GetDirectoryName(sceneFullPath)
            ?? throw new InvalidOperationException($"Could not resolve a directory for Godot scene: {scenePath}");

        var projectPath = FindGodotProjectDirectory(sceneDirectory);
        return projectPath
            ?? throw new InvalidOperationException(
                $"Could not locate project.godot for scene '{scenePath}'. Set GodotSceneTestHostOptions.ProjectPath explicitly.");
    }

    private static string? FindGodotProjectDirectory(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "project.godot")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string ToGodotScenePath(string scenePath, string projectPath)
    {
        var normalized = scenePath.Replace('\\', '/');
        if (normalized.StartsWith("res://", StringComparison.Ordinal))
        {
            return normalized;
        }

        var fullPath = Path.GetFullPath(scenePath);
        var relative = Path.GetRelativePath(projectPath, fullPath).Replace('\\', '/');
        if (!relative.StartsWith("..", StringComparison.Ordinal))
        {
            return "res://" + relative;
        }

        return "res://" + normalized.TrimStart('/');
    }

    private static IReadOnlySet<string> ToExpectedScreens(string scenePath)
    {
        var expected = new HashSet<string>(StringComparer.Ordinal);
        var normalized = scenePath.Replace('\\', '/');
        if (normalized.StartsWith("res://", StringComparison.Ordinal))
        {
            expected.Add(normalized);
            expected.Add(normalized["res://".Length..]);
            AddSceneStem(expected, normalized);
            return expected;
        }

        if (normalized.StartsWith("game/", StringComparison.Ordinal))
        {
            var projectRelative = normalized["game/".Length..];
            expected.Add("res://" + projectRelative);
            expected.Add(projectRelative);
            AddSceneStem(expected, normalized);
            return expected;
        }

        expected.Add(normalized);
        AddSceneStem(expected, normalized);
        return expected;
    }

    private static void AddSceneStem(HashSet<string> expected, string scenePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(scenePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        expected.Add(fileName);
        if (fileName.EndsWith("_screen", StringComparison.Ordinal) && fileName.Length > "_screen".Length)
        {
            expected.Add(fileName[..^"_screen".Length]);
        }
    }

    private static bool IsScenePath(string scene)
    {
        return scene.Contains('/', StringComparison.Ordinal) ||
            scene.Contains('\\', StringComparison.Ordinal) ||
            Path.GetExtension(scene).Length > 0 ||
            scene.StartsWith("res://", StringComparison.Ordinal);
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
        }
        catch
        {
        }

        process.Dispose();
    }

    private sealed class ProcessOutput
    {
        private readonly Process _process;
        private readonly StringBuilder _output = new();

        public ProcessOutput(Process process)
        {
            _process = process;
        }

        public void Start()
        {
            _process.OutputDataReceived += (_, args) => Append(args.Data);
            _process.ErrorDataReceived += (_, args) => Append(args.Data);
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        public string Read()
        {
            return _output.ToString();
        }

        private void Append(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (_output)
            {
                _output.AppendLine(line);
            }
        }
    }
}
