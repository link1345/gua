using System.Reflection;
using System.Runtime.InteropServices;
using Gua.Core;

namespace Gua.Runtime;

internal static unsafe class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct NodeV2 { internal uint StructSize; internal ulong KnownMask; internal nint Id, ParentId, Role, Label, Text, Value; internal GuaBounds Bounds; internal int Visible, Enabled, Focused, Hovered, Pressed, Checked, Selected; }
    [StructLayout(LayoutKind.Sequential)] internal struct NodeV3 { internal uint StructSize; internal NodeV2 Base; internal long CaretPosition, SelectionStart, SelectionEnd; internal double ScrollX, ScrollY, ScrollMaxX, ScrollMaxY, RangeValue, RangeMin, RangeMax; internal long SelectedIndex; }
    [StructLayout(LayoutKind.Sequential)] internal unsafe struct ActionRequest { internal uint StructSize; internal ulong RequestId; internal int Action; internal fixed byte NodeId[128]; internal fixed byte Value[256]; internal float DeltaX, DeltaY; internal int BoolValue; internal fixed byte Key[64]; internal uint Modifiers; internal int Sensitive, ScrollUnit; }
    [StructLayout(LayoutKind.Sequential)] internal struct ActionResult { internal uint StructSize; internal ulong RequestId; internal int Action, Status, ErrorCode; internal nint NodeId, Value; internal int Sensitive; }
    [StructLayout(LayoutKind.Sequential)] internal struct ScreenshotRequest { internal uint StructSize; internal ulong RequestId, SessionEpoch, AfterFrameSequence; }
    [StructLayout(LayoutKind.Sequential)] internal unsafe struct LegacyEvent { internal int Type; internal fixed byte NodeId[128]; }

#if !NETSTANDARD2_1
    static Native() => NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, Resolve);
    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != "gua_runtime") return 0;
        var file = OperatingSystem.IsWindows() ? "gua_runtime.dll" : OperatingSystem.IsMacOS() ? "libgua_runtime.dylib" : "libgua_runtime.so";
        foreach (var directory in CandidateDirectories(assembly))
        {
            var candidate = Path.Combine(directory, file);
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle)) return handle;
        }
        return NativeLibrary.TryLoad(name, assembly, searchPath, out var fallback) ? fallback : 0;
    }
    private static IEnumerable<string> CandidateDirectories(Assembly assembly)
    {
        var configured = Environment.GetEnvironmentVariable("GUA_RUNTIME_NATIVE_DIR"); if (!string.IsNullOrWhiteSpace(configured)) yield return configured;
        var legacy = Environment.GetEnvironmentVariable("GUA_NATIVE_DIR"); if (!string.IsNullOrWhiteSpace(legacy)) yield return legacy;
        yield return AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(assembly.Location) && Path.GetDirectoryName(assembly.Location) is { } directory) yield return directory;
        yield return Environment.CurrentDirectory;
    }
#endif

    private const string Library = "gua_runtime";
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint gua_runtime_create();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void gua_runtime_destroy(nint runtime);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void gua_runtime_begin_frame(nint runtime, [MarshalAs(UnmanagedType.LPUTF8Str)] string screen);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void gua_runtime_end_frame(nint runtime);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int gua_runtime_register_node_v3(nint runtime, in NodeV3 node);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int gua_runtime_consume_action_request(nint runtime, int action, [MarshalAs(UnmanagedType.LPUTF8Str)] string? nodeId, ref ActionRequest request);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int gua_runtime_emit_action_result(nint runtime, in ActionResult result);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int gua_runtime_consume_screenshot_request(nint runtime, ref ScreenshotRequest request);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int gua_runtime_complete_screenshot_request(nint runtime, ulong requestId, int result, [MarshalAs(UnmanagedType.LPUTF8Str)] string dataUri, int width, int height);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void gua_runtime_add_log(nint runtime, int level, [MarshalAs(UnmanagedType.LPUTF8Str)] string message);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern unsafe int gua_runtime_copy_ui_tree_json(nint runtime, byte* output, int size);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern unsafe int gua_runtime_copy_version_json(nint runtime, byte* output, int size);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void gua_runtime_set_adapter_version(nint runtime, [MarshalAs(UnmanagedType.LPUTF8Str)] string adapter, [MarshalAs(UnmanagedType.LPUTF8Str)] string version);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void gua_runtime_set_godot_plugin_version(nint runtime, [MarshalAs(UnmanagedType.LPUTF8Str)] string version);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int gua_runtime_enqueue_click(nint runtime, [MarshalAs(UnmanagedType.LPUTF8Str)] string id);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int gua_runtime_consume_click_request(nint runtime, [MarshalAs(UnmanagedType.LPUTF8Str)] string id);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int gua_runtime_emit_click(nint runtime, [MarshalAs(UnmanagedType.LPUTF8Str)] string id);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern unsafe int gua_runtime_find_node_by_role(nint runtime, [MarshalAs(UnmanagedType.LPUTF8Str)] string role, [MarshalAs(UnmanagedType.LPUTF8Str)] string label, byte* output, int size);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern unsafe int gua_runtime_poll_event(nint runtime, LegacyEvent* result);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int gua_runtime_start_inspector_bridge(nint runtime, int port);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void gua_runtime_stop_inspector_bridge(nint runtime);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int gua_runtime_inspector_bridge_running(nint runtime);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint gua_runtime_inspector_bridge_url(nint runtime);
}
