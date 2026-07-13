using Gua.Core;
using UnityEngine;

public sealed class GuaUnitySmoke : MonoBehaviour
{
    private GuaContext _context;

    private void Start()
    {
        _context = new GuaContext();
        _context.BeginFrame("unity-smoke");
        _context.RegisterNode(
            "unity-start",
            "button",
            "開始",
            new GuaBounds(10, 20, 160, 40));
        _context.EndFrame();

        var tree = _context.GetUiTreeJson();
        if (!tree.Contains("unity-start") || !tree.Contains("開始"))
        {
            throw new System.InvalidOperationException("Gua Unity smoke tree did not round-trip UTF-8 node data.");
        }

        Debug.Log("Gua netstandard2.1 Unity smoke passed: " + tree);
    }

    private void OnDestroy()
    {
        _context?.Dispose();
        _context = null;
    }
}
