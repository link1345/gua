using Godot;
using Gua.Core;

namespace Gua.Godot;

public sealed class GuaGodotRuntime : IDisposable
{
    private nint _runtime;
    private Control? _attachedRoot;
    private readonly Dictionary<string, BaseButton> _buttonsById = new(StringComparer.Ordinal);
    private readonly HashSet<ulong> _connectedButtons = new();
    private readonly HashSet<string> _suppressedButtonSignals = new(StringComparer.Ordinal);
    private bool _screenshotCaptureRunning;

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

    public void Attach(Control root)
    {
        ThrowIfDisposed();
        _attachedRoot = root ?? throw new ArgumentNullException(nameof(root));
    }

    public void SyncAttachedTree(string screen)
    {
        ThrowIfDisposed();
        if (_attachedRoot is null)
        {
            throw new InvalidOperationException("Gua Godot runtime is not attached to a root Control.");
        }

        BeginFrame(screen);
        _buttonsById.Clear();
        CollectControl(_attachedRoot, _attachedRoot);
        EndFrame();
        DispatchClickRequests();
        ScheduleScreenshotCapture();
    }

    public bool EnqueueClick(string id)
    {
        ThrowIfDisposed();
        return GuaRuntimeNative.gua_runtime_enqueue_click(_runtime, id) != 0;
    }

    public bool ConsumeClickRequest(string id)
    {
        ThrowIfDisposed();
        return GuaRuntimeNative.gua_runtime_consume_click_request(_runtime, id) != 0;
    }

