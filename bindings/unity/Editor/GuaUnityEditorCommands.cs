using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;

namespace Gua.Unity.Editor
{

public static class GuaUnityEditorCommands
{
    public static void PrepareProject()
    {
        if (AssetDatabase.LoadAssetAtPath<TMP_Settings>("Assets/TextMesh Pro/Resources/TMP Settings.asset") != null) return;
        TMP_PackageResourceImporter.ImportResources(true, false, false);
    }

    public static void BuildPlayer()
    {
        PrepareProject();
        var scene = Argument("-guaScene") ?? throw new ArgumentException("-guaScene is required.");
        var output = Argument("-guaOutput") ?? throw new ArgumentException("-guaOutput is required.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { scene }, locationPathName = output,
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development,
        });
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new InvalidOperationException($"Gua Unity player build failed: {report.summary.result} ({report.summary.totalErrors} errors).");
    }

    public static void StartPlayMode()
    {
        var scene = Argument("-guaScene") ?? throw new ArgumentException("-guaScene is required.");
        EditorSceneManager.OpenScene(scene);
        EditorApplication.isPlaying = true;
    }

    private static string? Argument(string name)
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.FindIndex(args, value => string.Equals(value, name, StringComparison.Ordinal));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
}
