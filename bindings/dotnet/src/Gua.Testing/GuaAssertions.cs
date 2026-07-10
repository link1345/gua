using System.Diagnostics;
using System.Text.Json;
using Gua.Core;

namespace Gua.Testing;

public static class GuaAssertions
{
    private static readonly AsyncLocal<GuaAssertionOptions?> CurrentOptions = new();

    public static GuaAssertionOptions Options
    {
        get => CurrentOptions.Value ?? GuaAssertionOptions.Default;
        set => CurrentOptions.Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public static GuaNodeExpectation GetById(IGuaContext context, string id)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Query(context).ById(id).Get();
    }

    public static GuaNodeExpectation GetByRole(IGuaContext context, string role, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        return Query(context).ByRole(role, name).Get();
    }

    public static GuaNodeExpectation GetByText(IGuaContext context, string text)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return Query(context).ByText(text).Get();
    }

    public static GuaLocatorQuery Query(IGuaContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new GuaLocatorQuery(context);
    }

    public static GuaNodeExpectation ExpectNode(IGuaContext context, string id)
    {
        return GetById(context, id);
    }

    public static void WaitFor(GuaNodeExpectation expectation, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(expectation);
        expectation.WaitFor(timeout);
    }

    public static GuaNodeExpectation WaitForId(IGuaContext context, string id, TimeSpan? timeout = null)
    {
        return WaitForQuery(() => GetById(context, id).RequireExistingSnapshot(), $"id '{id}'", timeout);
    }

    public static GuaNodeExpectation WaitForRole(IGuaContext context, string role, string? name = null, TimeSpan? timeout = null)
    {
        var description = name is null ? $"role '{role}'" : $"role '{role}' and label '{name}'";
        return WaitForQuery(() => GetByRole(context, role, name).RequireExistingSnapshot(), description, timeout);
    }

    public static GuaNodeExpectation WaitForText(IGuaContext context, string text, TimeSpan? timeout = null)
    {
        return WaitForQuery(() => GetByText(context, text).RequireExistingSnapshot(), $"text '{text}'", timeout);
    }

    public static void WaitUntil(
        Func<bool> condition,
        string description,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(1));
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(10);
        do
        {
            if (condition())
            {
                return;
            }

            Thread.Sleep(interval);
        }
        while (DateTime.UtcNow < deadline);

        Fail($"Timed out after {FormatTimeout(timeout)} waiting for {description}.");
    }

    internal static void Fail(string message)
    {
        Options.Fail(message);
        throw new GuaAssertionException(message);
    }

    internal static GuaNodeSnapshot? TryGetSnapshot(IGuaContext context, string id)
    {
        using var document = JsonDocument.Parse(context.GetUiTreeJson());
        if (!document.RootElement.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var node in nodes.EnumerateArray())
        {
            var nodeId = node.GetProperty("id").GetString();
            if (!string.Equals(nodeId, id, StringComparison.Ordinal))
            {
                continue;
            }

            var bounds = node.GetProperty("bounds");
            var actions = new List<string>();
            if (node.TryGetProperty("actions", out var actionValues) && actionValues.ValueKind == JsonValueKind.Array)
            {
                actions.AddRange(actionValues.EnumerateArray().Select(action => action.GetString() ?? string.Empty));
            }

            JsonElement state = default;
            var hasState = node.TryGetProperty("state", out state) && state.ValueKind == JsonValueKind.Object;
            bool? OptionalBoolean(string name) => hasState && state.TryGetProperty(name, out var value)
                ? value.GetBoolean()
                : null;
            string? OptionalString(string name) => node.TryGetProperty(name, out var value)
                ? value.GetString()
                : null;
            ulong? OptionalRootUInt64(string name) => document.RootElement.TryGetProperty(name, out var value)
                ? value.GetUInt64()
                : null;

            return new GuaNodeSnapshot(
                Id: id,
                Role: node.GetProperty("role").GetString() ?? string.Empty,
                Label: node.GetProperty("label").GetString() ?? string.Empty,
                Bounds: new GuaBounds(
                    bounds.GetProperty("x").GetSingle(),
                    bounds.GetProperty("y").GetSingle(),
                    bounds.GetProperty("w").GetSingle(),
                    bounds.GetProperty("h").GetSingle()),
                Visible: node.GetProperty("visible").GetBoolean(),
                Enabled: node.GetProperty("enabled").GetBoolean(),
                Actions: actions,
                ParentId: OptionalString("parentId"),
                Text: OptionalString("text"),
                Value: OptionalString("value"),
                Focused: OptionalBoolean("focused"),
                Hovered: OptionalBoolean("hovered"),
                Pressed: OptionalBoolean("pressed"),
                Checked: OptionalBoolean("checked"),
                Selected: OptionalBoolean("selected"),
                SchemaVersion: document.RootElement.TryGetProperty("schemaVersion", out var schemaVersion) ? schemaVersion.GetInt32() : null,
                SessionEpoch: OptionalRootUInt64("sessionEpoch"),
                FrameSequence: OptionalRootUInt64("frameSequence"),
                Revision: OptionalRootUInt64("revision"));
        }

        return null;
    }

    internal static string DescribeTree(IGuaContext context)
    {
        try
        {
            using var document = JsonDocument.Parse(context.GetUiTreeJson());
            if (!document.RootElement.TryGetProperty("nodes", out var nodes) || nodes.GetArrayLength() == 0)
            {
                return "Current Gua tree has no nodes.";
            }

            var descriptions = nodes.EnumerateArray()
                .Take(8)
                .Select(node =>
                {
                    var id = node.GetProperty("id").GetString();
                    var role = node.GetProperty("role").GetString();
                    var label = node.GetProperty("label").GetString();
                    var visible = node.GetProperty("visible").GetBoolean() ? "visible" : "hidden";
                    var enabled = node.GetProperty("enabled").GetBoolean() ? "enabled" : "disabled";
                    return $"{id} ({role}, '{label}', {visible}, {enabled})";
                });

            return "Current Gua nodes: " + string.Join("; ", descriptions) + ".";
        }
        catch (JsonException)
        {
            return "Current Gua tree JSON could not be parsed.";
        }
    }

    private static GuaNodeExpectation WaitForQuery(Func<GuaNodeExpectation> query, string description, TimeSpan? timeout)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(1));
        Exception? lastException = null;
        do
        {
            try
            {
                return query();
            }
            catch (Exception ex) when (ex is InvalidOperationException or GuaAssertionException)
            {
                lastException = ex;
                Thread.Sleep(10);
            }
        }
        while (DateTime.UtcNow < deadline);

        Fail($"Timed out after {FormatTimeout(timeout)} waiting for Gua node by {description}. {lastException?.Message}");
        throw new UnreachableException();
    }

    private static string FormatTimeout(TimeSpan? timeout)
    {
        return (timeout ?? TimeSpan.FromSeconds(1)).ToString("g");
    }
}

