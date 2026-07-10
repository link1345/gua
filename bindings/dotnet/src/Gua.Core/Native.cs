using System.Runtime.InteropServices;
using System.Reflection;

namespace Gua.Core;

internal static partial class Native
{
    internal const int NodeIdBufferSize = 128;
    private static readonly object ResolveLock = new();
    private static string[] _lastCandidateDirectories = [];

    internal unsafe struct GuaNativeEvent
    {
        public int Type;
        public fixed byte NodeId[NodeIdBufferSize];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GuaNativeNodeDescriptorV2
    {
        public uint StructSize;
        public ulong KnownMask;
        public nint Id;
        public nint ParentId;
        public nint Role;
        public nint Label;
        public nint Text;
        public nint Value;
        public GuaBounds Bounds;
        public int Visible;
        public int Enabled;
        public int Focused;
        public int Hovered;
        public int Pressed;
        public int Checked;
        public int Selected;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct GuaNativeNodeStateV2
    {
        public uint StructSize;
        public ulong KnownMask;
        public int Visible;
        public int Enabled;
        public int Focused;
        public int Hovered;
        public int Pressed;
        public int Checked;
        public int Selected;
        public fixed byte ParentId[128];
        public fixed byte Text[256];
        public fixed byte Value[256];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GuaNativeSelectorV1
    {
        public uint StructSize;
        public nint Id;
        public int IdMatch;
        public nint Role;
        public int RoleMatch;
        public nint Name;
        public int NameMatch;
        public nint Text;
        public int TextMatch;
        public nint ParentId;
        public int DirectChild;
        public int Visible;
        public int Enabled;
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

        var directories = CandidateNativeDirectories(assembly).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        lock (ResolveLock)
        {
            _lastCandidateDirectories = directories;
        }

        foreach (var directory in directories)
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

    internal static string NativeLoadErrorMessage(Exception exception)
    {
        var fileName = OperatingSystem.IsWindows()
            ? "gua.dll"
            : OperatingSystem.IsMacOS()
                ? "libgua.dylib"
                : "libgua.so";

        string[] directories;
        lock (ResolveLock)
        {
            directories = _lastCandidateDirectories;
        }

        if (directories.Length == 0)
        {
            directories = CandidateNativeDirectories(typeof(Native).Assembly).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        var searched = string.Join(Environment.NewLine, directories.Select(directory => $"  - {Path.Combine(directory, fileName)}"));
        return
            $"Failed to load the native Gua runtime '{fileName}'. Build native/gua-core and make the library discoverable with GUA_NATIVE_DIR, the .NET output directory, or the current directory." +
            $"{Environment.NewLine}Searched:{Environment.NewLine}{searched}{Environment.NewLine}Original error: {exception.Message}";
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
    internal static partial int gua_register_node_v2(nint context, in GuaNativeNodeDescriptorV2 descriptor);

    [LibraryImport("gua")]
    internal static partial nint gua_get_ui_tree_json(nint context);

    [LibraryImport("gua")]
    internal static unsafe partial int gua_copy_ui_tree_json(nint context, byte* outJson, int outJsonSize);

    [LibraryImport("gua", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void gua_add_log(nint context, int level, string message);

    [LibraryImport("gua")]
    internal static partial nint gua_get_logs_json(nint context);

    [LibraryImport("gua")]
    internal static unsafe partial int gua_copy_logs_json(nint context, byte* outJson, int outJsonSize);

    [LibraryImport("gua", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void gua_set_screenshot(nint context, string dataUri, int width, int height);

    [LibraryImport("gua")]
    internal static partial nint gua_get_screenshot_json(nint context);

    [LibraryImport("gua")]
    internal static unsafe partial int gua_copy_screenshot_json(nint context, byte* outJson, int outJsonSize);

    [LibraryImport("gua", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int gua_get_node_state(nint context, string nodeId, out GuaNodeState state);

    [LibraryImport("gua", StringMarshalling = StringMarshalling.Utf8)]
    internal static unsafe partial int gua_get_node_state_v2(nint context, string nodeId, GuaNativeNodeStateV2* state);

    [LibraryImport("gua", StringMarshalling = StringMarshalling.Utf8)]
    internal static unsafe partial int gua_find_node_by_id(nint context, string nodeId, byte* outNodeId, int outNodeIdSize);

    [LibraryImport("gua", StringMarshalling = StringMarshalling.Utf8)]
    internal static unsafe partial int gua_find_node_by_role(nint context, string role, string? name, byte* outNodeId, int outNodeIdSize);

    [LibraryImport("gua", StringMarshalling = StringMarshalling.Utf8)]
    internal static unsafe partial int gua_find_node_by_text(nint context, string text, byte* outNodeId, int outNodeIdSize);

    [LibraryImport("gua")]
    internal static unsafe partial int gua_query_nodes_json(nint context, in GuaNativeSelectorV1 selector, byte* outJson, int outJsonSize);

    [LibraryImport("gua", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int gua_enqueue_click(nint context, string nodeId);

    [LibraryImport("gua", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int gua_consume_click_request(nint context, string nodeId);

    [LibraryImport("gua", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int gua_emit_click(nint context, string nodeId);

    [LibraryImport("gua")]
    internal static unsafe partial int gua_poll_event(nint context, GuaNativeEvent* outEvent);
}
