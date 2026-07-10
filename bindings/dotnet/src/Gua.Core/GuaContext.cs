using System.Runtime.InteropServices;

namespace Gua.Core;

public sealed class GuaContext : IGuaContext, IDisposable
{
    private nint _handle;

    public GuaContext()
    {
        try
        {
            _handle = Native.gua_create_context();
        }
        catch (DllNotFoundException ex)
        {
            throw new InvalidOperationException(Native.NativeLoadErrorMessage(ex), ex);
        }
        catch (BadImageFormatException ex)
        {
            throw new InvalidOperationException(Native.NativeLoadErrorMessage(ex), ex);
        }

        if (_handle == nint.Zero)
        {
            throw new InvalidOperationException("Failed to create a Gua context.");
        }
    }

    public void BeginFrame(string screen)
    {
        ThrowIfDisposed();
        Native.gua_begin_frame(_handle, screen);
    }

    public void EndFrame()
    {
        ThrowIfDisposed();
        Native.gua_end_frame(_handle);
    }

    public void RegisterNode(
        string id,
        string role,
        string label,
        GuaBounds bounds,
        bool visible = true,
        bool enabled = true)
    {
        ThrowIfDisposed();
        Native.gua_register_node(
            _handle,
            id,
            role,
            label,
            bounds,
            visible ? 1 : 0,
            enabled ? 1 : 0);
    }

    public void RegisterNode(GuaNodeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ThrowIfDisposed();

        var known = GuaNodeKnownState.None;
        if (descriptor.ParentId is not null) known |= GuaNodeKnownState.ParentId;
        if (descriptor.Text is not null) known |= GuaNodeKnownState.Text;
        if (descriptor.Value is not null) known |= GuaNodeKnownState.Value;
        if (descriptor.Focused.HasValue) known |= GuaNodeKnownState.Focused;
        if (descriptor.Hovered.HasValue) known |= GuaNodeKnownState.Hovered;
        if (descriptor.Pressed.HasValue) known |= GuaNodeKnownState.Pressed;
        if (descriptor.Checked.HasValue) known |= GuaNodeKnownState.Checked;
        if (descriptor.Selected.HasValue) known |= GuaNodeKnownState.Selected;

        var strings = new[] { descriptor.Id, descriptor.ParentId, descriptor.Role, descriptor.Label, descriptor.Text, descriptor.Value };
        var pointers = strings.Select(value => value is null ? nint.Zero : System.Runtime.InteropServices.Marshal.StringToCoTaskMemUTF8(value)).ToArray();
        try
        {
            var native = new Native.GuaNativeNodeDescriptorV2
            {
                StructSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.GuaNativeNodeDescriptorV2>(),
                KnownMask = (ulong)known,
                Id = pointers[0],
                ParentId = pointers[1],
                Role = pointers[2],
                Label = pointers[3],
                Text = pointers[4],
                Value = pointers[5],
                Bounds = descriptor.Bounds,
                Visible = descriptor.Visible ? 1 : 0,
                Enabled = descriptor.Enabled ? 1 : 0,
                Focused = descriptor.Focused == true ? 1 : 0,
                Hovered = descriptor.Hovered == true ? 1 : 0,
                Pressed = descriptor.Pressed == true ? 1 : 0,
                Checked = descriptor.Checked == true ? 1 : 0,
                Selected = descriptor.Selected == true ? 1 : 0,
            };
            if (Native.gua_register_node_v2(_handle, in native) == 0)
            {
                throw new InvalidOperationException($"Failed to register Gua v2 node: {descriptor.Id}");
            }
        }
        finally
        {
            foreach (var pointer in pointers.Where(pointer => pointer != nint.Zero))
            {
                System.Runtime.InteropServices.Marshal.FreeCoTaskMem(pointer);
            }
        }
    }

    public string GetUiTreeJson()
    {
        ThrowIfDisposed();
        return ReadCopiedJson(JsonSource.UiTree, _handle);
    }

    public string GetDiagnosticsJson()
    {
        ThrowIfDisposed();
        return ReadCopiedJson(JsonSource.Diagnostics, _handle);
    }