public sealed class GuaNodeExpectation
{
    private readonly IGuaContext _context;
    private readonly string _id;
    private readonly string _description;

    internal GuaNodeExpectation(IGuaContext context, string id, string description)
    {
        _context = context;
        _id = id;
        _description = description;
    }

    public string Id => _id;

    public GuaNodeSnapshot Snapshot => GetSnapshotOrFail();

    public GuaNodeExpectation ToExist()
    {
        if (GuaAssertions.TryGetSnapshot(_context, _id) is null)
        {
            GuaAssertions.Fail($"Expected Gua node {_description} to exist. {GuaAssertions.DescribeTree(_context)}");
        }

        return this;
    }

    public GuaNodeExpectation NotToExist()
    {
        if (GuaAssertions.TryGetSnapshot(_context, _id) is not null)
        {
            GuaAssertions.Fail($"Expected Gua node {_description} not to exist. {GuaAssertions.DescribeTree(_context)}");
        }

        return this;
    }

    public GuaNodeExpectation ToBeVisible()
    {
        var snapshot = GetSnapshotOrFail();
        if (!snapshot.Visible)
        {
            GuaAssertions.Fail($"Expected Gua node {_description} to be visible, but it was hidden. {GuaAssertions.DescribeTree(_context)}");
        }

        return this;
    }

    public GuaNodeExpectation ToBeHidden()
    {
        var snapshot = GetSnapshotOrFail();
        if (snapshot.Visible)
        {
            GuaAssertions.Fail($"Expected Gua node {_description} to be hidden, but it was visible. {GuaAssertions.DescribeTree(_context)}");
        }

        return this;
    }

    public GuaNodeExpectation ToBeEnabled()
    {
        var snapshot = GetSnapshotOrFail();
        if (!snapshot.Enabled)
        {
            GuaAssertions.Fail($"Expected Gua node {_description} to be enabled, but it was disabled. {GuaAssertions.DescribeTree(_context)}");
        }

        return this;
    }

    public GuaNodeExpectation ToBeDisabled()
    {
        var snapshot = GetSnapshotOrFail();
        if (snapshot.Enabled)
        {
            GuaAssertions.Fail($"Expected Gua node {_description} to be disabled, but it was enabled. {GuaAssertions.DescribeTree(_context)}");
        }

        return this;
    }

    public GuaNodeExpectation ToHaveRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        var snapshot = GetSnapshotOrFail();
        if (!string.Equals(snapshot.Role, role, StringComparison.Ordinal))
        {
            GuaAssertions.Fail($"Expected Gua node {_description} to have role '{role}', but it had role '{snapshot.Role}'.");
        }

