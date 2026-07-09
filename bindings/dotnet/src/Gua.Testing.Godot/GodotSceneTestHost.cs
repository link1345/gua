using System.Diagnostics;
using System.Text;
using Gua.Core;

namespace Gua.Testing.Godot;

public sealed class GodotSceneTestHost : IDisposable
{
    private readonly GodotSceneTestHostOptions _options;
    private readonly Process _process;
    private bool _disposed;

    private GodotSceneTestHost(Process process, GuaRemoteContext context, GodotSceneTestHostOptions options)
    {
        _process = process;
        Context = context;
        _options = options;
    }

    public IGuaContext Context { get; }

    public int ProcessId => _process.Id;

    public static GodotSceneTestHost Load(string scenePath, GodotSceneTestHostOptions? options = null)
    {
        options ??= GodotSceneTestHostOptions.Default;
        var projectPath = ResolveProjectPath(scenePath, options.ProjectPath);
        var godotPath = ResolveGodotExecutable(options.GodotExecutablePath);
        var process = StartGodot(godotPath, projectPath, ToGodotScenePath(scenePath, projectPath), options);
        var output = new ProcessOutput(process);
        var context = new GuaRemoteContext(options.BridgeUrl, options.RequestTimeout);

        try
        {
            output.Start();
            context.WaitUntilAvailable(options.ConnectTimeout);
            return new GodotSceneTestHost(process, context, options);
        }
        catch (Exception error)
        {
            KillProcess(process);
            context.Dispose();
            throw new InvalidOperationException(
                $"Failed to connect to the Gua bridge at {options.BridgeUrl}. Godot output:\n{output.Read()}",
                error);
        }
    }

    public void Click(string nodeId, string? nextScene = null, TimeSpan? timeout = null)
    {
        ThrowIfDisposed();
        if (!Context.EnqueueClick(nodeId))
        {
            throw new InvalidOperationException($"Failed to click Gua node: {nodeId}");
        }

        if (nextScene is not null)
        {
            ((GuaRemoteContext)Context).WaitForScreen(ToExpectedScreen(nextScene), timeout ?? _options.SceneTimeout);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ((IDisposable)Context).Dispose();
        if (_options.KillProcessOnDispose)
        {
            KillProcess(_process);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static Process StartGodot(
        string godotPath,
        string projectPath,
        string scenePath,
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

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start Godot executable: {godotPath}");
    }

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
        var gameIndex = normalized.IndexOf("/game/", StringComparison.Ordinal);
        if (gameIndex >= 0)
        {
            return Path.GetFullPath(normalized[..(gameIndex + "/game".Length)]);
        }

        if (normalized.StartsWith("game/", StringComparison.Ordinal))
        {
            return Path.GetFullPath("game");
        }

        return Environment.CurrentDirectory;
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

        if (normalized.StartsWith("game/", StringComparison.Ordinal))
        {
            return "res://" + normalized["game/".Length..];
        }

        return "res://" + normalized.TrimStart('/');
    }

    private static string ToExpectedScreen(string scenePath)
    {
        var normalized = scenePath.Replace('\\', '/');
        if (normalized.StartsWith("res://", StringComparison.Ordinal))
        {
            return normalized;
        }

        if (normalized.StartsWith("game/", StringComparison.Ordinal))
        {
            return "res://" + normalized["game/".Length..];
        }

        return normalized;
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
