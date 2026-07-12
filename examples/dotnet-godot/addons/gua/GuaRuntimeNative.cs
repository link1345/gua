using System.Reflection;
using System.Runtime.InteropServices;
using Gua.Core;

namespace Gua.Godot;

internal static class GuaRuntimeNative
{
    internal const int NodeIdBufferSize = 128;

    internal unsafe struct NativeEvent
    {
        public int Type;
        public fixed byte NodeId[NodeIdBufferSize];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ScreenshotRequest
    {
        public uint StructSize;
        public ulong RequestId;
        public ulong SessionEpoch;
        public ulong AfterFrameSequence;
    }

    static GuaRuntimeNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(GuaRuntimeNative).Assembly, ResolveRuntimeLibrary);
    }

    private static nint ResolveRuntimeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, "gua_runtime", StringComparison.Ordinal))
        {
            return nint.Zero;
        }

        var fileName = OperatingSystem.IsWindows()
            ? "gua_runtime.dll"
            : OperatingSystem.IsMacOS()
                ? "libgua_runtime.dylib"
                : "libgua_runtime.so";

        foreach (var directory in CandidateNativeDirectories(assembly))
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }
        }

        return NativeLibrary.TryLoad(libraryName, assembly, searchPath, out var fallbackHandle)
            ? fallbackHandle
            : nint.Zero;
    }

    private static IEnumerable<string> CandidateNativeDirectories(Assembly assembly)
    {
        var configured = Environment.GetEnvironmentVariable("GUA_RUNTIME_NATIVE_DIR");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            yield return configured;
        }

        var legacyConfigured = Environment.GetEnvironmentVariable("GUA_NATIVE_DIR");
        if (!string.IsNullOrWhiteSpace(legacyConfigured))
        {
            yield return legacyConfigured;
        }

        yield return AppContext.BaseDirectory;

        var assemblyLocation = assembly.Location;
        if (!string.IsNullOrWhiteSpace(assemblyLocation))
        {
            var assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
            if (!string.IsNullOrWhiteSpace(assemblyDirectory))
            {
                yield return assemblyDirectory;
            }
        }

        yield return Environment.CurrentDirectory;
    }

    [DllImport("gua_runtime")]
    internal static extern nint gua_runtime_create();

    [DllImport("gua_runtime")]
    internal static extern void gua_runtime_destroy(nint runtime);

    [DllImport("gua_runtime")]
    internal static extern void gua_runtime_begin_frame(nint runtime, [MarshalAs(UnmanagedType.LPUTF8Str)] string screen);

    [DllImport("gua_runtime")]
    internal static extern void gua_runtime_end_frame(nint runtime);

    [DllImport("gua_runtime")]
    internal static extern void gua_runtime_register_node(
        nint runtime,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string id,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string role,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string label,
        GuaBounds bounds,
        int visible,
        int enabled);

    [DllImport("gua_runtime")]
    internal static extern nint gua_runtime_get_ui_tree_json(nint runtime);

    [DllImport("gua_runtime")]
    internal static extern void gua_runtime_add_log(nint runtime, int level, [MarshalAs(UnmanagedType.LPUTF8Str)] string message);

    [DllImport("gua_runtime")]
    internal static unsafe extern int gua_runtime_find_node_by_role(
        nint runtime,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string role,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        byte* outNodeId,
        int outNodeIdSize);

    [DllImport("gua_runtime")]
    internal static extern int gua_runtime_enqueue_click(nint runtime, [MarshalAs(UnmanagedType.LPUTF8Str)] string nodeId);

    [DllImport("gua_runtime")]
    internal static extern int gua_runtime_consume_click_request(nint runtime, [MarshalAs(UnmanagedType.LPUTF8Str)] string nodeId);

    [DllImport("gua_runtime")]
    internal static extern int gua_runtime_emit_click(nint runtime, [MarshalAs(UnmanagedType.LPUTF8Str)] string nodeId);

    [DllImport("gua_runtime")]
    internal static unsafe extern int gua_runtime_poll_event(nint runtime, NativeEvent* outEvent);

    [DllImport("gua_runtime")]
    internal static extern int gua_runtime_consume_screenshot_request(nint runtime, ref ScreenshotRequest request);

    [DllImport("gua_runtime")]
    internal static extern int gua_runtime_complete_screenshot_request(
        nint runtime, ulong requestId, int result, [MarshalAs(UnmanagedType.LPUTF8Str)] string dataUri, int width, int height);

    [DllImport("gua_runtime")]
    internal static extern int gua_runtime_start_inspector_bridge(nint runtime, int port);

    [DllImport("gua_runtime")]
    internal static extern int gua_runtime_inspector_bridge_running(nint runtime);

    [DllImport("gua_runtime")]
    internal static extern nint gua_runtime_inspector_bridge_url(nint runtime);
}
