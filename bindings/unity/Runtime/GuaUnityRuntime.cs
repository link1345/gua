using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Gua.Core;
using Gua.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Button = UnityEngine.UI.Button;
using Toggle = UnityEngine.UI.Toggle;

namespace Gua.Unity
{

[DefaultExecutionOrder(32000)]
public sealed class GuaUnityRuntime : MonoBehaviour
{
    private readonly Dictionary<string, Target> targets = new(StringComparer.Ordinal);
    private readonly HashSet<string> ids = new(StringComparer.Ordinal);
    private readonly Dictionary<object, string> clickTargetIds = new();
    private readonly Dictionary<Button, UnityEngine.Events.UnityAction> uGuiClickHandlers = new();
    private readonly Dictionary<UnityEngine.UIElements.Button, Action> visualClickHandlers = new();
    private readonly HashSet<object> suppressedClicks = new();
    private GuaRuntime? runtime;
    private bool screenshotRunning;
    private static GuaUnityRuntime activeRuntime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void EnsureStarted()
    {
        if (activeRuntime != null || FindFirstObjectByType<GuaUnityRuntime>() != null) return;
        var host = new GameObject("Gua Runtime") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(host);
        host.AddComponent<GuaUnityRuntime>();
    }

    private void Awake()
    {
        try
        {
            runtime = new GuaRuntime();
            runtime.SetAdapterVersion("unity", GuaVersion.Parse(runtime.GetVersionJson()).RuntimeVersion);
            var configured = Environment.GetEnvironmentVariable("GUA_BRIDGE_PORT");
            var port = int.TryParse(configured, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 8765;
            if (!runtime.StartInspectorBridge(port)) throw new InvalidOperationException($"Failed to start Gua Inspector bridge on port {port}.");
            Debug.Log($"Gua Unity adapter listening on {runtime.InspectorBridgeUrl}.");
            activeRuntime = this;
        }
        catch (Exception error)
        {
            Debug.LogError("Failed to initialize the Gua Unity adapter: " + error);
            enabled = false;
        }
    }

    public static void RunFrame() { if (activeRuntime != null && activeRuntime.enabled) activeRuntime.Tick(); }

    private void Tick()
    {
        if (runtime == null) return;
        try
        {
            targets.Clear();
            ids.Clear();
            clickTargetIds.Clear();
            runtime.BeginFrame(CurrentScreen());
            CollectUiToolkit();
            CollectUGui();
            PruneClickObservers();
            runtime.EndFrame();
            DispatchActions();
            ScheduleScreenshot();
        }
        catch (Exception error) { Debug.LogError("Gua Unity adapter frame failed: " + error); }
    }

    private void OnDestroy()
    {
        if (activeRuntime == this) activeRuntime = null;
        foreach (var pair in uGuiClickHandlers)
            if (pair.Key != null) pair.Key.onClick.RemoveListener(pair.Value);
        foreach (var pair in visualClickHandlers) pair.Key.clicked -= pair.Value;
        uGuiClickHandlers.Clear();
        visualClickHandlers.Clear();
        clickTargetIds.Clear();
        suppressedClicks.Clear();
        runtime?.Dispose();
        runtime = null;
    }

    private string CurrentScreen()
    {
        var screen = FindFirstObjectByType<GuaScreen>(FindObjectsInactive.Include);
        if (screen != null && !string.IsNullOrWhiteSpace(screen.Value)) return screen.Value;
        var scene = SceneManager.GetActiveScene();
        return string.IsNullOrWhiteSpace(scene.path) ? scene.name : scene.path;
    }

    private void CollectUiToolkit()
    {
        foreach (var document in FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var root = document.rootVisualElement;
            if (root == null) continue;
            var rootId = ExplicitOrObjectId(document.gameObject, "uidocument");
            CollectVisualElement(root, rootId, null, 0, document.isActiveAndEnabled && document.gameObject.activeInHierarchy);
        }
    }

    private void CollectVisualElement(VisualElement element, string id, string? parentId, int index, bool hostVisible)
    {
        var explicitId = !string.IsNullOrWhiteSpace(element.viewDataKey) ? element.viewDataKey : element.name;
        var resolved = FitNodeId(string.IsNullOrWhiteSpace(explicitId)
            ? $"{id}/{element.GetType().Name}[{index}]"
            : $"{id}/{EscapeId(explicitId)}");
        var role = VisualRole(element);
        var label = VisualLabel(element);
        var visible = hostVisible && element.resolvedStyle.display != DisplayStyle.None && element.resolvedStyle.visibility == Visibility.Visible;
        var enabled = element.enabledInHierarchy;
        var registered = Register(resolved, role, label, VisualBounds(element), visible, enabled, parentId,
            text: role is "text" or "textbox" ? label : null,
            value: VisualValue(element), focused: element.panel?.focusController?.focusedElement == element,
            checkedValue: element is UnityEngine.UIElements.Toggle toggle ? toggle.value : null,
            selectedValue: null, range: VisualRange(element), target: new Target(element, role));
        if (registered && element is UnityEngine.UIElements.Button button) ObserveClick(button, resolved);
        if (element is ListView listView)
        {
            CollectListViewItems(listView, resolved, visible, enabled);
            return;
        }
        for (var childIndex = 0; childIndex < element.hierarchy.childCount; childIndex++)
            CollectVisualElement(element.hierarchy[childIndex], resolved, resolved, childIndex, visible);
    }

    private void CollectListViewItems(ListView listView, string parentId, bool parentVisible, bool parentEnabled)
    {
        if (listView.itemsSource == null) return;
        var selectedIndices = new HashSet<int>(listView.selectedIndices ?? Enumerable.Empty<int>());
        for (var index = 0; index < listView.itemsSource.Count; index++)
        {
            var itemElement = listView.GetRootElementForIndex(index);
            var label = itemElement?.Q<Label>()?.text ?? listView.itemsSource[index]?.ToString() ?? string.Empty;
            var visible = parentVisible && itemElement != null && itemElement.resolvedStyle.display != DisplayStyle.None && itemElement.resolvedStyle.visibility == Visibility.Visible;
            var bounds = itemElement == null ? default : VisualBounds(itemElement);
            Register(FitNodeId($"{parentId}/item[{index}]"), "listitem", label, bounds, visible, parentEnabled, parentId,
                text: label, value: label, focused: itemElement?.panel?.focusController?.focusedElement == itemElement,
                checkedValue: null, selectedValue: selectedIndices.Contains(index), range: default,
                target: new Target(new ListItemTarget(listView, index), "listitem"));
        }
    }

    private void CollectUGui()
    {
        var visited = new HashSet<Transform>();
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            CollectTransform(canvas.transform, null, canvas, visited, null);
    }

    private void CollectTransform(Transform transform, string? parentId, Canvas canvas, HashSet<Transform> visited, string? ancestorButtonLabel)
    {
        if (!visited.Add(transform)) return;
        var id = FitNodeId(ExplicitOrObjectId(transform.gameObject, transform.GetSiblingIndex().ToString(CultureInfo.InvariantCulture)));
        var selectable = transform.GetComponent<Selectable>();
        var text = transform.GetComponent<Text>();
        var role = UGuiRole(selectable, text, transform.GetComponent<ScrollRect>());
        var label = UGuiLabel(transform, selectable, text);
        object actionTarget = selectable ?? (object?)transform.GetComponent<ScrollRect>() ?? transform.gameObject;
        var rect = transform as RectTransform;
        var bounds = rect == null ? default : ScreenBounds(rect, canvas);
        var visible = transform.gameObject.activeInHierarchy && (selectable == null || selectable.IsActive());
        var enabled = selectable?.IsInteractable() ?? visible;
        bool? checkedValue = selectable is Toggle toggle ? toggle.isOn : null;
        var value = UGuiValue(selectable);
        if (GuaUnityAdapterRegistry.TryDescribe(transform, out var tmpTarget, out var tmpRole, out var tmpLabel, out var tmpValue))
        { actionTarget = tmpTarget; role = tmpRole; label = tmpLabel; value = tmpValue; }
        (double? value, double? min, double? max) range = selectable is UnityEngine.UI.Slider slider
            ? (slider.value, slider.minValue, slider.maxValue) : default;
        var suppressAsButtonLabel = selectable == null && role == "text" && ancestorButtonLabel != null &&
            string.Equals(label, ancestorButtonLabel, StringComparison.Ordinal);
        var registered = !suppressAsButtonLabel && Register(id, role, label, bounds, visible, enabled, parentId,
            text: role is "text" or "textbox" ? label : null, value: value,
            focused: EventSystem.current?.currentSelectedGameObject == transform.gameObject,
            checkedValue: checkedValue, selectedValue: null, range: range, target: new Target(actionTarget, role, visible, enabled));
        if (registered && actionTarget is Button button) ObserveClick(button, id);
        var childParentId = suppressAsButtonLabel ? parentId : id;
        var childButtonLabel = role == "button" ? label : selectable != null ? null : ancestorButtonLabel;
        for (var i = 0; i < transform.childCount; i++)
            CollectTransform(transform.GetChild(i), childParentId, canvas, visited, childButtonLabel);
    }

    private bool Register(string id, string role, string label, GuaBounds bounds, bool visible, bool enabled, string? parentId,
        string? text, string? value, bool? focused, bool? checkedValue, bool? selectedValue,
        (double? value, double? min, double? max) range, Target target)
    {
        if (!ids.Add(id)) { runtime!.AddLog(3, $"Duplicate Unity Gua id ignored: {id}"); return false; }
        runtime!.RegisterNode(new GuaNodeDescriptor(id, role, label, bounds, visible, enabled, parentId, text, value,
            Focused: focused, Checked: checkedValue, Selected: selectedValue,
            RangeValue: range.value, RangeMin: range.min, RangeMax: range.max));
        targets[id] = new Target(target.Value, target.Role, visible, enabled);
        return true;
    }

    private void DispatchActions()
    {
        foreach (var pair in targets.ToArray())
        foreach (var action in SupportedActions(pair.Value.Role))
        while (runtime!.TryConsumeAction(action, pair.Key, out var request))
        {
            if (!pair.Value.Visible)
            {
                runtime.EmitActionResult(request, false, GuaActionError.Hidden);
                continue;
            }
            if (!pair.Value.Enabled)
            {
                runtime.EmitActionResult(request, false, GuaActionError.Disabled);
                continue;
            }
            var suppressObservedClick = request.Action == GuaActionType.Click && clickTargetIds.ContainsKey(pair.Value.Value);
            if (suppressObservedClick) suppressedClicks.Add(pair.Value.Value);
            try
            {
                var success = Apply(pair.Value.Value, request, out var resultValue, out var failure);
                runtime.EmitActionResult(request, success, success ? GuaActionError.None : failure, resultValue);
            }
            catch (Exception error)
            {
                var message = $"Unity action {request.RequestId} ({request.Action}, node='{request.NodeId ?? "<null>"}') failed: {error.Message}";
                runtime.AddLog(3, message);
                Debug.LogError(message);
                runtime.EmitActionResult(request, false, GuaActionError.InvalidValue);
            }
            finally
            {
                if (suppressObservedClick) suppressedClicks.Remove(pair.Value.Value);
            }
        }
        while (runtime!.TryConsumeAction(GuaActionType.PressKey, null, out var global))
        {
            var focused = targets.Values.FirstOrDefault(target => IsFocused(target.Value));
            string? value = null;
            var failure = GuaActionError.Unsupported;
            if (focused != null && !focused.Visible) failure = GuaActionError.Hidden;
            else if (focused != null && !focused.Enabled) failure = GuaActionError.Disabled;
            var ok = focused != null && focused.Visible && focused.Enabled && Apply(focused.Value, global, out value, out failure);
            runtime.EmitActionResult(global, ok, ok ? GuaActionError.None : failure, value);
        }
    }

    private void ObserveClick(object target, string id)
    {
        clickTargetIds[target] = id;
        if (target is Button uGuiButton && !uGuiClickHandlers.ContainsKey(uGuiButton))
        {
            UnityEngine.Events.UnityAction handler = () => EmitObservedClick(uGuiButton);
            uGuiClickHandlers.Add(uGuiButton, handler);
            uGuiButton.onClick.AddListener(handler);
        }
        else if (target is UnityEngine.UIElements.Button visualButton && !visualClickHandlers.ContainsKey(visualButton))
        {
            Action handler = () => EmitObservedClick(visualButton);
            visualClickHandlers.Add(visualButton, handler);
            visualButton.clicked += handler;
        }
    }

    private void EmitObservedClick(object target)
    {
        if (suppressedClicks.Remove(target)) return;
        if (runtime == null || !clickTargetIds.TryGetValue(target, out var id)) return;
        if (!runtime.EmitClick(id)) runtime.AddLog(3, $"Failed to emit observed Unity click: {id}");
    }

    private void PruneClickObservers()
    {
        foreach (var pair in uGuiClickHandlers.Where(pair => !clickTargetIds.ContainsKey(pair.Key)).ToArray())
        {
            if (pair.Key != null) pair.Key.onClick.RemoveListener(pair.Value);
            uGuiClickHandlers.Remove(pair.Key);
            suppressedClicks.Remove(pair.Key);
        }
        foreach (var pair in visualClickHandlers.Where(pair => !clickTargetIds.ContainsKey(pair.Key)).ToArray())
        {
            pair.Key.clicked -= pair.Value;
            visualClickHandlers.Remove(pair.Key);
            suppressedClicks.Remove(pair.Key);
        }
    }

    private static IEnumerable<GuaActionType> SupportedActions(string role)
    {
        if (role is "button" or "checkbox") yield return GuaActionType.Click;
        if (role is "button" or "checkbox" or "textbox" or "slider" or "combobox" or "list") yield return GuaActionType.Focus;
        if (role is "textbox" or "slider") yield return GuaActionType.SetValue;
        if (role == "checkbox") yield return GuaActionType.SetChecked;
        if (role is "combobox" or "list" or "listitem" or "tablist" or "tab") yield return GuaActionType.Select;
        if (role is "list" or "scrollarea") yield return GuaActionType.Scroll;
        if (role == "textbox") yield return GuaActionType.PressKey;
    }

    private static bool Apply(object target, GuaActionRequest request, out string? value, out GuaActionError failure)
    {
        value = null;
        failure = GuaActionError.Unsupported;
        if (request.Action == GuaActionType.Focus)
        {
            if (target is VisualElement visual) { visual.Focus(); return true; }
            if (target is Component component && EventSystem.current != null) { EventSystem.current.SetSelectedGameObject(component.gameObject); return true; }
            return false;
        }
        if (target is UnityEngine.UIElements.Button visualButton && request.Action == GuaActionType.Click)
        {
            using var click = ClickEvent.GetPooled(); visualButton.SendEvent(click); return true;
        }
        if (target is Button button && request.Action == GuaActionType.Click)
        {
            if (EventSystem.current != null) ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
            else button.onClick.Invoke();
            return true;
        }
        if (target is UnityEngine.UIElements.Toggle visualToggle && request.Action is GuaActionType.Click or GuaActionType.SetChecked)
        { visualToggle.value = request.Action == GuaActionType.Click ? !visualToggle.value : request.BoolValue; value = visualToggle.value.ToString(); return true; }
        if (target is Toggle toggle && request.Action is GuaActionType.Click or GuaActionType.SetChecked)
        { toggle.isOn = request.Action == GuaActionType.Click ? !toggle.isOn : request.BoolValue; value = toggle.isOn.ToString(); return true; }
        if (target is TextField textField && request.Action == GuaActionType.SetValue) { textField.value = request.Value ?? ""; value = textField.value; return true; }
        if (target is InputField input && request.Action == GuaActionType.SetValue) { input.text = request.Value ?? ""; value = input.text; return true; }
        if (target is UnityEngine.UIElements.Slider visualSlider && request.Action == GuaActionType.SetValue)
        {
            if (!float.TryParse(request.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var visualNumber)) { failure = GuaActionError.InvalidValue; return false; }
            visualSlider.value = visualNumber; value = visualSlider.value.ToString(CultureInfo.InvariantCulture); return true;
        }
        if (target is UnityEngine.UI.Slider slider && request.Action == GuaActionType.SetValue)
        {
            if (!float.TryParse(request.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) { failure = GuaActionError.InvalidValue; return false; }
            slider.value = number; value = slider.value.ToString(CultureInfo.InvariantCulture); return true;
        }
        if (target is DropdownField dropdown && request.Action == GuaActionType.Select)
        {
            if (!dropdown.choices.Contains(request.Value ?? "")) { failure = GuaActionError.InvalidValue; return false; }
            dropdown.value = request.Value!; value = dropdown.value; return true;
        }
        if (target is Dropdown legacyDropdown && request.Action == GuaActionType.Select)
        { var index = legacyDropdown.options.FindIndex(option => option.text == request.Value); if (index < 0) { failure = GuaActionError.InvalidValue; return false; } legacyDropdown.value = index; value = request.Value; return true; }
        if (target is ListItemTarget listItem && request.Action == GuaActionType.Select)
        {
            if (listItem.List.itemsSource == null || listItem.Index < 0 || listItem.Index >= listItem.List.itemsSource.Count) { failure = GuaActionError.InvalidValue; return false; }
            listItem.List.selectedIndex = listItem.Index; value = listItem.List.itemsSource[listItem.Index]?.ToString(); return true;
        }
        if (target is ListView listView && request.Action == GuaActionType.Select)
        {
            var index = SelectionIndex(request.Value, listView.itemsSource);
            if (index < 0 || listView.itemsSource == null || index >= listView.itemsSource.Count) { failure = GuaActionError.InvalidValue; return false; }
            listView.selectedIndex = index; value = listView.itemsSource[index]?.ToString(); return true;
        }
        if (target is TabView tabView && request.Action == GuaActionType.Select)
        {
            if (!int.TryParse(request.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tabIndex)) { failure = GuaActionError.InvalidValue; return false; }
            tabView.selectedTabIndex = tabIndex; value = tabIndex.ToString(CultureInfo.InvariantCulture); return true;
        }
        if (target is Tab tab && request.Action == GuaActionType.Select)
        { using var click = ClickEvent.GetPooled(); tab.SendEvent(click); value = tab.label; return true; }
        if (target is ListView scrollingList && request.Action == GuaActionType.Scroll)
        {
            var listScrollView = scrollingList.Q<ScrollView>();
            if (listScrollView == null) return false;
            var multiplier = request.ScrollUnit == 1 ? Math.Max(1f, scrollingList.fixedItemHeight) : 1f;
            listScrollView.scrollOffset += new Vector2(request.DeltaX, request.DeltaY) * multiplier; return true;
        }
        if (target is ScrollView scrollView && request.Action == GuaActionType.Scroll)
        { scrollView.scrollOffset += new Vector2(request.DeltaX, request.DeltaY); return true; }
        if (target is ScrollRect scrollRect && request.Action == GuaActionType.Scroll)
        { scrollRect.normalizedPosition += new Vector2(request.DeltaX, -request.DeltaY) * 0.01f; return true; }
        if (target is VisualElement keyTarget && request.Action == GuaActionType.PressKey)
        {
            if (!GuaUnityKeyEvent.TryCreateGesture(request, out var keyDown, out var keyUp)) { failure = GuaActionError.InvalidValue; return false; }
            using var down = KeyDownEvent.GetPooled(keyDown.character, keyDown.keyCode, keyDown.modifiers); keyTarget.SendEvent(down);
            using var up = KeyUpEvent.GetPooled(keyUp.character, keyUp.keyCode, keyUp.modifiers); keyTarget.SendEvent(up); return true;
        }
        if (target is InputField keyInput && request.Action == GuaActionType.PressKey)
        {
            if (!GuaUnityKeyEvent.TryCreateGesture(request, out var keyDown, out var keyUp)) { failure = GuaActionError.InvalidValue; return false; }
            if (!GuaUnityKeyEvent.TryQueueKeyUp(keyUp)) return false;
            keyInput.ProcessEvent(keyDown); value = keyInput.text; return true;
        }
        if (GuaUnityAdapterRegistry.TryApply(target, request, out value)) return true;
        if (request.Action is GuaActionType.SetValue or GuaActionType.Select or GuaActionType.PressKey) failure = GuaActionError.InvalidValue;
        return false;
    }

    private void ScheduleScreenshot()
    {
        if (screenshotRunning || !runtime!.TryConsumeScreenshotRequest(out var request)) return;
        if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        { runtime.CompleteScreenshot(request, GuaScreenshotAvailability.Headless); return; }
        screenshotRunning = true;
        StartCoroutine(Capture(request));
    }

    private IEnumerator Capture(GuaScreenshotRequest request)
    {
        yield return new WaitForEndOfFrame();
        try
        {
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            if (texture == null) runtime!.CompleteScreenshot(request, GuaScreenshotAvailability.RenderingDisabled);
            else
            {
                var png = texture.EncodeToPNG();
                runtime!.CompleteScreenshot(request, GuaScreenshotAvailability.Available, "data:image/png;base64," + Convert.ToBase64String(png), texture.width, texture.height);
                Destroy(texture);
            }
        }
        catch (Exception error) { runtime!.AddLog(3, "Unity screenshot failed: " + error.Message); runtime.CompleteScreenshot(request, GuaScreenshotAvailability.RenderingDisabled); }
        finally { screenshotRunning = false; ScheduleScreenshot(); }
    }

    private static string ExplicitOrObjectId(GameObject gameObject, string suffix)
    {
        var explicitId = gameObject.GetComponent<GuaId>()?.Value;
        if (!string.IsNullOrWhiteSpace(explicitId)) return explicitId;
        var segments = new Stack<string>();
        for (var current = gameObject.transform; current != null; current = current.parent) segments.Push($"{EscapeId(current.name)}[{current.GetSiblingIndex()}]");
        return $"{EscapeId(gameObject.scene.path)}/{string.Join("/", segments)}/{suffix}";
    }

    private static string EscapeId(string value) => (value ?? "").Replace("/", "~1");
    private static string FitNodeId(string id)
    {
        const int maxUtf8Bytes = 127;
        if (Encoding.UTF8.GetByteCount(id) <= maxUtf8Bytes) return id;

        ulong hash = 14695981039346656037UL;
        foreach (var value in Encoding.UTF8.GetBytes(id))
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }

        var suffix = "~" + hash.ToString("x16", CultureInfo.InvariantCulture);
        var prefixLength = id.Length;
        while (prefixLength > 0 && Encoding.UTF8.GetByteCount(id.Substring(0, prefixLength)) + suffix.Length > maxUtf8Bytes)
            prefixLength--;
        return id.Substring(0, prefixLength) + suffix;
    }
    private static GuaBounds VisualBounds(VisualElement element)
    {
        var rect = element.worldBound;
        var scale = Mathf.Max(0f, element.panel?.scaledPixelsPerPoint ?? 1f);
        return SafeBounds(rect.x * scale, rect.y * scale, rect.width * scale, rect.height * scale);
    }
    private static GuaBounds ScreenBounds(RectTransform rect, Canvas canvas) { var corners = new Vector3[4]; rect.GetWorldCorners(corners); var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera; var min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]); var max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]); return SafeBounds(min.x, Screen.height - max.y, max.x - min.x, max.y - min.y); }
    private static GuaBounds SafeBounds(float x, float y, float width, float height) => new GuaBounds(Finite(x), Finite(y), Math.Max(0, Finite(width)), Math.Max(0, Finite(height)));
    private static float Finite(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : value;
    private static int SelectionIndex(string requested, System.Collections.IList items)
    {
        int parsed;
        if (int.TryParse(requested, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) return parsed;
        if (items == null) return -1;
        for (var index = 0; index < items.Count; index++) if (string.Equals(items[index]?.ToString(), requested, StringComparison.Ordinal)) return index;
        return -1;
    }
    private static string VisualRole(VisualElement element) => element switch { UnityEngine.UIElements.Button => "button", UnityEngine.UIElements.Toggle => "checkbox", TextField => "textbox", UnityEngine.UIElements.Slider or SliderInt => "slider", DropdownField => "combobox", ListView => "list", TabView => "tablist", Tab => "tab", ScrollView => "scrollarea", Label => "text", _ => "panel" };
    private static string VisualLabel(VisualElement element) => element switch { UnityEngine.UIElements.Button b => b.text, Label l => l.text, TextField f => f.label, UnityEngine.UIElements.Toggle t => t.label, _ => element.name ?? "" };
    private static string? VisualValue(VisualElement element) => element switch { TextField f => f.value, UnityEngine.UIElements.Toggle t => t.value.ToString(), UnityEngine.UIElements.Slider s => s.value.ToString(CultureInfo.InvariantCulture), SliderInt s => s.value.ToString(CultureInfo.InvariantCulture), DropdownField d => d.value, _ => null };
    private static (double? value, double? min, double? max) VisualRange(VisualElement element) => element switch { UnityEngine.UIElements.Slider s => (s.value, s.lowValue, s.highValue), SliderInt s => (s.value, s.lowValue, s.highValue), _ => default };
    private static string UGuiRole(Selectable? selectable, Text? text, ScrollRect? scroll) => selectable switch { Button => "button", Toggle => "checkbox", InputField => "textbox", UnityEngine.UI.Slider => "slider", Dropdown => "combobox", _ => scroll != null ? "scrollarea" : text != null ? "text" : "panel" };
    private static string UGuiLabel(Transform transform, Selectable? selectable, Text? text) => selectable switch
    {
        InputField input => input.text,
        Dropdown dropdown => dropdown.captionText?.text ?? transform.name,
        _ when text != null => text.text,
        _ when selectable != null => transform.GetComponentInChildren<Text>(true)?.text ?? transform.name,
        _ => transform.name,
    };
    private static string? UGuiValue(Selectable? selectable) => selectable switch { InputField i => i.text, Toggle t => t.isOn.ToString(), UnityEngine.UI.Slider s => s.value.ToString(CultureInfo.InvariantCulture), Dropdown d => d.value >= 0 && d.value < d.options.Count ? d.options[d.value].text : "", _ => null };
    private static bool IsFocused(object target) => target switch { VisualElement v => v.panel?.focusController?.focusedElement == v, Component c => EventSystem.current?.currentSelectedGameObject == c.gameObject, _ => false };
    private sealed class ListItemTarget
    {
        internal ListItemTarget(ListView list, int index) { List = list; Index = index; }
        internal ListView List { get; }
        internal int Index { get; }
    }
    private sealed class Target
    {
        internal Target(object value, string role, bool visible = true, bool enabled = true) { Value = value; Role = role; Visible = visible; Enabled = enabled; }
        internal object Value { get; }
        internal string Role { get; }
        internal bool Visible { get; }
        internal bool Enabled { get; }
    }
}
}
