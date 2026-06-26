namespace Gua.Core;

public sealed class GuaContext : IDisposable
{
    private const int NodeIdBufferSize = 128;

    private nint _handle;

    public GuaContext()
    {
        _handle = Native.gua_create_context();
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

    public GuaNodeState GetNodeState(string id)
    {
        ThrowIfDisposed();
        if (Native.gua_get_node_state(_handle, id, out var state) == 0)
        {
            throw new InvalidOperationException($"Gua node not found: {id}");
        }
        return state;
    }

    public string FindNodeById(string id)
    {
        ThrowIfDisposed();
        unsafe
        {
            var buffer = stackalloc byte[NodeIdBufferSize];
            if (Native.gua_find_node_by_id(_handle, id, buffer, NodeIdBufferSize) == 0)
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
            var buffer = stackalloc byte[NodeIdBufferSize];
            if (Native.gua_find_node_by_role(_handle, role, name, buffer, NodeIdBufferSize) == 0)
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
            var buffer = stackalloc byte[NodeIdBufferSize];
            if (Native.gua_find_node_by_text(_handle, text, buffer, NodeIdBufferSize) == 0)
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
}
