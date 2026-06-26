namespace Gua.Core;

public sealed class GuaContext : IDisposable
{
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
}
