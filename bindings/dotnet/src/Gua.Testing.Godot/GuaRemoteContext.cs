using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Gua.Core;

namespace Gua.Testing.Godot;

public sealed class GuaRemoteContext : IGuaContext, IDisposable
{
    private readonly Uri _bridgeUri;
    private readonly TimeSpan _requestTimeout;
    private ClientWebSocket? _socket;
    private readonly List<GuaActionEvent> _bufferedActionEvents = new();
    private int _nextId = 1;
    private bool _disposed;

    public GuaRemoteContext(string bridgeUrl, TimeSpan requestTimeout)
    {
        _bridgeUri = new Uri(bridgeUrl);
        _requestTimeout = requestTimeout;
    }

    public GuaNodeState GetNodeState(string id)
    {
        var node = GetUiTree().FindNodeById(id)
            ?? throw new InvalidOperationException($"Gua node not found: {id}");
        return new GuaNodeState(node.Visible, node.Enabled);
    }

    public string GetUiTreeJson()
    {
        return RequestRawResult(new { type = "get_ui_tree" });
    }

    public string GetDiagnosticsJson()
    {
        return RequestRawResult(new { type = "get_diagnostics" });
    }

    public GuaVersion GetVersion()
    {
        return GuaVersion.Parse(RequestRawResult(new { type = "get_version" }));
    }

