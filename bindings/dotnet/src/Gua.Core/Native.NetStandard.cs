#if NETSTANDARD2_1
using System.Runtime.InteropServices;

namespace Gua.Core;

internal static partial class Native
{
    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint gua_create_context();

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void gua_destroy_context(nint context);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void gua_begin_frame(nint context, [MarshalAs(UnmanagedType.LPUTF8Str)] string screen);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void gua_end_frame(nint context);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void gua_register_node(
        nint context,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string id,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string role,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string label,
        GuaBounds bounds,
        int visible,
        int enabled);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int gua_register_node_v2(nint context, in GuaNativeNodeDescriptorV2 descriptor);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int gua_register_node_v3(nint context, in GuaNativeNodeDescriptorV3 descriptor);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint gua_get_ui_tree_json(nint context);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe int gua_copy_ui_tree_json(nint context, byte* outJson, int outJsonSize);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void gua_add_log(nint context, int level, [MarshalAs(UnmanagedType.LPUTF8Str)] string message);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint gua_get_logs_json(nint context);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe int gua_copy_logs_json(nint context, byte* outJson, int outJsonSize);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void gua_set_screenshot(nint context, [MarshalAs(UnmanagedType.LPUTF8Str)] string dataUri, int width, int height);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint gua_get_screenshot_json(nint context);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe int gua_copy_screenshot_json(nint context, byte* outJson, int outJsonSize);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int gua_set_diagnostics_history_limit(nint context, uint historyLimit);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int gua_set_diagnostics_environment_json(nint context, [MarshalAs(UnmanagedType.LPUTF8Str)] string environmentJson);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe int gua_copy_diagnostics_json(nint context, byte* outJson, int outJsonSize);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe int gua_copy_version_json(byte* outJson, int outJsonSize);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int gua_get_node_state(nint context, [MarshalAs(UnmanagedType.LPUTF8Str)] string nodeId, out GuaNodeState state);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe int gua_get_node_state_v2(nint context, [MarshalAs(UnmanagedType.LPUTF8Str)] string nodeId, GuaNativeNodeStateV2* state);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe int gua_find_node_by_id(nint context, [MarshalAs(UnmanagedType.LPUTF8Str)] string nodeId, byte* outNodeId, int outNodeIdSize);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe int gua_find_node_by_role(
        nint context,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string role,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? name,
        byte* outNodeId,
        int outNodeIdSize);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe int gua_find_node_by_text(nint context, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, byte* outNodeId, int outNodeIdSize);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe int gua_query_nodes_json(nint context, in GuaNativeSelectorV1 selector, byte* outJson, int outJsonSize);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int gua_enqueue_click(nint context, [MarshalAs(UnmanagedType.LPUTF8Str)] string nodeId);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int gua_consume_click_request(nint context, [MarshalAs(UnmanagedType.LPUTF8Str)] string nodeId);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int gua_emit_click(nint context, [MarshalAs(UnmanagedType.LPUTF8Str)] string nodeId);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe int gua_poll_event(nint context, GuaNativeEvent* outEvent);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int gua_enqueue_action(nint context, in GuaNativeActionRequestDescriptor descriptor, out ulong requestId);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe int gua_poll_event_v2(nint context, GuaNativeEventV2* outEvent);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe int gua_poll_event_v2_for_request(nint context, ulong requestId, GuaNativeEventV2* outEvent);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe int gua_poll_event_v3(nint context, GuaNativeEventV3* outEvent);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe int gua_poll_event_v3_for_request(nint context, ulong requestId, GuaNativeEventV3* outEvent);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe int gua_consume_action_request(
        nint context,
        int action,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? nodeId,
        GuaNativeActionRequest* outRequest);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int gua_emit_action_result(nint context, in GuaNativeActionResult result);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe int gua_get_context_status(nint context, GuaNativeContextStatus* status);

    [DllImport("gua", CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe int gua_reset_context(nint context, in GuaNativeResetOptions options, GuaNativeResetReport* report);
}
#endif
