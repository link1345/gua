using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
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

[DefaultExecutionOrder(-32000)]
public sealed partial class GuaUnityRuntime : MonoBehaviour
{
    private readonly Dictionary<string, Target> targets = new(StringComparer.Ordinal);
    private readonly HashSet<string> ids = new(StringComparer.Ordinal);
    private readonly Dictionary<object, string> clickTargetIds = new();
    private readonly ConditionalWeakTable<object, SensitiveTarget> sensitiveTargets = new();
    private readonly HashSet<string> sensitiveTargetIds = new(StringComparer.Ordinal);
    private readonly Dictionary<Button, UnityEngine.Events.UnityAction> uGuiClickHandlers = new();
    private readonly Dictionary<UnityEngine.UIElements.Button, Action> visualClickHandlers = new();
    private readonly HashSet<object> suppressedClicks = new();
    private GuaRuntime? runtime;
    private object? frameFocusTarget;
    private bool screenshotRunning;
    private static GuaUnityRuntime activeRuntime;
    private readonly Dictionary<ulong, int> webCalls = new();
    private readonly Dictionary<ulong, int> webGameInputCalls = new();
    private GuaGameInputSession? webGameInputSession;
    private const int DefaultWebCallTimeoutMs = 5000;
    private string webOwnerId = string.Empty;
    private bool webInstalled;
    [DllImport("__Internal")] private static extern void GuaUnityWebInstall(string hostName, string ownerId, int timeoutMs);
    [DllImport("__Internal")] private static extern void GuaUnityWebUninstall(string ownerId);
    [DllImport("__Internal")] private static extern void GuaUnityWebResolve(string ownerId, int callId, string json, int failed);

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
            runtime.Clock.CallbackFailed += error => Debug.LogError("Gua clock callback failed: " + error);
            runtime.SetAdapterVersion("unity", GuaVersion.Parse(runtime.GetVersionJson()).RuntimeVersion);
            runtime.EnableVirtualClockAdapter();
            InitializeGameInput();
            runtime.EnableWorldObjectTreeAdapter();
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                webOwnerId = Guid.NewGuid().ToString("N");
                GuaUnityWebInstall(gameObject.name, webOwnerId, DefaultWebCallTimeoutMs);
                webInstalled = true;
                Debug.Log("Gua Unity WebGL same-page bridge installed.");
            }
            else
            {
                var configured = Environment.GetEnvironmentVariable("GUA_BRIDGE_PORT");
                var port = int.TryParse(configured, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 8765;
                if (!runtime.StartInspectorBridge(port)) throw new InvalidOperationException($"Failed to start Gua Inspector bridge on port {port}.");
                Debug.Log($"Gua Unity adapter listening on {runtime.InspectorBridgeUrl}.");
            }
            activeRuntime = this;
        }
        catch (Exception error)
        {
            Debug.LogError("Failed to initialize the Gua Unity adapter: " + error);
            try { runtime?.Dispose(); }
            catch (Exception cleanupError) { Debug.LogError("Failed to dispose the partially initialized Gua Unity runtime: " + cleanupError); }
            finally { runtime = null; }
            try { DisposeGameInput(); }
            catch (Exception cleanupError) { Debug.LogError("Failed to remove partially initialized Gua Unity input devices: " + cleanupError); }
            enabled = false;
        }
    }

    private IEnumerator Start()
    {
        // Scene fixtures and user maps may be created by AfterSceneLoad hooks.
        yield return null;
        InitializeGameInput();
    }

    public static void RunFrame() { if (activeRuntime != null && activeRuntime.enabled) activeRuntime.Tick(); }

    /// <summary>
    /// Gets the clock that game logic must explicitly use to participate in
    /// Gua pause and run-for operations.
    /// </summary>
    public static GuaRuntimeClock Clock => activeRuntime?.runtime?.Clock
        ?? throw new InvalidOperationException("The Gua Unity runtime has not started yet.");

    private void Tick()
    {
        if (runtime == null) return;
        try
        {
            runtime.Clock.AdvanceMilliseconds((double)Time.unscaledDeltaTime * 1000.0);
            targets.Clear();
            ids.Clear();
            clickTargetIds.Clear();
            frameFocusTarget = ResolveFocusTarget();
            runtime.BeginFrame(CurrentScreen());
            CollectUiToolkit();
            CollectUGui();
            PruneClickObservers();
            runtime.EndFrame();
            try { CollectWorldObjects(); }
            catch (Exception error) { Debug.LogError("Gua Unity world publication failed: " + error); }
            DispatchActions();
            PumpGameInput();
            if (Application.platform == RuntimePlatform.WebGLPlayer) { FlushWebActionResults(); FlushWebGameInputResults(); }
            ScheduleScreenshot();
        }
        catch (Exception error) { Debug.LogError("Gua Unity adapter frame failed: " + error); }
    }

    public void HandleWebRequest(string json)
    {
        if (runtime == null) return;
        WebEnvelope envelope;
        try { envelope = JsonUtility.FromJson<WebEnvelope>(json); }
        catch (Exception error) { Debug.LogError("Invalid Gua WebGL request: " + error.Message); return; }
        if (envelope == null || envelope.command == null) return;
        if (envelope.command.type == "get_ui_tree")
        {
            GuaUnityWebResolve(webOwnerId, envelope.callId, runtime.GetUiTreeJson(), 0);
            return;
        }
        if (envelope.command.type == "get_world_object_tree")
        {
            GuaUnityWebResolve(webOwnerId, envelope.callId, runtime.GetPlayerWorldObjectTreeJson(), 0);
            return;
        }
        if (envelope.command.type == "query_world_objects")
        {
            if (!TryWorldSelector(envelope.command, envelope.commandFields, out var selector, out var error))
            {
                ResolveWebError(envelope.callId, "invalid_request", error);
                return;
            }
            GuaUnityWebResolve(webOwnerId, envelope.callId, runtime.QueryPlayerWorldObjectsJson(selector), 0);
            return;
        }
        if (envelope.command.type == "get_game_input_capabilities")
        {
            GuaUnityWebResolve(webOwnerId, envelope.callId, JsonSerializer.Serialize(WebGameInputCapabilities()), 0);
            return;
        }
        if (envelope.command.type == "get_game_input_actions")
        {
            if (gameInputCapabilities == GuaGameInputCapabilities.None) { ResolveWebError(envelope.callId, "engine_unsupported", "Unity game input is not initialized."); return; }
            GuaUnityWebResolve(webOwnerId, envelope.callId, runtime.GetGameInputActionsJson(), 0);
            return;
        }
        if (envelope.command.type == "get_game_input_state")
        {
            if (!EnsureWebGameInputSession()) { ResolveWebError(envelope.callId, "engine_unsupported", "Unity game input is not initialized."); return; }
            GuaUnityWebResolve(webOwnerId, envelope.callId, webGameInputSession!.GetStateJson(), 0);
            return;
        }
        if (envelope.command.type == "perform_game_input")
        {
            var gameInput = default(ParsedGameInput);
            var gameInputError = "Invalid game input request.";
            if (!EnsureWebGameInputSession() || !TryParseWebGameInput(json, out gameInput, out gameInputError))
            {
                ResolveWebError(envelope.callId, gameInputCapabilities == GuaGameInputCapabilities.None ? "engine_unsupported" : "invalid_request",
                    gameInputCapabilities == GuaGameInputCapabilities.None ? "Unity game input is not initialized." : gameInputError);
                return;
            }
            if (gameInput.Kind == GuaGameInputKind.Semantic && gameInput.Operation is GuaGameInputOperation.Press or GuaGameInputOperation.Set &&
                WebGameInputRequiresConfirmation(gameInput.Target) && !gameInput.Confirmed)
            {
                ResolveWebError(envelope.callId, "invalid_request", $"Game input action '{gameInput.Target}' requires confirmed=true.");
                return;
            }
            try
            {
                var gameInputRequestId = webGameInputSession!.Send(gameInput.Kind, gameInput.Operation, gameInput.Target, gameInput.Value,
                    TimeSpan.FromMilliseconds(gameInput.LeaseMs), gameInput.X, gameInput.Y, gameInput.DeviceIndex, gameInput.Sensitive);
                webGameInputCalls[gameInputRequestId] = envelope.callId;
            }
            catch (Exception error) { ResolveWebError(envelope.callId, "invalid_request", error.Message); }
            return;
        }
        if (envelope.command.type != "perform_action" || envelope.command.request == null)
        {
            ResolveWebError(envelope.callId, "engine_unsupported", "Unity WebGL does not support this Gua command.");
            return;
        }
        var source = envelope.command.request;
        if (!TryActionType(source.action, out var action))
        {
            ResolveWebError(envelope.callId, "invalid_request", "Unknown Gua action.");
            return;
        }
        var request = new GuaActionRequest(action, EmptyToNull(source.nodeId), EmptyToNull(source.value), source.deltaX, source.deltaY,
            source.@checked, EmptyToNull(source.key), (uint)Math.Clamp(source.modifiers, 0, 15), source.sensitive, source.scrollUnit);
        var result = runtime.EnqueueAction(request, out var requestId);
        if (result != GuaActionError.None)
        {
            ResolveWebError(envelope.callId, WebErrorCode(result), $"Unity rejected the Gua action ({(int)result}).");
            return;
        }
        webCalls[requestId] = envelope.callId;
    }

    public void HandleWebCancellation(string callIdValue)
    {
        if (!int.TryParse(callIdValue, NumberStyles.None, CultureInfo.InvariantCulture, out var callId)) return;
        foreach (var pair in webCalls.ToArray())
        {
            if (pair.Value != callId) continue;
            var result = runtime?.CancelAction(pair.Key) ?? GuaActionCancelResult.NotFound;
            if (result == GuaActionCancelResult.InFlight) continue;
            if (result == GuaActionCancelResult.NotFound) runtime?.TryPollActionEvent(pair.Key, out _);
            webCalls.Remove(pair.Key);
        }
        if (webGameInputCalls.Values.Contains(callId)) ReleaseWebGameInputSession(callId);
    }

    private void FlushWebActionResults()
    {
        foreach (var pair in webCalls.ToArray())
        {
            if (!runtime!.TryPollActionEvent(pair.Key, out var result)) continue;
            var payload = new WebCompletion
            {
                requestId = result.RequestId, action = (int)result.Action, succeeded = result.Succeeded,
                error = (int)result.Error, nodeId = result.NodeId, value = result.Sensitive ? string.Empty : result.Value,
                sensitive = result.Sensitive, sessionEpoch = result.SessionEpoch ?? 0,
                frameSequence = result.FrameSequence ?? 0, revision = result.Revision ?? 0,
            };
            GuaUnityWebResolve(webOwnerId, pair.Value, JsonUtility.ToJson(payload), 0);
            webCalls.Remove(pair.Key);
        }
    }

    private void FlushWebGameInputResults()
    {
        if (webGameInputSession == null) return;
        foreach (var pair in webGameInputCalls.ToArray())
        {
            var result = webGameInputSession.PollResult(pair.Key);
            if (!result.Completed) continue;
            GuaUnityWebResolve(webOwnerId, pair.Value, JsonSerializer.Serialize(result), 0);
            webGameInputCalls.Remove(pair.Key);
        }
    }

    private bool EnsureWebGameInputSession()
    {
        if (runtime == null || gameInputCapabilities == GuaGameInputCapabilities.None) return false;
        webGameInputSession ??= runtime.CreateGameInputSession();
        return true;
    }

    private void ReleaseWebGameInputSession(int cancelledCallId = 0)
    {
        var affectedCalls = webGameInputCalls.Values.Distinct().ToArray();
        webGameInputCalls.Clear();
        foreach (var callId in affectedCalls)
            if (callId != cancelledCallId)
                ResolveWebError(callId, "aborted", "The page-owned game input session was released because another call was cancelled.");
        try { webGameInputSession?.Dispose(); }
        catch (Exception error) { Debug.LogError("Failed to release Unity WebMCP game input: " + error.Message); }
        webGameInputSession = null;
    }

    private string[] WebGameInputCapabilities()
    {
        var result = new List<string>();
        if ((gameInputCapabilities & GuaGameInputCapabilities.Semantic) != 0) result.Add("semantic_game_input_v1");
        if ((gameInputCapabilities & GuaGameInputCapabilities.Keyboard) != 0) result.Add("raw_keyboard_input_v1");
        if ((gameInputCapabilities & GuaGameInputCapabilities.Pointer) != 0) result.Add("raw_pointer_input_v1");
        if ((gameInputCapabilities & GuaGameInputCapabilities.Gamepad) != 0) result.Add("raw_gamepad_input_v1");
        if ((gameInputCapabilities & GuaGameInputCapabilities.Text) != 0) result.Add("text_input_v1");
        if (gameInputCapabilities != GuaGameInputCapabilities.None) result.Add("game_input_lease_v1");
        return result.ToArray();
    }

    private void ResolveWebError(int callId, string code, string message) =>
        GuaUnityWebResolve(webOwnerId, callId, JsonUtility.ToJson(new WebError { code = code, message = message }), 1);
    private static string WebErrorCode(GuaActionError error) => error switch
    {
        GuaActionError.NodeNotFound => "node_not_found",
        GuaActionError.Hidden => "hidden",
        GuaActionError.Disabled => "disabled",
        GuaActionError.Unsupported => "unsupported_action",
        _ => "invalid_request",
    };
    private static string? EmptyToNull(string value) => string.IsNullOrEmpty(value) ? null : value;
    private static bool TryWorldSelector(WebCommand source, string[]? commandFields, out GuaWorldSelector selector, out string error)
    {
        selector = null!;
        error = string.Empty;
        if (source.directChild is < 0 or > 1 || (source.directChild != 0 && string.IsNullOrEmpty(source.parentId)))
        {
            error = "parentId is required when directChild is true.";
            return false;
        }
        if (source.visibleToPlayer is < 0 or > 2 || source.active is < 0 or > 2)
        {
            error = "World boolean filters are invalid.";
            return false;
        }
        var fields = new HashSet<string>(commandFields ?? Array.Empty<string>(), StringComparer.Ordinal);
        var stateFieldCount = new[] { "stateKey", "stateType", "stateString", "stateNumber", "stateBool" }.Count(fields.Contains);
        GuaWorldStateCriterion? state = null;
        if (stateFieldCount != 0)
        {
            if (!fields.Contains("stateKey") || string.IsNullOrEmpty(source.stateKey) || !fields.Contains("stateType"))
            {
                error = "World state criteria require a non-empty stateKey and stateType.";
                return false;
            }
            object? value;
            switch (source.stateType)
            {
                case 0 when stateFieldCount == 2:
                    value = null;
                    break;
                case 1 when stateFieldCount == 3 && fields.Contains("stateString") && source.stateString != null:
                    value = source.stateString;
                    break;
                case 2 when stateFieldCount == 3 && fields.Contains("stateNumber") && double.IsFinite(source.stateNumber):
                    value = source.stateNumber;
                    break;
                case 3 when stateFieldCount == 3 && fields.Contains("stateBool"):
                    value = source.stateBool;
                    break;
                default:
                    error = "World state criterion is invalid.";
                    return false;
            }
            state = new GuaWorldStateCriterion(source.stateKey, value);
        }
        selector = new GuaWorldSelector(
            Id: EmptyToNull(source.worldId), Kind: EmptyToNull(source.kind), Label: EmptyToNull(source.label), Tag: EmptyToNull(source.tag),
            ParentId: EmptyToNull(source.parentId), DirectChild: source.directChild != 0,
            VisibleToPlayer: WorldFilter(source.visibleToPlayer), Active: WorldFilter(source.active), State: state);
        return true;
    }
    private static bool? WorldFilter(int value) => value == 0 ? null : value == 2;
    private static bool TryActionType(string value, out GuaActionType action)
    {
        action = value switch
        {
            "click" => GuaActionType.Click, "focus" => GuaActionType.Focus, "set_value" => GuaActionType.SetValue,
            "set_checked" => GuaActionType.SetChecked, "select" => GuaActionType.Select, "scroll" => GuaActionType.Scroll,
            "press_key" => GuaActionType.PressKey, _ => 0,
        };
        return action != 0;
    }

    private static bool TryParseWebGameInput(string json, out ParsedGameInput result, out string error)
    {
        result = default;
        error = "Invalid game input request.";
        try
        {
            using var document = JsonDocument.Parse(json);
            var request = document.RootElement.GetProperty("command").GetProperty("request");
            var type = request.GetProperty("type").GetString() ?? string.Empty;
            var leaseMs = request.TryGetProperty("leaseMs", out var lease) ? lease.GetInt32() : 5000;
            var deviceIndex = request.TryGetProperty("gamepadIndex", out var index) ? index.GetInt32() : 0;
            if (leaseMs is < 1 or > 60000 || deviceIndex is < 0 or > 3) return false;
            var sensitive = request.TryGetProperty("sensitive", out var sensitiveValue) && sensitiveValue.ValueKind == JsonValueKind.True;
            var confirmed = request.TryGetProperty("confirmed", out var confirmedValue) && confirmedValue.ValueKind == JsonValueKind.True;
            string Text(string name, string fallback = "") => request.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() ?? fallback : fallback;
            double Number(string name) => request.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.Number ? item.GetDouble() : 0;
            ParsedGameInput Build(GuaGameInputKind kind, GuaGameInputOperation operation, string target, object? value = null) =>
                new(kind, operation, target, value, leaseMs, Number("x"), Number("y"), deviceIndex, sensitive, confirmed);
            result = type switch
            {
                "press_game_input_action" => Build(GuaGameInputKind.Semantic, GuaGameInputOperation.Press, Text("actionId"), true),
                "set_game_input_action" when request.TryGetProperty("value", out var value) => Build(GuaGameInputKind.Semantic, GuaGameInputOperation.Set, Text("actionId"), value.Clone()),
                "release_game_input_action" => Build(GuaGameInputKind.Semantic, GuaGameInputOperation.Release, Text("actionId")),
                "release_all_game_inputs" => Build(GuaGameInputKind.Cleanup, GuaGameInputOperation.ReleaseAll, string.Empty),
                "key_down" => Build(GuaGameInputKind.Keyboard, GuaGameInputOperation.Down, Text("code")),
                "key_up" => Build(GuaGameInputKind.Keyboard, GuaGameInputOperation.Up, Text("code")),
                "press_physical_key" => Build(GuaGameInputKind.Keyboard, GuaGameInputOperation.Press, Text("code")),
                "pointer_move" when Text("mode") == "absolute" => Build(GuaGameInputKind.Pointer, GuaGameInputOperation.MoveAbsolute, "absolute:" + Text("coordinateSpace", "viewport_pixels")),
                "pointer_move" when Text("mode") == "delta" => Build(GuaGameInputKind.Pointer, GuaGameInputOperation.MoveDelta, "delta:"),
                "pointer_button_down" => Build(GuaGameInputKind.Pointer, GuaGameInputOperation.Down, Text("button")),
                "pointer_button_up" => Build(GuaGameInputKind.Pointer, GuaGameInputOperation.Up, Text("button")),
                "pointer_wheel" => new ParsedGameInput(GuaGameInputKind.Pointer, GuaGameInputOperation.Wheel,
                    Text("wheelUnit", "pixels"), null, leaseMs, Number("deltaX"), Number("deltaY"), deviceIndex, sensitive, confirmed),
                "gamepad_button_down" => Build(GuaGameInputKind.Gamepad, GuaGameInputOperation.Down, Text("button")),
                "gamepad_button_up" => Build(GuaGameInputKind.Gamepad, GuaGameInputOperation.Up, Text("button")),
                "set_gamepad_axis" => Build(GuaGameInputKind.Gamepad, GuaGameInputOperation.Set, Text("axis"), Number("value")),
                "reset_gamepad" => Build(GuaGameInputKind.Gamepad, GuaGameInputOperation.Reset, string.Empty),
                "text_input" => Build(GuaGameInputKind.TextInput, GuaGameInputOperation.Set, string.Empty, Text("text")),
                _ => default,
            };
            if (result.Kind == 0) return false;
            error = string.Empty;
            return true;
        }
        catch (Exception parseError) { error = parseError.Message; return false; }
    }

    private bool WebGameInputRequiresConfirmation(string actionId)
    {
        if (runtime == null) return false;
        using var document = JsonDocument.Parse(runtime.GetGameInputActionsJson());
        foreach (var action in document.RootElement.GetProperty("actions").EnumerateArray())
            if (action.GetProperty("id").GetString() == actionId)
                return action.TryGetProperty("requiresConfirmation", out var confirmation) && confirmation.ValueKind == JsonValueKind.True;
        return false;
    }

    private readonly struct ParsedGameInput
    {
        public ParsedGameInput(GuaGameInputKind kind, GuaGameInputOperation operation, string target, object? value,
            int leaseMs, double x, double y, int deviceIndex, bool sensitive, bool confirmed)
        { Kind = kind; Operation = operation; Target = target; Value = value; LeaseMs = leaseMs; X = x; Y = y; DeviceIndex = deviceIndex; Sensitive = sensitive; Confirmed = confirmed; }
        public GuaGameInputKind Kind { get; }
        public GuaGameInputOperation Operation { get; }
        public string Target { get; }
        public object? Value { get; }
        public int LeaseMs { get; }
        public double X { get; }
        public double Y { get; }
        public int DeviceIndex { get; }
        public bool Sensitive { get; }
        public bool Confirmed { get; }
    }

    [Serializable] private sealed class WebEnvelope { public int callId; public WebCommand command; public string[] commandFields; }
    [Serializable] private sealed class WebCommand
    {
        public string type; public WebAction request;
        public string worldId, kind, label, tag, parentId, stateKey, stateString;
        public int directChild, visibleToPlayer, active, stateType;
        public double stateNumber;
        public bool stateBool;
    }
    [Serializable] private sealed class WebAction
    {
        public string action, nodeId, value, key; public float deltaX, deltaY; public bool @checked, sensitive; public int modifiers, scrollUnit;
    }
    [Serializable] private sealed class WebCompletion
    {
        public ulong requestId, sessionEpoch, frameSequence, revision; public int action, error; public bool succeeded, sensitive; public string nodeId, value;
    }
    [Serializable] private sealed class WebError { public string code, message; }

    private void OnDestroy()
    {
        if (activeRuntime == this) activeRuntime = null;
        if (webInstalled)
        {
            GuaUnityWebUninstall(webOwnerId);
            webInstalled = false;
        }
        webCalls.Clear();
        ReleaseWebGameInputSession();
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
        var screen = FindObjectsByType<GuaScreen>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate.isActiveAndEnabled && candidate.gameObject.activeInHierarchy);
        if (screen != null && !string.IsNullOrWhiteSpace(screen.Value)) return screen.Value;
        var scene = SceneManager.GetActiveScene();
        return string.IsNullOrWhiteSpace(scene.path) ? scene.name : scene.path;
    }

    private void CollectWorldObjects()
    {
        if (runtime == null) return;
        runtime.BeginWorldFrame(CurrentScreen());
        var published = false;
        try
        {
            var frameIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in FindObjectsByType<GuaWorldObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(item => item.gameObject.scene.IsValid() && item.gameObject.scene.isLoaded)
                .OrderBy(item => ScenePath(item.gameObject.scene), StringComparer.Ordinal)
                .ThenBy(item => item.gameObject.scene.handle)
                .ThenBy(item => HierarchyPath(item.transform), StringComparer.Ordinal)
                .ThenBy(item => item.Id, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(source.Id))
                {
                    runtime.AddLog(3, $"Unity GuaWorldObject on '{source.name}' requires a stable Id and rejected the world frame.");
                    throw new InvalidOperationException($"GuaWorldObject on '{source.name}' requires a stable Id.");
                }
                if (!frameIds.Add(source.Id)) { runtime.AddLog(3, $"Duplicate Unity GuaWorldObject Id '{source.Id}' rejected the world frame."); throw new InvalidOperationException($"Duplicate world object Id '{source.Id}'."); }
                var parentId = NearestWorldParentId(source.transform.parent);
                var position = source.transform.position;
                runtime.RegisterWorldObject(new GuaWorldObjectDescriptor(source.Id, source.Kind, source.Label, source.Space,
                    new GuaWorldPosition(position.x, position.y, source.Space == GuaWorldSpace.World3D ? position.z : 0),
                    source.VisibleToPlayer, source.Active, parentId, source.Description,
                    source.AgentExposure, source.Tags, source.State,
                    string.IsNullOrEmpty(source.DomainId) ? null : source.DomainId,
                    string.IsNullOrEmpty(source.RelatedUiNodeId) ? null : source.RelatedUiNodeId));
            }
            runtime.EndWorldFrame();
            published = true;
        }
        finally
        {
            if (!published) try { runtime.AbortWorldFrame(); } catch { /* A prior native rejection may already have cleared staging. */ }
        }
    }

    private static string? NearestWorldParentId(Transform? current)
    {
        for (; current != null; current = current.parent)
        {
            var parent = current.GetComponent<GuaWorldObject>();
            if (parent != null && !string.IsNullOrWhiteSpace(parent.Id)) return parent.Id;
        }
        return null;
    }

    private static string HierarchyPath(Transform transform)
    {
        var parts = new Stack<string>();
        for (var current = transform; current != null; current = current.parent) parts.Push(current.GetSiblingIndex().ToString("D6", CultureInfo.InvariantCulture));
        return string.Join("/", parts);
    }

    private static string ScenePath(Scene scene) => string.IsNullOrWhiteSpace(scene.path) ? scene.name : scene.path;

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
        var sensitive = sensitiveTargets.TryGetValue(element, out _) || sensitiveTargetIds.Contains(resolved);
        if (sensitive) label = string.IsNullOrWhiteSpace(element.name) ? resolved : element.name;
        var range = VisualRange(element);
        if (sensitive) range.value = null;
        var visible = hostVisible && element.resolvedStyle.display != DisplayStyle.None && element.resolvedStyle.visibility == Visibility.Visible;
        var enabled = element.enabledInHierarchy;
        var registered = Register(resolved, role, label, VisualBounds(element), visible, enabled, parentId,
            text: !sensitive && (role is "text" or "textbox") ? label : null,
            value: sensitive ? null : VisualValue(element), focused: ReferenceEquals(frameFocusTarget, element),
            checkedValue: element is UnityEngine.UIElements.Toggle toggle ? toggle.value : null,
            selectedValue: null, range: range, target: new Target(element, role));
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
                text: label, value: label, focused: ReferenceEquals(frameFocusTarget, itemElement),
                checkedValue: null, selectedValue: selectedIndices.Contains(index), range: default,
                target: new Target(new ListItemTarget(listView, index), "listitem"));
        }
    }

    private void CollectUGui()
    {
        var visited = new HashSet<Transform>();
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (canvas.transform.parent != null && canvas.transform.parent.GetComponentInParent<Canvas>(true) != null) continue;
            CollectTransform(canvas.transform, null, canvas, visited, null, true);
        }
    }

    private void CollectTransform(Transform transform, string? parentId, Canvas canvas, HashSet<Transform> visited, string? ancestorSelectableLabel, bool parentVisible)
    {
        if (!visited.Add(transform)) return;
        var localCanvas = transform.GetComponent<Canvas>() ?? canvas;
        var id = FitNodeId(ExplicitOrObjectId(transform.gameObject, transform.GetSiblingIndex().ToString(CultureInfo.InvariantCulture)));
        var selectable = transform.GetComponent<Selectable>();
        var text = transform.GetComponent<Text>();
        var role = UGuiRole(selectable, text, transform.GetComponent<ScrollRect>());
        var label = UGuiLabel(transform, selectable, text);
        object actionTarget = selectable ?? (object?)transform.GetComponent<ScrollRect>() ?? transform.gameObject;
        var rect = transform as RectTransform;
        var bounds = rect == null ? default : ScreenBounds(rect, localCanvas);
        var visible = parentVisible && localCanvas.isActiveAndEnabled && transform.gameObject.activeInHierarchy && (selectable == null || selectable.IsActive());
        var enabled = selectable?.IsInteractable() ?? visible;
        bool? checkedValue = selectable is Toggle toggle ? toggle.isOn : null;
        var value = UGuiValue(selectable);
        if (GuaUnityAdapterRegistry.TryDescribe(transform, out var tmpTarget, out var tmpRole, out var tmpLabel, out var tmpValue))
        { actionTarget = tmpTarget; role = tmpRole; label = tmpLabel; value = tmpValue; }
        var selectableContentLabel = label;
        var sensitive = sensitiveTargets.TryGetValue(actionTarget, out _) || sensitiveTargetIds.Contains(id);
        if (sensitive) label = transform.name;
        (double? value, double? min, double? max) range = selectable is UnityEngine.UI.Slider slider
            ? (slider.value, slider.minValue, slider.maxValue) : default;
        if (sensitive) range.value = null;
        var suppressAsSelectableLabel = selectable == null && role == "text" && ancestorSelectableLabel != null &&
            string.Equals(label, ancestorSelectableLabel, StringComparison.Ordinal);
        var registered = !suppressAsSelectableLabel && Register(id, role, label, bounds, visible, enabled, parentId,
            text: !sensitive && (role is "text" or "textbox") ? label : null, value: sensitive ? null : value,
            focused: ReferenceEquals(frameFocusTarget, transform.gameObject),
            checkedValue: checkedValue, selectedValue: null, range: range, target: new Target(actionTarget, role, visible, enabled));
        if (registered && actionTarget is Button button) ObserveClick(button, id);
        var childParentId = suppressAsSelectableLabel ? parentId : id;
        var childSelectableLabel = selectable != null ? selectableContentLabel : ancestorSelectableLabel;
        for (var i = 0; i < transform.childCount; i++)
            CollectTransform(transform.GetChild(i), childParentId, localCanvas, visited, childSelectableLabel, visible);
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
            var resultRequest = ProtectSensitiveResult(request, pair.Key, pair.Value.Value);
            if (!pair.Value.Visible)
            {
                runtime.EmitActionResult(resultRequest, false, GuaActionError.Hidden);
                continue;
            }
            if (!pair.Value.Enabled)
            {
                runtime.EmitActionResult(resultRequest, false, GuaActionError.Disabled);
                continue;
            }
            var suppressObservedClick = request.Action == GuaActionType.Click && clickTargetIds.ContainsKey(pair.Value.Value);
            if (suppressObservedClick) suppressedClicks.Add(pair.Value.Value);
            try
            {
                var success = Apply(pair.Value.Value, request, out var resultValue, out var failure);
                if (success && request.Action == GuaActionType.SetValue && request.Sensitive)
                    MarkSensitive(pair.Key, pair.Value.Value);
                resultRequest = ProtectSensitiveResult(request, pair.Key, pair.Value.Value);
                runtime.EmitActionResult(resultRequest, success, success ? GuaActionError.None : failure, resultValue);
            }
            catch (Exception error)
            {
                if (request.Action == GuaActionType.SetValue && request.Sensitive)
                    MarkSensitive(pair.Key, pair.Value.Value);
                resultRequest = ProtectSensitiveResult(request, pair.Key, pair.Value.Value);
                var detail = resultRequest.Sensitive ? "[redacted]" : error.Message;
                var message = $"Unity action {request.RequestId} ({request.Action}, node='{request.NodeId ?? "<null>"}') failed: {detail}";
                runtime.AddLog(3, message);
                Debug.LogError(message);
                runtime.EmitActionResult(resultRequest, false, GuaActionError.InvalidValue);
            }
            finally
            {
                if (suppressObservedClick) suppressedClicks.Remove(pair.Value.Value);
            }
        }
        while (runtime!.TryConsumeAction(GuaActionType.PressKey, null, out var global))
        {
            var focused = targets.FirstOrDefault(pair => IsFrameFocused(pair.Value.Value));
            var focusedTarget = focused.Value;
            string? value = null;
            var failure = GuaActionError.Unsupported;
            if (focusedTarget != null && !focusedTarget.Visible) failure = GuaActionError.Hidden;
            else if (focusedTarget != null && !focusedTarget.Enabled) failure = GuaActionError.Disabled;
            var ok = focusedTarget != null && focusedTarget.Visible && focusedTarget.Enabled && Apply(focusedTarget.Value, global, out value, out failure);
            var resultRequest = focusedTarget == null ? global : ProtectSensitiveResult(global, focused.Key, focusedTarget.Value);
            runtime.EmitActionResult(resultRequest, ok, ok ? GuaActionError.None : failure, value);
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

    private void MarkSensitive(string id, object target)
    {
        sensitiveTargetIds.Add(id);
        sensitiveTargets.GetValue(target, static _ => new SensitiveTarget());
    }

    private GuaActionRequest ProtectSensitiveResult(GuaActionRequest request, string id, object target) =>
        request.Sensitive || sensitiveTargetIds.Contains(id) || sensitiveTargets.TryGetValue(target, out _)
            ? new GuaActionRequest(request.Action, request.NodeId, request.Value, request.DeltaX, request.DeltaY,
                request.BoolValue, request.Key, request.Modifiers, true, request.ScrollUnit, request.RequestId)
            : request;

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
        if (role is "button" or "checkbox" or "tab") yield return GuaActionType.Click;
        if (role is "button" or "checkbox" or "tab" or "textbox" or "slider" or "combobox" or "list") yield return GuaActionType.Focus;
        if (role is "textbox" or "slider") yield return GuaActionType.SetValue;
        if (role == "checkbox") yield return GuaActionType.SetChecked;
        if (role is "combobox" or "list" or "listitem" or "tablist" or "tab") yield return GuaActionType.Select;
        if (role is "list" or "scrollarea") yield return GuaActionType.Scroll;
        if (role == "textbox") yield return GuaActionType.PressKey;
    }

    private bool Apply(object target, GuaActionRequest request, out string? value, out GuaActionError failure)
    {
        value = null;
        failure = GuaActionError.Unsupported;
        if (request.Action == GuaActionType.Scroll && request.ScrollUnit is not 0 and not 1)
        { failure = GuaActionError.InvalidValue; return false; }
        if (request.Action == GuaActionType.Focus)
        {
            if (target is VisualElement visual) { EventSystem.current?.SetSelectedGameObject(null); visual.Focus(); return true; }
            if (target is Component component && EventSystem.current != null) { ClearUiToolkitFocus(); EventSystem.current.SetSelectedGameObject(component.gameObject); return true; }
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
        if (target is SliderInt visualSliderInt && request.Action == GuaActionType.SetValue)
        {
            if (!int.TryParse(request.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var visualNumber)) { failure = GuaActionError.InvalidValue; return false; }
            visualSliderInt.value = visualNumber; value = visualSliderInt.value.ToString(CultureInfo.InvariantCulture); return true;
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
        if (target is Tab tab && request.Action is GuaActionType.Click or GuaActionType.Select)
        { using var click = ClickEvent.GetPooled(); tab.SendEvent(click); value = tab.label; return true; }
        if (target is ListView scrollingList && request.Action == GuaActionType.Scroll)
        {
            var listScrollView = scrollingList.Q<ScrollView>();
            if (listScrollView == null) return false;
            var multiplier = request.ScrollUnit == 1 ? PositiveOrOne(scrollingList.fixedItemHeight) : 1f;
            listScrollView.scrollOffset += new Vector2(request.DeltaX, request.DeltaY) * multiplier; return true;
        }
        if (target is ScrollView scrollView && request.Action == GuaActionType.Scroll)
        {
            var multiplier = request.ScrollUnit == 1 ? PositiveOrOne(scrollView.mouseWheelScrollSize) : 1f;
            scrollView.scrollOffset += new Vector2(request.DeltaX, request.DeltaY) * multiplier; return true;
        }
        if (target is ScrollRect scrollRect && request.Action == GuaActionType.Scroll)
        { return ApplyScrollRect(scrollRect, request); }
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

    private static bool ApplyScrollRect(ScrollRect scrollRect, GuaActionRequest request)
    {
        var content = scrollRect.content;
        var viewport = scrollRect.viewport ?? scrollRect.transform as RectTransform;
        if (content == null || viewport == null) return false;
        var canvasScale = PositiveOrOne(scrollRect.GetComponentInParent<Canvas>()?.scaleFactor ?? 1f);
        var lineExtent = request.ScrollUnit == 1 ? PositiveOrOne(scrollRect.scrollSensitivity) : 1f / canvasScale;
        var overflowX = Math.Max(0f, content.rect.width - viewport.rect.width);
        var overflowY = Math.Max(0f, content.rect.height - viewport.rect.height);
        var normalizedX = overflowX > 0f ? request.DeltaX * lineExtent / overflowX : 0f;
        var normalizedY = overflowY > 0f ? request.DeltaY * lineExtent / overflowY : 0f;
        scrollRect.normalizedPosition += new Vector2(normalizedX, -normalizedY);
        return true;
    }

    private static float PositiveOrOne(float value) =>
        float.IsNaN(value) || float.IsInfinity(value) || value <= 0f ? 1f : value;

    private void ScheduleScreenshot()
    {
        if (screenshotRunning || !runtime!.TryConsumeScreenshotRequest(out var request)) return;
        if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        { runtime.TryCompleteScreenshot(request, GuaScreenshotAvailability.Headless); return; }
        screenshotRunning = true;
        StartCoroutine(Capture(request));
    }

    private IEnumerator Capture(GuaScreenshotRequest request)
    {
        yield return new WaitForEndOfFrame();
        try
        {
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            if (texture == null) runtime!.TryCompleteScreenshot(request, GuaScreenshotAvailability.RenderingDisabled);
            else
            {
                var png = texture.EncodeToPNG();
                runtime!.TryCompleteScreenshot(request, GuaScreenshotAvailability.Available, "data:image/png;base64," + Convert.ToBase64String(png), texture.width, texture.height);
                Destroy(texture);
            }
        }
        catch (Exception error) { runtime!.AddLog(3, "Unity screenshot failed: " + error.Message); runtime.TryCompleteScreenshot(request, GuaScreenshotAvailability.RenderingDisabled); }
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
    private object? ResolveFocusTarget()
    {
        foreach (var document in FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!document.isActiveAndEnabled) continue;
            var focused = document.rootVisualElement?.panel?.focusController?.focusedElement;
            if (focused != null) return focused;
        }
        var selected = EventSystem.current?.currentSelectedGameObject;
        return selected != null && selected.activeInHierarchy ? selected : null;
    }

    private static void ClearUiToolkitFocus()
    {
        foreach (var document in FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            document.rootVisualElement?.panel?.focusController?.focusedElement?.Blur();
    }

    private bool IsFrameFocused(object target) => target switch
    {
        VisualElement visual => ReferenceEquals(frameFocusTarget, visual),
        Component component => ReferenceEquals(frameFocusTarget, component.gameObject),
        _ => false,
    };
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
    private sealed class SensitiveTarget { }
}
}
