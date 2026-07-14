using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class GuaUnityFixtureAssets : IPreprocessBuildWithReport
{
    private const string AssetPath = "Assets/Resources/GuaFixturePanelSettings.asset";

    public int callbackOrder => 0;

    [InitializeOnLoadMethod]
    private static void Initialize() => EditorApplication.delayCall += EnsurePanelSettings;

    public void OnPreprocessBuild(BuildReport report) => EnsurePanelSettings();

    private static void EnsurePanelSettings()
    {
        if (AssetDatabase.LoadAssetAtPath<PanelSettings>(AssetPath) != null) return;
        Directory.CreateDirectory("Assets/Resources");
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        settings.referenceResolution = new Vector2Int(640, 360);
        settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        settings.match = 0.5f;
        AssetDatabase.CreateAsset(settings, AssetPath);
        AssetDatabase.SaveAssets();
    }
}
