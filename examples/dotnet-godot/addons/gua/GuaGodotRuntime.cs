using Godot;
using Gua.Core;

namespace Gua.Godot;

public sealed class GuaGodotRuntime : IDisposable
{
    private nint _runtime;

    public GuaGodotRuntime()
    {
        System.Environment.SetEnvironmentVariable(
            "GUA_RUNTIME_NATIVE_DIR",
            ProjectSettings.GlobalizePath("res://addons/gua/bin"));
        _runtime = GuaRuntimeNative.gua_runtime_create();
        if (_runtime == nint.Zero)
        {
            throw new InvalidOperationException("Failed to create a Gua runtime.");
        }
    }

    public bool InspectorBridgeRunning
    {
        get
        {
            ThrowIfDisposed();
            return GuaRuntimeNative.gua_runtime_inspector_bridge_running(_runtime) != 0;
        }
    }

    public string InspectorBridgeUrl
    {
        get
        {
            ThrowIfDisposed();
            return ReadNativeUtf8(GuaRuntimeNative.gua_runtime_inspector_bridge_url(_runtime));
        }
    }

    public bool StartInspectorBridge(int port = 8765)
    {
        ThrowIfDisposed();
        return GuaRuntimeNative.gua_runtime_start_inspector_bridge(_runtime, port) != 0;
    }

    public void BeginFrame(string screen)
    {
        ThrowIfDisposed();
        GuaRuntimeNative.gua_runtime_begin_frame(_runtime, screen);
    }

    public void EndFrame()
    {
        ThrowIfDisposed();
        GuaRuntimeNative.gua_runtime_end_frame(_runtime);
    }

    public void RegisterNode(
        string id,
        string role,
        string label,
        Rect2 bounds,
        bool visible = true,
        bool enabled = true)
    {
        ThrowIfDisposed();
        GuaRuntimeNative.gua_runtime_register_node(
            _runtime,
            id,
            role,
            label,
            new GuaBounds(bounds.Position.X, bounds.Position.Y, bounds.Size.X, bounds.Size.Y),
            visible ? 1 : 0,
            enabled ? 1 : 0);
    }

    public void RegisterControlNode(string id, string role, string label, Control control, bool enabled)
    {
        var disabled = control is BaseButton { Disabled: true };
        RegisterNode(
            id,
            role,
            label,
            new Rect2(control.GlobalPosition, control.Size),
            control.Visible,
            enabled && !disabled);
    }

    public bool EnqueueClick(string id)
    {
        ThrowIfDisposed();
        return GuaRuntimeNative.gua_runtime_enqueue_click(_runtime, id) != 0;
    }

    public void ClickByRole(string role, string label)
    {
        ThrowIfDisposed();
        unsafe
        {
            var buffer = stackalloc byte[GuaRuntimeNative.NodeIdBufferSize];
            if (GuaRuntimeNative.gua_runtime_find_node_by_role(_runtime, role, label, buffer, GuaRuntimeNative.NodeIdBufferSize) == 0)
            {
                throw new InvalidOperationException($"Gua node not found by role and name: {role}, {label}");
            }

            var nodeId = ReadNativeUtf8((nint)buffer);
            if (!EnqueueClick(nodeId))
            {
                throw new InvalidOperationException($"Gua node is not clickable: {nodeId}");
            }
        }
    }

    public bool TryPollEvent(out GuaEvent e)
    {
        ThrowIfDisposed();
        unsafe
        {
            GuaRuntimeNative.NativeEvent nativeEvent;
            if (GuaRuntimeNative.gua_runtime_poll_event(_runtime, &nativeEvent) == 0)
            {
                e = default;
                return false;
            }

            e = new GuaEvent((GuaEventType)nativeEvent.Type, ReadNativeUtf8((nint)nativeEvent.NodeId));
            return true;
        }
    }

    public void AddLog(GuaLogLevel level, string message)
    {
        ThrowIfDisposed();
        GuaRuntimeNative.gua_runtime_add_log(_runtime, (int)level, message);
    }

    public string GetUiTreeJson()
    {
        ThrowIfDisposed();
        return ReadNativeUtf8(GuaRuntimeNative.gua_runtime_get_ui_tree_json(_runtime));
    }

    public void Dispose()
    {
        if (_runtime == nint.Zero)
        {
            return;
        }

        GuaRuntimeNative.gua_runtime_destroy(_runtime);
        _runtime = nint.Zero;
    }

    private void ThrowIfDisposed()
    {
        if (_runtime == nint.Zero)
        {
            throw new ObjectDisposedException(nameof(GuaGodotRuntime));
        }
    }

    private static string ReadNativeUtf8(nint value)
    {
        return System.Runtime.InteropServices.Marshal.PtrToStringUTF8(value)
            ?? throw new InvalidOperationException("Native Gua runtime returned an invalid UTF-8 string.");
    }
}
