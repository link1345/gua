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

    public bool EnqueueClick(string id)
    {
        ThrowIfDisposed();
        return Native.gua_enqueue_click(_handle, id) != 0;
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
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
        };
    }

    private enum JsonSource
    {
        UiTree,
        Logs,
        Screenshot,
    }
}
