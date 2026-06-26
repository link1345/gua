using System.Runtime.InteropServices;

namespace Gua.Core;

internal static partial class Native
{
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

    [LibraryImport("gua", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int gua_get_node_state(nint context, string nodeId, out GuaNodeState state);

    [LibraryImport("gua", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int gua_enqueue_click(nint context, string nodeId);
}
