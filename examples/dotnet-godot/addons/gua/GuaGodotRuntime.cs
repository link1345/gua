using Godot;
using Gua.Core;
using Gua.Runtime;

namespace Gua.Godot;

public sealed class GuaGodotRuntime : IDisposable
{
    private GuaRuntime? _runtime;
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
        _runtime = new GuaRuntime();
        _runtime.SetGodotPluginVersion("0.1.0");
    }

    public bool InspectorBridgeRunning
    {
        get
        {
            ThrowIfDisposed();
            return _runtime!.InspectorBridgeRunning;
        }
    }

    public string InspectorBridgeUrl
    {
        get
        {
            ThrowIfDisposed();
            return _runtime!.InspectorBridgeUrl;
        }
    }

    public bool StartInspectorBridge(int port = 8765)
    {
        ThrowIfDisposed();
        return _runtime!.StartInspectorBridge(port);
    }

    public void BeginFrame(string screen)
    {
        ThrowIfDisposed();
        _runtime!.BeginFrame(screen);
    }

    public void EndFrame()
    {
        ThrowIfDisposed();
        _runtime!.EndFrame();
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
        _runtime!.RegisterNode(new GuaNodeDescriptor(id, role, label,
            new GuaBounds(bounds.Position.X, bounds.Position.Y, bounds.Size.X, bounds.Size.Y), visible, enabled));
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
        return _runtime!.EnqueueClick(id);
    }

    public bool ConsumeClickRequest(string id)
    {
        ThrowIfDisposed();
        return _runtime!.ConsumeClickRequest(id);
    }

    public bool EmitClick(string id)
    {
        ThrowIfDisposed();
        return _runtime!.EmitClick(id);
    }

    public void ClickByRole(string role, string label)
    {
        ThrowIfDisposed();
        var nodeId = _runtime!.FindNodeByRole(role, label) ?? throw new InvalidOperationException($"Gua node not found by role and name: {role}, {label}");
        if (!EnqueueClick(nodeId)) throw new InvalidOperationException($"Gua node is not clickable: {nodeId}");
    }

    public bool TryPollEvent(out GuaEvent e)
    {
        ThrowIfDisposed();
        return _runtime!.TryPollEvent(out e);
    }

    public void AddLog(GuaLogLevel level, string message)
    {
        ThrowIfDisposed();
        _runtime!.AddLog((int)level, message);
    }

    public string GetUiTreeJson()
    {
        ThrowIfDisposed();
        return _runtime!.GetUiTreeJson();
    }

    public void Dispose()
    {
        _runtime?.Dispose();
        _runtime = null;
    }

    private void ThrowIfDisposed()
    {
        if (_runtime is null)
        {
            throw new ObjectDisposedException(nameof(GuaGodotRuntime));
        }
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
        long? caretPosition = null, selectionStart = null, selectionEnd = null, selected = null;
        double? x = null, y = null, maxX = null, maxY = null, value = null, min = null, max = null;
        if (control is LineEdit line)
        {
            caretPosition = line.CaretColumn;
            selectionStart = line.HasSelection() ? line.GetSelectionFromColumn() : caretPosition;
            selectionEnd = line.HasSelection() ? line.GetSelectionToColumn() : caretPosition;
        }
        else if (control is TextEdit edit)
        {
            caretPosition = edit.GetCaretColumn();
            selectionStart = edit.HasSelection() ? edit.GetSelectionFromColumn() : caretPosition;
            selectionEnd = edit.HasSelection() ? edit.GetSelectionToColumn() : caretPosition;
        }
        if (control is ScrollContainer sc)
        {
            x = sc.ScrollHorizontal; y = sc.ScrollVertical;
            maxX = sc.GetHScrollBar().MaxValue; maxY = sc.GetVScrollBar().MaxValue;
        }
        if (control is global::Godot.Range rangeControl)
        {
            value = rangeControl.Value; min = rangeControl.MinValue; max = rangeControl.MaxValue;
        }
        if (control is OptionButton option) selected = option.Selected;
        else if (control is ItemList list) { var items = list.GetSelectedItems(); selected = items.Length == 0 ? -1 : items[0]; }
        else if (control is TabContainer tabs) selected = tabs.CurrentTab;

        _runtime!.RegisterNode(new GuaNodeDescriptor(id, role, label,
            new GuaBounds(control.GlobalPosition.X, control.GlobalPosition.Y, control.Size.X, control.Size.Y),
            control.IsVisibleInTree(), enabled, Focused: control.HasFocus(), CaretPosition: caretPosition,
            SelectionStart: selectionStart, SelectionEnd: selectionEnd, ScrollX: x, ScrollY: y,
            ScrollMaxX: maxX, ScrollMaxY: maxY, RangeValue: value, RangeMin: min, RangeMax: max,
            SelectedIndex: selected));
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
        if (!_runtime!.TryConsumeScreenshotRequest(out var request)) return;
        if (DisplayServer.GetName() == "headless")
        {
            _runtime.CompleteScreenshot(request, GuaScreenshotAvailability.Headless);
            return;
        }
        _screenshotCaptureRunning = true;
        CaptureScreenshotAfterDrawAsync(request);
    }

    private async void CaptureScreenshotAfterDrawAsync(GuaScreenshotRequest request)
    {
        try
        {
            if (DisplayServer.GetName() == "headless")
            {
                _runtime!.CompleteScreenshot(request, GuaScreenshotAvailability.Headless);
                return;
            }
            await _attachedRoot!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = _attachedRoot.GetViewport().GetTexture().GetImage();
            if (image is null || image.IsEmpty())
            {
                _runtime!.CompleteScreenshot(request, GuaScreenshotAvailability.RenderingDisabled);
                return;
            }
            var png = image.SavePngToBuffer();
            var uri = "data:image/png;base64," + Convert.ToBase64String(png);
            _runtime!.CompleteScreenshot(request, GuaScreenshotAvailability.Available, uri, image.GetWidth(), image.GetHeight());
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
