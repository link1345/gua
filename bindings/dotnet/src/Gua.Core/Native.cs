using System.Runtime.InteropServices;
using System.Reflection;

namespace Gua.Core;

internal static partial class Native
{
    internal const int NodeIdBufferSize = 128;

    internal unsafe struct GuaNativeEvent
    {
        public int Type;
        public fixed byte NodeId[NodeIdBufferSize];
    }

    static Native()
    {
        NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, ResolveGuaLibrary);
    }

    private static nint ResolveGuaLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, "gua", StringComparison.Ordinal))
        {
            return nint.Zero;
        }

        var fileName = OperatingSystem.IsWindows()
            ? "gua.dll"
            : OperatingSystem.IsMacOS()
                ? "libgua.dylib"
                : "libgua.so";

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
        var configured = Environment.GetEnvironmentVariable("GUA_NATIVE_DIR");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            yield return configured;
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

    [LibraryImport("gua")]
    internal static partial nint gua_create_context();

    [LibraryImport("gua")]
    internal static partial void gua_destroy_context(nint context);

    [LibraryImport("gua", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void gua_begin_frame(nint context, string screen);

    [LibraryImport("gua")]
    internal static partial void gua_end_frame(nint context);

    [LibraryImport("gua", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void gua_register_node(
        nint context,
        string id,
        string role,
        string label,
        GuaBounds bounds,
        int visible,
        int enabled);

    [LibraryImport("gua")]
    internal static partial nint gua_get_ui_tree_json(nint context);

    [LibraryImport("gua", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void gua_add_log(nint context, int level, string message);

    [LibraryImport("gua")]
    internal static partial nint gua_get_logs_json(nint context);

    [LibraryImport("gua", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void gua_set_screenshot(nint context, string dataUri, int width, int height);

    [LibraryImport("gua")]
    internal static partial nint gua_get_screenshot_json(nint context);

    [LibraryImport("gua", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int gua_get_node_state(nint context, string nodeId, out GuaNodeState state);

    [LibraryImport("gua", StringMarshalling = StringMarshalling.Utf8)]
    internal static unsafe partial int gua_find_node_by_id(nint context, string nodeId, byte* outNodeId, int outNodeIdSize);

    [LibraryImport("gua", StringMarshalling = StringMarshalling.Utf8)]
    internal static unsafe partial int gua_find_node_by_role(nint context, string role, string? name, byte* outNodeId, int outNodeIdSize);

    [LibraryImport("gua", StringMarshalling = StringMarshalling.Utf8)]
    internal static unsafe partial int gua_find_node_by_text(nint context, string text, byte* outNodeId, int outNodeIdSize);

    [LibraryImport("gua", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int gua_enqueue_click(nint context, string nodeId);

    [LibraryImport("gua")]
    internal static unsafe partial int gua_poll_event(nint context, GuaNativeEvent* outEvent);
}