    public void ConfigureDiagnostics(uint historyLimit, string environmentJson = "{}")
    {
        ThrowIfDisposed();
        if (Native.gua_set_diagnostics_history_limit(_handle, historyLimit) == 0 ||
            Native.gua_set_diagnostics_environment_json(_handle, environmentJson) == 0)
        {
            throw new ArgumentException("Invalid Gua diagnostics configuration.", nameof(environmentJson));
        }
    }

    public unsafe GuaContextStatus GetContextStatus()
    {
        ThrowIfDisposed();
        Native.GuaNativeContextStatus status = default;
        status.StructSize = (uint)sizeof(Native.GuaNativeContextStatus);
        if (Native.gua_get_context_status(_handle, &status) == 0)
        {
            throw new InvalidOperationException("Failed to inspect the Gua context.");
        }

        return new GuaContextStatus(
            status.SessionEpoch, status.FrameSequence, status.Revision, status.NodeCount,
            status.PendingRequestCount, status.InFlightRequestCount, status.UnconsumedEventCount,
            status.LogCount, status.HasScreenshot != 0, ActionOrNull(status.FirstPendingAction),
            Marshal.PtrToStringUTF8((nint)status.FirstPendingNodeId) ?? string.Empty,
            ActionOrNull(status.FirstEventAction),
            Marshal.PtrToStringUTF8((nint)status.FirstEventNodeId) ?? string.Empty);
    }

    public unsafe GuaResetReport Reset(GuaResetOptions? options = null)
    {
        ThrowIfDisposed();
        options ??= new GuaResetOptions();
        var nativeOptions = new Native.GuaNativeResetOptions
        {
            StructSize = (uint)sizeof(Native.GuaNativeResetOptions),
            Flags = (uint)options.Targets,
            Strict = options.Strict ? 1 : 0,
            ExpectedSessionEpoch = options.ExpectedSessionEpoch ?? 0,
        };
        Native.GuaNativeResetReport report = default;
        report.StructSize = (uint)sizeof(Native.GuaNativeResetReport);
        var result = Native.gua_reset_context(_handle, in nativeOptions, &report);
        if (result == (int)GuaResetResult.InvalidArgument)
        {
            throw new ArgumentException("Invalid Gua reset options.", nameof(options));
        }
        return new GuaResetReport(
            (GuaResetResult)result, report.PreviousSessionEpoch, report.SessionEpoch,
            report.PendingRequestCount, report.InFlightRequestCount, report.UnconsumedEventCount,
            report.DiscardedNodeCount, report.DiscardedPendingRequestCount, report.DiscardedInFlightRequestCount,
            report.DiscardedEventCount, report.DiscardedLogCount, report.DiscardedScreenshot != 0,
            ActionOrNull(report.FirstPendingAction), Marshal.PtrToStringUTF8((nint)report.FirstPendingNodeId) ?? string.Empty,
            ActionOrNull(report.FirstEventAction), Marshal.PtrToStringUTF8((nint)report.FirstEventNodeId) ?? string.Empty);
    }

    private static GuaActionType? ActionOrNull(int action) => action == 0 ? null : (GuaActionType)action;

    public void AddLog(GuaLogLevel level, string message)
    {
        ThrowIfDisposed();
        Native.gua_add_log(_handle, (int)level, message);
    }

    public string GetLogsJson()
    {
        ThrowIfDisposed();
        return ReadCopiedJson(JsonSource.Logs, _handle);
    }

    public void SetScreenshot(string dataUri, int width, int height)
    {
        ThrowIfDisposed();
        Native.gua_set_screenshot(_handle, dataUri, width, height);
    }

    public string GetScreenshotJson()
    {
        ThrowIfDisposed();
        return ReadCopiedJson(JsonSource.Screenshot, _handle);
    }

    public GuaNodeState GetNodeState(string id)
    {
        ThrowIfDisposed();
        if (Native.gua_get_node_state(_handle, id, out var state) == 0)
        {
            throw new InvalidOperationException($"Gua node not found: {id}");
        }
        return state;
    }