        return this;
    }

    public GuaNodeExpectation ToHaveLabel(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        var snapshot = GetSnapshotOrFail();
        if (!string.Equals(snapshot.Label, label, StringComparison.Ordinal))
        {
            GuaAssertions.Fail($"Expected Gua node {_description} to have label '{label}', but it had label '{snapshot.Label}'.");
        }

        return this;
    }

    public GuaNodeExpectation ToHaveAction(string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        var snapshot = GetSnapshotOrFail();
        if (!snapshot.Actions.Contains(action, StringComparer.Ordinal))
        {
            GuaAssertions.Fail($"Expected Gua node {_description} to expose action '{action}', but actions were [{string.Join(", ", snapshot.Actions)}].");
        }

        return this;
    }

    public GuaNodeExpectation Click()
    {
        ToBeVisible();
        ToBeEnabled();
        ToHaveAction("click");

        if (!_context.EnqueueClick(_id))
        {
            GuaAssertions.Fail(
                $"Failed to enqueue click for Gua node {_description}. Click() records a request; the game adapter or GuaTestHost must consume it and emit a click event.");
        }

        return this;
    }

    public GuaNodeExpectation ClickAndWaitForEvent(TimeSpan? timeout = null)
    {
        Click();
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(1));
        do
        {
            while (_context.TryPollEvent(out var e))
            {
                if (e.Type == GuaEventType.Click && string.Equals(e.NodeId, _id, StringComparison.Ordinal))
                {
                    return this;
                }
            }

            Thread.Sleep(10);
        }
        while (DateTime.UtcNow < deadline);

        GuaAssertions.Fail($"Timed out waiting for click event from Gua node {_description}. The adapter consumed no click request for '{_id}'.");
        return this;
    }

    public ulong Focus() => Enqueue(new GuaActionRequest(GuaActionType.Focus, _id), "focus");

    public ulong SetValue(string value, bool sensitive = false) =>
        Enqueue(new GuaActionRequest(GuaActionType.SetValue, _id, Value: value, Sensitive: sensitive), "set_value");

    public ulong SetChecked(bool value) =>
        Enqueue(new GuaActionRequest(GuaActionType.SetChecked, _id, BoolValue: value), "set_checked");

    public ulong Select(string value) =>
        Enqueue(new GuaActionRequest(GuaActionType.Select, _id, Value: value), "select");

    public ulong Scroll(float deltaX, float deltaY, int unit = 0) =>
        Enqueue(new GuaActionRequest(GuaActionType.Scroll, _id, DeltaX: deltaX, DeltaY: deltaY, ScrollUnit: unit), "scroll");

    public ulong PressKey(string key, uint modifiers = 0) =>
        Enqueue(new GuaActionRequest(GuaActionType.PressKey, _id, Key: key, Modifiers: modifiers), "press_key");

    public GuaNodeExpectation WaitForAction(ulong requestId, TimeSpan? timeout = null)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)((timeout ?? TimeSpan.FromSeconds(1)).TotalSeconds * Stopwatch.Frequency);
        do
        {
            if (_context.TryPollActionEvent(requestId, out var e))
            {
                if (!e.Succeeded) GuaAssertions.Fail($"Gua action {e.Action} failed for {_description}: {e.Error}.");
                return this;
            }
            Thread.Sleep(10);
        }
        while (Stopwatch.GetTimestamp() < deadline);
        GuaAssertions.Fail($"Timed out waiting for action request {requestId} on Gua node {_description}.");
        return this;
    }

    public GuaNodeExpectation WaitFor(TimeSpan? timeout = null)
    {
        return WaitUntil(node => node is not null, "exist", timeout);
    }

    public GuaNodeExpectation WaitUntilVisible(TimeSpan? timeout = null)
    {
        return WaitUntil(node => node is { Visible: true }, "be visible", timeout);
    }

    public GuaNodeExpectation WaitUntilHidden(TimeSpan? timeout = null)
    {
        return WaitUntil(node => node is { Visible: false } || node is null, "be hidden or removed", timeout);
    }

    public GuaNodeExpectation WaitUntilEnabled(TimeSpan? timeout = null)
    {
        return WaitUntil(node => node is { Enabled: true }, "be enabled", timeout);
    }

    public GuaNodeExpectation WaitUntilDisabled(TimeSpan? timeout = null)
    {
        return WaitUntil(node => node is { Enabled: false }, "be disabled", timeout);
    }

    internal GuaNodeExpectation RequireExistingSnapshot()
    {
        ToExist();
        return this;
    }

    private GuaNodeExpectation WaitUntil(Func<GuaNodeSnapshot?, bool> condition, string description, TimeSpan? timeout)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(1));
        do
        {
            if (condition(GuaAssertions.TryGetSnapshot(_context, _id)))
            {
                return this;
            }

            Thread.Sleep(10);
        }
        while (DateTime.UtcNow < deadline);

        GuaAssertions.Fail($"Timed out waiting for Gua node {_description} to {description}. {GuaAssertions.DescribeTree(_context)}");
        return this;
    }

    private GuaNodeSnapshot GetSnapshotOrFail()
    {
        var snapshot = GuaAssertions.TryGetSnapshot(_context, _id);
        if (snapshot is null)
        {
            GuaAssertions.Fail($"Expected Gua node {_description} to exist. {GuaAssertions.DescribeTree(_context)}");
            throw new UnreachableException();
        }

        return snapshot;
    }

    private ulong Enqueue(GuaActionRequest request, string action)
    {
        ToBeVisible();
        ToBeEnabled();
        ToHaveAction(action);
        var error = _context.EnqueueAction(request, out var requestId);
        if (error != GuaActionError.None) GuaAssertions.Fail($"Failed to enqueue {action} for Gua node {_description}: {error}.");
        return requestId;
    }
}
