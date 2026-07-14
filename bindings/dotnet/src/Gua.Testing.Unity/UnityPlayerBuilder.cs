using System.Diagnostics;
using System.Text;

namespace Gua.Testing.Unity;

public static class UnityPlayerBuilder
{
    public static string Build(string scenePath, UnityPlayerBuildOptions? options = null)
    {
        options ??= new(); var project = UnityPaths.ResolveProject(scenePath, options.ProjectPath); var scene = UnityPaths.ToAssetPath(scenePath, project);
        var unity = UnityPaths.ResolveEditor(options.UnityExecutablePath);
        var output = Path.GetFullPath(options.OutputPath ?? Path.Combine(Path.GetTempPath(), "gua-unity-player", Guid.NewGuid().ToString("N"), "GuaUnityFixture.exe"));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var log = output + ".build.log";
        var args = new List<string> { "-batchmode", "-quit", "-projectPath", project, "-executeMethod", "Gua.Unity.Editor.GuaUnityEditorCommands.BuildPlayer", "-guaScene", scene, "-guaOutput", output, "-logFile", log };
        args.AddRange(options.AdditionalArguments);
        using var process = Process.Start(UnityPaths.StartInfo(unity, project, args, null)) ?? throw new InvalidOperationException($"Failed to start Unity Editor: {unity}");
        if (!process.WaitForExit((int)Math.Min(int.MaxValue, options.Timeout.TotalMilliseconds))) { TryKill(process); throw new TimeoutException($"Unity player build timed out after {options.Timeout:g}. Log: {log}"); }
        if (process.ExitCode != 0 || !File.Exists(output)) throw new InvalidOperationException($"Unity player build failed with exit code {process.ExitCode}. editor='{unity}', project='{project}', scene='{scene}', output='{output}', log='{log}'.\n{Read(log)}");
        return output;
    }
    private static string Read(string path) { try { return File.Exists(path) ? File.ReadAllText(path) : ""; } catch { return ""; } }
    private static void TryKill(Process process) { try { process.Kill(); } catch { } }
}