    public GuaNodeStateV2 GetNodeStateV2(string id)
    {
        ThrowIfDisposed();
        unsafe
        {
            Native.GuaNativeNodeStateV2 state = default;
            state.StructSize = (uint)sizeof(Native.GuaNativeNodeStateV2);
            if (Native.gua_get_node_state_v2(_handle, id, &state) == 0)
            {
                throw new InvalidOperationException($"Gua node not found: {id}");
            }

            var known = (GuaNodeKnownState)state.KnownMask;
            string? Read(byte* value, GuaNodeKnownState flag) => known.HasFlag(flag)
                ? System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)value) ?? string.Empty
                : null;
            bool? ReadBool(int value, GuaNodeKnownState flag) => known.HasFlag(flag) ? value != 0 : null;

            return new GuaNodeStateV2(
                known,
                state.Visible != 0,
                state.Enabled != 0,
                Read(state.ParentId, GuaNodeKnownState.ParentId),
                Read(state.Text, GuaNodeKnownState.Text),
                Read(state.Value, GuaNodeKnownState.Value),
                ReadBool(state.Focused, GuaNodeKnownState.Focused),
                ReadBool(state.Hovered, GuaNodeKnownState.Hovered),
                ReadBool(state.Pressed, GuaNodeKnownState.Pressed),
                ReadBool(state.Checked, GuaNodeKnownState.Checked),
                ReadBool(state.Selected, GuaNodeKnownState.Selected));
        }
    }

    public string FindNodeById(string id)
    {
        ThrowIfDisposed();
        unsafe
        {
            var buffer = stackalloc byte[Native.NodeIdBufferSize];
            if (Native.gua_find_node_by_id(_handle, id, buffer, Native.NodeIdBufferSize) == 0)
            {
                throw new InvalidOperationException($"Gua node not found by id: {id}");
            }

            return ReadUtf8NodeId(buffer);
        }
    }

    public string FindNodeByRole(string role, string? name = null)
    {
        ThrowIfDisposed();
        unsafe
        {
            var buffer = stackalloc byte[Native.NodeIdBufferSize];
            if (Native.gua_find_node_by_role(_handle, role, name, buffer, Native.NodeIdBufferSize) == 0)
            {
                throw new InvalidOperationException(name is null
                    ? $"Gua node not found by role: {role}"
                    : $"Gua node not found by role and name: {role}, {name}");
            }

            return ReadUtf8NodeId(buffer);
        }
    }

    public string FindNodeByText(string text)
    {
        ThrowIfDisposed();
        unsafe
        {
            var buffer = stackalloc byte[Native.NodeIdBufferSize];
            if (Native.gua_find_node_by_text(_handle, text, buffer, Native.NodeIdBufferSize) == 0)
            {
                throw new InvalidOperationException($"Gua node not found by text: {text}");
            }

            return ReadUtf8NodeId(buffer);
        }
    }

    public GuaQueryResult Query(GuaSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ThrowIfDisposed();
        var values = new[] { selector.Id, selector.Role, selector.Name, selector.Text, selector.ParentId };
        var pointers = values.Select(value => value is null ? nint.Zero : System.Runtime.InteropServices.Marshal.StringToCoTaskMemUTF8(value)).ToArray();
        try
        {
            var native = new Native.GuaNativeSelectorV1
            {
                StructSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.GuaNativeSelectorV1>(),
                Id = pointers[0], IdMatch = (int)selector.IdMatch,
                Role = pointers[1], RoleMatch = (int)selector.RoleMatch,
                Name = pointers[2], NameMatch = (int)selector.NameMatch,
                Text = pointers[3], TextMatch = (int)selector.TextMatch,
                ParentId = pointers[4], DirectChild = selector.DirectChild ? 1 : 0,
                Visible = (int)selector.Visible, Enabled = (int)selector.Enabled,
            };
            unsafe
            {
                var required = Native.gua_query_nodes_json(_handle, in native, null, 0);
                var buffer = new byte[required];
                fixed (byte* pointer = buffer)
                {
                    Native.gua_query_nodes_json(_handle, in native, pointer, buffer.Length);
                    var json = System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)pointer)
                        ?? throw new InvalidOperationException("Native Gua returned invalid selector JSON.");
                    return System.Text.Json.JsonSerializer.Deserialize<GuaQueryResult>(json, QueryJsonOptions)
                        ?? throw new InvalidOperationException("Native Gua returned an empty selector result.");
                }
            }
        }
        finally
        {
            foreach (var pointer in pointers.Where(pointer => pointer != nint.Zero))
                System.Runtime.InteropServices.Marshal.FreeCoTaskMem(pointer);
        }
    }

    public bool EnqueueClick(string id)
    {
        ThrowIfDisposed();
        return Native.gua_enqueue_click(_handle, id) != 0;
    }

    public GuaActionError EnqueueAction(GuaActionRequest request, out ulong requestId)
    {
        ThrowIfDisposed();
        var nodeId = Marshal.StringToCoTaskMemUTF8(request.NodeId);
        var value = Marshal.StringToCoTaskMemUTF8(request.Value);
        var key = Marshal.StringToCoTaskMemUTF8(request.Key);
        try
        {
            var descriptor = new Native.GuaNativeActionRequestDescriptor
            {
                StructSize = (uint)Marshal.SizeOf<Native.GuaNativeActionRequestDescriptor>(),
                Action = (int)request.Action,
                NodeId = nodeId,
                Value = value,
                DeltaX = request.DeltaX,
                DeltaY = request.DeltaY,
                BoolValue = request.BoolValue ? 1 : 0,
                Key = key,
                Modifiers = request.Modifiers,
                Sensitive = request.Sensitive ? 1 : 0,
                ScrollUnit = request.ScrollUnit,
            };
            var result = Native.gua_enqueue_action(_handle, in descriptor, out requestId);
            return result == 1 ? GuaActionError.None : (GuaActionError)result;
        }
        finally
        {
            Marshal.FreeCoTaskMem(nodeId);
            Marshal.FreeCoTaskMem(value);
            Marshal.FreeCoTaskMem(key);
        }
    }

    public bool TryPollActionEvent(out GuaActionEvent e)
    {
        return TryPollActionEventCore(null, out e);
    }

    public bool TryPollActionEvent(ulong requestId, out GuaActionEvent e)
    {
        ArgumentOutOfRangeException.ThrowIfZero(requestId);
        return TryPollActionEventCore(requestId, out e);
    }

    private bool TryPollActionEventCore(ulong? requestId, out GuaActionEvent e)
    {
        ThrowIfDisposed();
        unsafe
        {
            Native.GuaNativeEventV2 nativeEvent = new()
            {
                StructSize = (uint)sizeof(Native.GuaNativeEventV2),
            };
            var found = requestId.HasValue
                ? Native.gua_poll_event_v2_for_request(_handle, requestId.Value, &nativeEvent)
                : Native.gua_poll_event_v2(_handle, &nativeEvent);
            if (found == 0)
            {
                e = default;
                return false;
            }
            e = new GuaActionEvent(
                nativeEvent.RequestId,
                (GuaActionType)nativeEvent.Action,
                nativeEvent.Status == 1,
                (GuaActionError)nativeEvent.ErrorCode,
                Marshal.PtrToStringUTF8((nint)nativeEvent.NodeId) ?? string.Empty,
                Marshal.PtrToStringUTF8((nint)nativeEvent.Value) ?? string.Empty,
                nativeEvent.Sensitive != 0);
            return true;
        }
    }

    public bool TryConsumeAction(GuaActionType action, string? nodeId, out GuaActionRequest request)
    {
        ThrowIfDisposed();
        unsafe
        {
            Native.GuaNativeActionRequest nativeRequest = new() { StructSize = (uint)sizeof(Native.GuaNativeActionRequest) };
            if (Native.gua_consume_action_request(_handle, (int)action, nodeId, &nativeRequest) == 0)
            {
                request = default;
                return false;
            }
            request = new GuaActionRequest(
                (GuaActionType)nativeRequest.Action,
                Marshal.PtrToStringUTF8((nint)nativeRequest.NodeId),
                Marshal.PtrToStringUTF8((nint)nativeRequest.Value),
                nativeRequest.DeltaX,
                nativeRequest.DeltaY,
                nativeRequest.BoolValue != 0,
                Marshal.PtrToStringUTF8((nint)nativeRequest.Key),
                nativeRequest.Modifiers,
                nativeRequest.Sensitive != 0,
                nativeRequest.ScrollUnit,
                nativeRequest.RequestId);
            return true;
        }
    }

    public bool EmitActionResult(GuaActionEvent e)
    {
        ThrowIfDisposed();
        var nodeId = Marshal.StringToCoTaskMemUTF8(e.NodeId);
        var value = Marshal.StringToCoTaskMemUTF8(e.Value);
        try
        {
            var result = new Native.GuaNativeActionResult
            {
                StructSize = (uint)Marshal.SizeOf<Native.GuaNativeActionResult>(),
                RequestId = e.RequestId,
                Action = (int)e.Action,
                Status = e.Succeeded ? 1 : 2,
                ErrorCode = (int)e.Error,
                NodeId = nodeId,
                Value = value,
                Sensitive = e.Sensitive ? 1 : 0,
            };
            return Native.gua_emit_action_result(_handle, in result) != 0;
        }
        finally
        {
            Marshal.FreeCoTaskMem(nodeId);
            Marshal.FreeCoTaskMem(value);
        }
    }

    public bool ConsumeClickRequest(string id)
    {
        ThrowIfDisposed();
        return Native.gua_consume_click_request(_handle, id) != 0;
    }

    public bool EmitClick(string id)
    {
        ThrowIfDisposed();
        return Native.gua_emit_click(_handle, id) != 0;
    }

    public bool TryPollEvent(out GuaEvent e)
    {
        ThrowIfDisposed();

        unsafe
        {
            Native.GuaNativeEvent nativeEvent;
            if (Native.gua_poll_event(_handle, &nativeEvent) == 0)
            {
                e = default;
                return false;
            }

            e = new GuaEvent(
                (GuaEventType)nativeEvent.Type,
                ReadNativeEventNodeId(&nativeEvent));
            return true;
        }
    }

    public void Dispose()
    {
        if (_handle == nint.Zero)
        {
            return;
        }

        Native.gua_destroy_context(_handle);
        _handle = nint.Zero;
    }

    private void ThrowIfDisposed()
    {
        if (_handle == nint.Zero)
        {
            throw new ObjectDisposedException(nameof(GuaContext));
        }
    }

    private static unsafe string ReadUtf8NodeId(byte* buffer)
    {
        return System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)buffer)
            ?? throw new InvalidOperationException("Native Gua returned an invalid node id.");
    }

    private static unsafe string ReadNativeEventNodeId(Native.GuaNativeEvent* nativeEvent)
    {
        return System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)nativeEvent->NodeId)
            ?? throw new InvalidOperationException("Native Gua returned an invalid event node id.");
    }

    private static unsafe string ReadCopiedJson(JsonSource source, nint handle)
    {
        var requiredSize = CopyJson(source, handle, null, 0);
        if (requiredSize <= 0)
        {
            throw new InvalidOperationException("Native Gua returned an invalid JSON size.");
        }

        while (true)
        {
            var buffer = new byte[requiredSize];
            fixed (byte* bufferPointer = buffer)
            {
                var actualSize = CopyJson(source, handle, bufferPointer, buffer.Length);
                if (actualSize <= 0)
                {
                    throw new InvalidOperationException("Native Gua returned an invalid JSON size.");
                }

                if (actualSize <= buffer.Length)
                {
                    return System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)bufferPointer)
                        ?? throw new InvalidOperationException("Native Gua returned an invalid UTF-8 string.");
                }

                requiredSize = actualSize;
            }
        }
    }

    private static unsafe int CopyJson(JsonSource source, nint handle, byte* buffer, int bufferSize)
    {
        return source switch
        {
            JsonSource.UiTree => Native.gua_copy_ui_tree_json(handle, buffer, bufferSize),
            JsonSource.Logs => Native.gua_copy_logs_json(handle, buffer, bufferSize),
            JsonSource.Screenshot => Native.gua_copy_screenshot_json(handle, buffer, bufferSize),
            JsonSource.Diagnostics => Native.gua_copy_diagnostics_json(handle, buffer, bufferSize),
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
        };
    }

    private enum JsonSource
    {
        UiTree,
        Logs,
        Screenshot,
        Diagnostics,
    }

    private static readonly System.Text.Json.JsonSerializerOptions QueryJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