    public bool EmitClick(string id)
    {
        ThrowIfDisposed();
        return GuaRuntimeNative.gua_runtime_emit_click(_runtime, id) != 0;
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

    private void CollectControl(Control root, Control control)
    {
        var id = GetControlId(root, control);
        var role = GetControlRole(control);
        var label = GetControlLabel(control);
        var enabled = IsControlEnabled(control);
        RegisterReflectedNode(id, role, label, control, enabled);

        if (control is BaseButton button)
        {
            _buttonsById[id] = button;
            ConnectButtonPressed(button, id);
        }

        foreach (var child in control.GetChildren())
        {
            if (child is Control childControl)
            {
                CollectControl(root, childControl);
            }
        }
    }

    private void RegisterReflectedNode(string id, string role, string label, Control control, bool enabled)
    {
        const ulong focused = 1UL << 3, caret = 1UL << 8, selection = 1UL << 9, scroll = 1UL << 10,
            scrollMax = 1UL << 11, rangeValue = 1UL << 12, rangeMin = 1UL << 13, rangeMax = 1UL << 14, selectedIndex = 1UL << 15;
        var mask = focused;
        long caretPosition = 0, selectionStart = 0, selectionEnd = 0, selected = -1;
        double x = 0, y = 0, maxX = 0, maxY = 0, value = 0, min = 0, max = 0;
        if (control is LineEdit line)
        {
            mask |= caret | selection; caretPosition = line.CaretColumn;
            selectionStart = line.HasSelection() ? line.GetSelectionFromColumn() : caretPosition;
            selectionEnd = line.HasSelection() ? line.GetSelectionToColumn() : caretPosition;
        }
        else if (control is TextEdit edit)
        {
            mask |= caret | selection; caretPosition = edit.GetCaretColumn();
            selectionStart = edit.HasSelection() ? edit.GetSelectionFromColumn() : caretPosition;
            selectionEnd = edit.HasSelection() ? edit.GetSelectionToColumn() : caretPosition;
        }
        if (control is ScrollContainer sc)
        {
            mask |= scroll | scrollMax; x = sc.ScrollHorizontal; y = sc.ScrollVertical;
            maxX = sc.GetHScrollBar().MaxValue; maxY = sc.GetVScrollBar().MaxValue;
        }
        if (control is global::Godot.Range rangeControl)
        {
            mask |= rangeValue | rangeMin | rangeMax; value = rangeControl.Value; min = rangeControl.MinValue; max = rangeControl.MaxValue;
        }
        if (control is OptionButton option) { mask |= selectedIndex; selected = option.Selected; }
        else if (control is ItemList list) { mask |= selectedIndex; var items = list.GetSelectedItems(); selected = items.Length == 0 ? -1 : items[0]; }
        else if (control is TabContainer tabs) { mask |= selectedIndex; selected = tabs.CurrentTab; }

        var values = new[] { id, role, label };
        var pointers = values.Select(System.Runtime.InteropServices.Marshal.StringToCoTaskMemUTF8).ToArray();
        try
        {
            var baseNode = new GuaRuntimeNative.NativeNodeV2
            {
                StructSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<GuaRuntimeNative.NativeNodeV2>(), KnownMask = mask,
                Id = pointers[0], Role = pointers[1], Label = pointers[2], Bounds = new GuaBounds(control.GlobalPosition.X, control.GlobalPosition.Y, control.Size.X, control.Size.Y),
                Visible = control.IsVisibleInTree() ? 1 : 0, Enabled = enabled ? 1 : 0, Focused = control.HasFocus() ? 1 : 0,
            };
            var node = new GuaRuntimeNative.NativeNodeV3
            {
                StructSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<GuaRuntimeNative.NativeNodeV3>(), Base = baseNode,
                CaretPosition = caretPosition, SelectionStart = selectionStart, SelectionEnd = selectionEnd,
                ScrollX = x, ScrollY = y, ScrollMaxX = maxX, ScrollMaxY = maxY,
                RangeValue = value, RangeMin = min, RangeMax = max, SelectedIndex = selected,
            };
            if (GuaRuntimeNative.gua_runtime_register_node_v3(_runtime, in node) == 0) throw new InvalidOperationException($"Failed to reflect Gua node: {id}");
        }
        finally { foreach (var pointer in pointers) System.Runtime.InteropServices.Marshal.FreeCoTaskMem(pointer); }
    }

    private void DispatchClickRequests()
    {
        foreach (var (id, button) in _buttonsById.ToArray())
        {
            while (ConsumeClickRequest(id))
            {
                if (button.Disabled || !button.IsVisibleInTree())
                {
                    continue;
                }

                EmitClick(id);
                _suppressedButtonSignals.Add(id);
                button.EmitSignal(BaseButton.SignalName.Pressed);
            }
        }
    }

    private void ScheduleScreenshotCapture()
    {
        if (_screenshotCaptureRunning || _attachedRoot is null) return;
        var request = new GuaRuntimeNative.ScreenshotRequest
        {
            StructSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<GuaRuntimeNative.ScreenshotRequest>()
        };
        if (GuaRuntimeNative.gua_runtime_consume_screenshot_request(_runtime, ref request) == 0) return;
        if (DisplayServer.GetName() == "headless")
        {
            GuaRuntimeNative.gua_runtime_complete_screenshot_request(_runtime, request.RequestId, -1, string.Empty, 0, 0);
            return;
        }
        _screenshotCaptureRunning = true;
        CaptureScreenshotAfterDrawAsync(request.RequestId);
    }

    private async void CaptureScreenshotAfterDrawAsync(ulong requestId)
    {
        try
        {
            if (DisplayServer.GetName() == "headless")
            {
                GuaRuntimeNative.gua_runtime_complete_screenshot_request(_runtime, requestId, -1, string.Empty, 0, 0);
                return;
            }
            await _attachedRoot!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = _attachedRoot.GetViewport().GetTexture().GetImage();
            if (image is null || image.IsEmpty())
            {
                GuaRuntimeNative.gua_runtime_complete_screenshot_request(_runtime, requestId, -2, string.Empty, 0, 0);
                return;
            }
            var png = image.SavePngToBuffer();
            var uri = "data:image/png;base64," + Convert.ToBase64String(png);
            GuaRuntimeNative.gua_runtime_complete_screenshot_request(_runtime, requestId, 1, uri, image.GetWidth(), image.GetHeight());
        }
        finally
        {
            _screenshotCaptureRunning = false;
            ScheduleScreenshotCapture();
        }
    }

    private void ConnectButtonPressed(BaseButton button, string id)
    {
        if (!_connectedButtons.Add(button.GetInstanceId()))
        {
            return;
        }

        button.Pressed += () =>
        {
            if (_suppressedButtonSignals.Remove(id))
            {
                return;
            }

            EmitClick(id);
        };
    }

    private static string GetControlId(Control root, Control control)
    {
        if (control.HasMeta("gua_id"))
        {
            return control.GetMeta("gua_id").AsString();
        }

        if (control == root)
        {
            return "root";
        }

        return root.GetPathTo(control).ToString();
    }

    private static string GetControlRole(Control control)
    {
        return control switch
        {
            CheckBox => "checkbox",
            BaseButton => "button",
            Label => "text",
            LineEdit => "textbox",
            TextEdit => "textbox",
            Slider => "slider",
            _ => "panel",
        };
    }

    private static string GetControlLabel(Control control)
    {
        return control switch
        {
            Button button => button.Text,
            Label label => label.Text,
            LineEdit lineEdit => lineEdit.Text,
            TextEdit textEdit => textEdit.Text,
            _ => control.Name,
        };
    }

    private static bool IsControlEnabled(Control control)
    {
        return control switch
        {
            BaseButton button => !button.Disabled,
            LineEdit lineEdit => lineEdit.Editable,
            TextEdit textEdit => textEdit.Editable,
            _ => false,
        };
    }
}