    public Task<GuaVersion> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(GetVersion, cancellationToken);
    }

    public string GetScreenshotJson()
    {
        return RequestRawResult(new { type = "get_screenshot" });
    }

    public GuaScreenshot CaptureScreenshot(TimeSpan timeout, ulong? afterFrameSequence = null)
    {
        try
        {
            return GuaScreenshot.Parse(RequestRawResult(new
            {
                type = "capture_screenshot",
                afterFrameSequence,
                timeoutMs = Math.Max(1, (int)timeout.TotalMilliseconds),
            }, timeout + _requestTimeout));
        }
        catch (InvalidOperationException error) when (error.Message.Contains("headless", StringComparison.Ordinal))
        { throw new GuaScreenshotException(GuaScreenshotError.Headless, "Godot viewport capture is unavailable in headless mode.", error); }
        catch (InvalidOperationException error) when (error.Message.Contains("rendering_disabled", StringComparison.Ordinal))
        { throw new GuaScreenshotException(GuaScreenshotError.RenderingDisabled, "Godot viewport rendering is disabled.", error); }
        catch (InvalidOperationException error) when (error.Message.Contains("unsupported", StringComparison.Ordinal))
        { throw new GuaScreenshotException(GuaScreenshotError.Unsupported, "The connected adapter does not support viewport capture.", error); }
        catch (InvalidOperationException error) when (error.Message.Contains("timed out", StringComparison.Ordinal))
        { throw new GuaScreenshotException(GuaScreenshotError.Timeout, $"Godot viewport capture timed out after {timeout:g}.", error); }
        catch (InvalidOperationException error) when (error.Message.Contains("stale_session", StringComparison.Ordinal))
        { throw new GuaScreenshotException(GuaScreenshotError.StaleSession, "The screenshot request belongs to a stale Gua session epoch.", error); }
        catch (OperationCanceledException error)
        { throw new GuaScreenshotException(GuaScreenshotError.Timeout, $"Godot viewport capture timed out after {timeout:g}.", error); }
    }

    public GuaContextStatus GetContextStatus()
    {
        var status = Request<ContextStatusResult>(new { type = "get_context_status" });
        return new GuaContextStatus(
            status.SessionEpoch, status.FrameSequence, status.Revision, status.NodeCount,
            status.PendingRequestCount, status.InFlightRequestCount, status.UnconsumedEventCount,
            status.LogCount, status.HasScreenshot, ActionOrNull(status.FirstPendingAction),
            status.FirstPendingNodeId, ActionOrNull(status.FirstEventAction), status.FirstEventNodeId);
    }

    public GuaResetReport Reset(GuaResetOptions? options = null)
    {
        options ??= new GuaResetOptions();
        var expectedEpoch = options.ExpectedSessionEpoch ?? GetContextStatus().SessionEpoch;
        var report = Request<ResetReportResult>(new
        {
            type = "reset_context",
            expectedSessionEpoch = expectedEpoch,
            flags = (uint)options.Targets,
            strict = options.Strict,
        });
        return new GuaResetReport(
            (GuaResetResult)report.Result, report.PreviousSessionEpoch, report.SessionEpoch,
            report.PendingRequestCount, report.InFlightRequestCount, report.UnconsumedEventCount,
            report.DiscardedNodeCount, report.DiscardedPendingRequestCount, report.DiscardedInFlightRequestCount,
            report.DiscardedEventCount, report.DiscardedLogCount, report.DiscardedScreenshot,
            ActionOrNull(report.FirstPendingAction), report.FirstPendingNodeId,
            ActionOrNull(report.FirstEventAction), report.FirstEventNodeId);
    }

    private static GuaActionType? ActionOrNull(int action) => action == 0 ? null : (GuaActionType)action;

    public string FindNodeById(string id)
    {
        return GetUiTree().FindNodeById(id)?.Id
            ?? throw new InvalidOperationException($"Gua node not found by id: {id}");
    }

    public string FindNodeByRole(string role, string? name = null)
    {
        var node = GetUiTree().Nodes.FirstOrDefault(node =>
            string.Equals(node.Role, role, StringComparison.Ordinal) &&
            (name is null || string.Equals(node.Label, name, StringComparison.Ordinal)));
        if (node is null)
        {
            throw new InvalidOperationException(name is null
                ? $"Gua node not found by role: {role}"
                : $"Gua node not found by role and name: {role}, {name}");
        }

        return node.Id;
    }

    public string FindNodeByText(string text)
    {
        var node = GetUiTree().Nodes.FirstOrDefault(node => string.Equals(node.Label, text, StringComparison.Ordinal));
        if (node is null)
        {
            throw new InvalidOperationException($"Gua node not found by text: {text}");
        }

        return node.Id;
    }

    public GuaQueryResult Query(GuaSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return Request<GuaQueryResult>(new
        {
            type = "query_nodes",
            selectorId = selector.Id,
            idMatch = (int)selector.IdMatch,
            role = selector.Role,
            roleMatch = (int)selector.RoleMatch,
            name = selector.Name,
            nameMatch = (int)selector.NameMatch,
            text = selector.Text,
            textMatch = (int)selector.TextMatch,
            parentId = selector.ParentId,
            directChild = selector.DirectChild ? 1 : 0,
            visible = (int)selector.Visible,
            enabled = (int)selector.Enabled,
        });
    }

    public bool EnqueueClick(string id)
    {
        Request<object?>(new { type = "click_node", nodeId = id }, allowNullResult: true);
        return true;
    }

    public GuaActionError EnqueueAction(GuaActionRequest request, out ulong requestId)
    {
        var type = request.Action switch
        {
            GuaActionType.Click => "click_node",
            GuaActionType.Focus => "focus_node",
            GuaActionType.SetValue => "set_value",
            GuaActionType.SetChecked => "set_checked",
            GuaActionType.Select => "select",
            GuaActionType.Scroll => "scroll",
            GuaActionType.PressKey => "press_key",
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };
        var command = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["deltaX"] = request.DeltaX,
            ["deltaY"] = request.DeltaY,
            ["checked"] = request.BoolValue,
            ["modifiers"] = request.Modifiers,
            ["sensitive"] = request.Sensitive,
            ["scrollUnit"] = request.ScrollUnit,
        };
        if (request.NodeId is not null) command["nodeId"] = request.NodeId;
        if (request.Value is not null) command["value"] = request.Value;
        if (request.Key is not null) command["key"] = request.Key;
        try
        {
            var result = Request<ActionRequestResult>(command);
            requestId = result.RequestId;
            return GuaActionError.None;
        }
        catch (InvalidOperationException error)
        {
            requestId = 0;
            if (error.Message.Contains("node_not_found", StringComparison.Ordinal)) return GuaActionError.NodeNotFound;
            if (error.Message.Contains("hidden", StringComparison.Ordinal)) return GuaActionError.Hidden;
            if (error.Message.Contains("disabled", StringComparison.Ordinal)) return GuaActionError.Disabled;
            if (error.Message.Contains("unsupported", StringComparison.Ordinal)) return GuaActionError.Unsupported;
            if (error.Message.Contains("invalid_value", StringComparison.Ordinal)) return GuaActionError.InvalidValue;
            return GuaActionError.InvalidArgument;
        }
    }

    public bool TryPollActionEvent(out GuaActionEvent e)
    {
        if (_bufferedActionEvents.Count != 0)
        {
            e = _bufferedActionEvents[0];
            _bufferedActionEvents.RemoveAt(0);
            return true;
        }
        return TryPollActionEventCore(null, out e);
    }

    public bool TryPollActionEvent(ulong requestId, out GuaActionEvent e)
    {
        ArgumentOutOfRangeException.ThrowIfZero(requestId);
        var bufferedIndex = _bufferedActionEvents.FindIndex(candidate => candidate.RequestId == requestId);
        if (bufferedIndex >= 0)
        {
            e = _bufferedActionEvents[bufferedIndex];
            _bufferedActionEvents.RemoveAt(bufferedIndex);
            return true;
        }
        return TryPollActionEventCore(requestId, out e);
    }

    private bool TryPollActionEventCore(ulong? requestId, out GuaActionEvent e)
    {
        var result = Request<ActionEventResult?>(requestId.HasValue
            ? new { type = "poll_events", requestId = requestId.Value }
            : new { type = "poll_events" }, allowNullResult: true);
        if (result is null)
        {
            e = default;
            return false;
        }
        e = new GuaActionEvent(result.RequestId, (GuaActionType)result.Action, result.Succeeded,
            (GuaActionError)result.Error, result.NodeId, result.Value, result.Sensitive,
            result.SessionEpoch, result.FrameSequence, result.Revision);
        return true;
    }

    public bool TryPollEvent(out GuaEvent e)
    {
        while (TryPollActionEventCore(null, out var actionEvent))
        {
            if (actionEvent.Succeeded && actionEvent.Action is GuaActionType.Click or GuaActionType.Focus)
            {
                e = new GuaEvent(actionEvent.Action == GuaActionType.Click ? GuaEventType.Click : GuaEventType.Focus, actionEvent.NodeId);
                return true;
            }
            _bufferedActionEvents.Add(actionEvent);
        }
        e = default;
        return false;
    }

    public void WaitUntilAvailable(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? lastError = null;
        do
        {
            try
            {
                GetUiTree();
                return;
            }
            catch (Exception error)
            {
                lastError = error;
                Thread.Sleep(100);
            }
        }
        while (DateTime.UtcNow < deadline);

        throw new TimeoutException($"Timed out connecting to Gua bridge at {_bridgeUri}.", lastError);
    }

    public string GetCurrentScreen()
    {
        return GetUiTree().Screen;
    }

    public void WaitForScreenTransition(
        IReadOnlySet<string> expectedScreens,
        string? previousScreen,
        bool allowAnyScreenChange,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            var tree = GetUiTree();
            if (expectedScreens.Contains(tree.Screen) ||
                (allowAnyScreenChange &&
                    previousScreen is not null &&
                    !string.Equals(tree.Screen, previousScreen, StringComparison.Ordinal)))
            {
                return;
            }

            Thread.Sleep(50);
        }
        while (DateTime.UtcNow < deadline);

        var expected = string.Join(", ", expectedScreens);
        throw new TimeoutException($"Timed out waiting for Godot scene. Expected screen [{expected}], current screen did not change from '{previousScreen}'.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _socket?.Dispose();
    }

    private GuaRemoteUiTree GetUiTree()
    {
        return Request<GuaRemoteUiTree>(new { type = "get_ui_tree" });
    }

    private T Request<T>(object command, bool allowNullResult = false)
    {
        EnsureConnected();
        var id = _nextId++;
        var payload = JsonSerializer.Serialize(command, command.GetType());
        using var document = JsonDocument.Parse(payload);
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", id);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }

        Send(output.ToArray());
        while (true)
        {
            var responseJson = ReceiveString();
            using var response = JsonDocument.Parse(responseJson);
            if (!response.RootElement.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id)
            {
                continue;
            }

            if (!response.RootElement.GetProperty("ok").GetBoolean())
            {
                throw new InvalidOperationException(response.RootElement.GetProperty("error").GetString());
            }

            var result = response.RootElement.GetProperty("result").Deserialize<T>(JsonOptions);
            if (result is null && !allowNullResult)
            {
                throw new InvalidOperationException("Gua bridge returned an empty result.");
            }

            return result!;
        }
    }

    private string RequestRawResult(object command, TimeSpan? responseTimeout = null)
    {
        EnsureConnected();
        var id = _nextId++;
        var payload = JsonSerializer.Serialize(command, command.GetType());
        using var document = JsonDocument.Parse(payload);
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", id);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }

        Send(output.ToArray());
        while (true)
        {
            var responseJson = ReceiveString(responseTimeout);
            using var response = JsonDocument.Parse(responseJson);
            if (!response.RootElement.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id)
            {
                continue;
            }

            if (!response.RootElement.GetProperty("ok").GetBoolean())
            {
                throw new InvalidOperationException(response.RootElement.GetProperty("error").GetString());
            }

            return response.RootElement.GetProperty("result").GetRawText();
        }
    }

    private void EnsureConnected()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_socket?.State == WebSocketState.Open)
        {
            return;
        }

        using var cts = new CancellationTokenSource(_requestTimeout);
        _socket?.Dispose();
        _socket = new ClientWebSocket();
        try
        {
            _socket.ConnectAsync(_bridgeUri, cts.Token).GetAwaiter().GetResult();
        }
        catch
        {
            _socket.Dispose();
            _socket = null;
            throw;
        }
    }

    private void Send(byte[] payload)
    {
        using var cts = new CancellationTokenSource(_requestTimeout);
        var socket = _socket ?? throw new InvalidOperationException("Gua bridge WebSocket is not connected.");
        socket.SendAsync(payload, WebSocketMessageType.Text, true, cts.Token).GetAwaiter().GetResult();
    }

    private string ReceiveString(TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? _requestTimeout);
        var buffer = new byte[65536];
        using var stream = new MemoryStream();
        while (true)
        {
            var socket = _socket ?? throw new InvalidOperationException("Gua bridge WebSocket is not connected.");
            var result = socket.ReceiveAsync(buffer, cts.Token).GetAwaiter().GetResult();
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException("Gua bridge WebSocket closed.");
            }

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed record ActionRequestResult(ulong RequestId);
    private sealed record ActionEventResult(
        ulong RequestId, int Action, bool Succeeded, int Error, string NodeId, string Value, bool Sensitive,
        ulong SessionEpoch, ulong FrameSequence, ulong Revision);
    private sealed record ContextStatusResult(
        ulong SessionEpoch, ulong FrameSequence, ulong Revision, uint NodeCount,
        uint PendingRequestCount, uint InFlightRequestCount, uint UnconsumedEventCount,
        uint LogCount, bool HasScreenshot, int FirstPendingAction, string FirstPendingNodeId,
        int FirstEventAction, string FirstEventNodeId);
    private sealed record ResetReportResult(
        int Result, ulong PreviousSessionEpoch, ulong SessionEpoch, uint PendingRequestCount,
        uint InFlightRequestCount, uint UnconsumedEventCount, uint DiscardedNodeCount,
        uint DiscardedPendingRequestCount, uint DiscardedInFlightRequestCount, uint DiscardedEventCount,
        uint DiscardedLogCount, bool DiscardedScreenshot, int FirstPendingAction, string FirstPendingNodeId,
        int FirstEventAction, string FirstEventNodeId);
}
