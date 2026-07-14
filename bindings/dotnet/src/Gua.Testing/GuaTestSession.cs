using Gua.Core;
using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace Gua.Testing;

public enum GuaResetMode
{
    Disabled,
    NonStrict,
    Strict,
}

public sealed record GuaResetPolicy(GuaResetMode Mode, GuaResetTargets Targets = GuaResetTargets.Default)
{
    public static GuaResetPolicy Disabled { get; } = new(GuaResetMode.Disabled);
    public static GuaResetPolicy NonStrict { get; } = new(GuaResetMode.NonStrict);
    public static GuaResetPolicy Strict { get; } = new(GuaResetMode.Strict);

    internal GuaResetOptions ToOptions(ulong epoch) => new(Targets, Mode == GuaResetMode.Strict, epoch);
}

public sealed class GuaTestSessionOptions
{
    public static GuaTestSessionOptions Strict { get; } = new()
    {
        StartupReset = GuaResetPolicy.Strict,
        TeardownReset = GuaResetPolicy.Strict,
        CaptureDiagnosticsBeforeTeardown = true,
    };

    public GuaResetPolicy StartupReset { get; init; } = GuaResetPolicy.Disabled;
    public GuaResetPolicy TeardownReset { get; init; } = GuaResetPolicy.Disabled;
    public bool CaptureDiagnosticsBeforeTeardown { get; init; }
    public bool CleanupAfterLeakReport { get; init; }
    public GuaDiagnosticsSession? DiagnosticsSession { get; init; }
}

public sealed record GuaLeakItem(ulong? RequestId, string Kind, string Action, string NodeId);
public sealed record GuaLeakReport(
    ulong SessionEpoch,
    uint PendingRequestCount,
    uint InFlightRequestCount,
    uint UnconsumedEventCount,
    IReadOnlyList<GuaLeakItem> Items)
{
    public bool IsClean => PendingRequestCount == 0 && InFlightRequestCount == 0 && UnconsumedEventCount == 0;
}

public sealed class GuaTestSession : IDisposable, IAsyncDisposable
{
    private readonly IGuaContext _context;
    private readonly GuaTestSessionOptions _options;
    private bool _disposed;

    public GuaTestSession(IGuaContext context, GuaTestSessionOptions? options = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _options = options ?? new GuaTestSessionOptions();
        ApplyReset(_options.StartupReset, "startup");
    }

    public GuaContextStatus Inspect() => _context.GetContextStatus();

    public void AssertClean()
    {
        var status = Inspect();
        if (status.IsClean) return;
        throw new GuaAssertionException(
            $"Gua test session {status.SessionEpoch} is dirty: pending={status.PendingRequestCount}, " +
            $"inFlight={status.InFlightRequestCount}, events={status.UnconsumedEventCount}, " +
            $"firstRequest={status.FirstPendingAction}:{status.FirstPendingNodeId}, " +
            $"firstEvent={status.FirstEventAction}:{status.FirstEventNodeId}. Payload values are redacted.");
    }

    public GuaResetReport Reset(GuaResetOptions? options = null)
    {
        options ??= new GuaResetOptions();
        var status = Inspect();
        var resolved = options with { ExpectedSessionEpoch = options.ExpectedSessionEpoch ?? status.SessionEpoch };
        var report = _context.Reset(resolved);
        if (report.Result == GuaResetResult.Dirty)
        {
            throw new GuaAssertionException(
                $"Strict Gua reset rejected dirty session {report.SessionEpoch}: " +
                $"pending={report.PendingRequestCount}, inFlight={report.InFlightRequestCount}, " +
                $"events={report.UnconsumedEventCount}, firstRequest={report.FirstPendingAction}:{report.FirstPendingNodeId}, " +
                $"firstEvent={report.FirstEventAction}:{report.FirstEventNodeId}. No state was discarded; payload values are redacted.");
        }
        if (report.Result == GuaResetResult.StaleEpoch)
        {
            throw new InvalidOperationException(
                $"Gua reset rejected stale session epoch {resolved.ExpectedSessionEpoch}; current epoch is {report.SessionEpoch}.");
        }
        return report;
    }

    public Task<GuaResetReport> ResetAsync(GuaResetOptions? options = null, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Reset(options), cancellationToken);
    }

    public GuaLeakReport InspectLeaks()
    {
        var status = Inspect();
        var items = new List<GuaLeakItem>();
        try
        {
            using var document = JsonDocument.Parse(_context.GetDiagnosticsJson());
            var root = document.RootElement;
            AddItems(root, "pendingRequests", "request", items);
            if (status.UnconsumedEventCount > 0)
                AddItems(root, "events", "event", items, (int)Math.Min(status.UnconsumedEventCount, (uint)int.MaxValue));
        }
        catch
        {
            if (status.FirstPendingAction is { } requestAction)
                items.Add(new(null, "request", requestAction.ToString(), status.FirstPendingNodeId));
            if (status.FirstEventAction is { } eventAction)
                items.Add(new(null, "event", eventAction.ToString(), status.FirstEventNodeId));
        }
        return new(status.SessionEpoch, status.PendingRequestCount, status.InFlightRequestCount, status.UnconsumedEventCount, items);
    }

    public void Run(Action body)
    {
        Guard.NotNull(body, nameof(body));
        Exception? primary = null;
        try { body(); }
        catch (Exception error) { primary = error; }

        var teardown = Teardown(primary);
        _disposed = true;
        if (primary is not null)
        {
            if (teardown is not null) primary.Data["GuaTeardownFailure"] = teardown;
            ExceptionDispatchInfo.Capture(primary).Throw();
        }
        if (teardown is not null) throw teardown;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var error = Teardown(null);
        if (error is not null) throw error;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return default;
    }

    private Exception? Teardown(Exception? primary)
    {
        var leak = InspectLeaks();
        var failures = new List<Exception>();
        if ((_options.CaptureDiagnosticsBeforeTeardown && (!leak.IsClean || primary is not null)) && _options.DiagnosticsSession is { } diagnostics)
        {
            var cause = primary ?? new GuaAssertionException(FormatLeak(leak));
            var result = diagnostics.Capture(cause);
            if (primary is not null) primary.Data["GuaDiagnosticsResult"] = result;
            if (result.CaptureErrors.Count > 0)
                failures.Add(new InvalidOperationException(string.Join("; ", result.CaptureErrors.Select(e => $"{e.Stage}: {e.ErrorType}: {e.Message}"))));
        }

        try { ApplyReset(_options.TeardownReset, "teardown"); }
        catch (Exception error)
        {
            failures.Add(error);
            if (_options.CleanupAfterLeakReport && !leak.IsClean)
            {
                try { Reset(new GuaResetOptions(_options.TeardownReset.Targets, Strict: false)); }
                catch (Exception cleanupError) { failures.Add(cleanupError); }
            }
        }
        return failures.Count switch
        {
            0 => null,
            1 => failures[0],
            _ => new AggregateException("Gua teardown reported multiple secondary failures.", failures),
        };
    }

    private void ApplyReset(GuaResetPolicy policy, string stage)
    {
        if (policy.Mode == GuaResetMode.Disabled) return;
        var status = Inspect();
        try { Reset(policy.ToOptions(status.SessionEpoch)); }
        catch (Exception error)
        {
            var leak = InspectLeaks();
            throw new GuaAssertionException($"Gua {stage} reset failed. {FormatLeak(leak)} {error.Message}", error);
        }
    }

    private static string FormatLeak(GuaLeakReport leak)
    {
        var details = string.Join(", ", leak.Items.Select(item =>
            $"{item.Kind}[requestId={item.RequestId?.ToString() ?? "unknown"}, action={item.Action}, nodeId={item.NodeId}]"));
        return $"Gua session {leak.SessionEpoch} leaked pending={leak.PendingRequestCount}, inFlight={leak.InFlightRequestCount}, events={leak.UnconsumedEventCount}" +
            (details.Length == 0 ? string.Empty : $"; {details}") + ". Payload values are redacted.";
    }

    private static void AddItems(JsonElement root, string property, string kind, List<GuaLeakItem> items, int? tailCount = null)
    {
        if (!root.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array) return;
        var selected = array.EnumerateArray().ToArray();
        if (tailCount is { } count && selected.Length > count) selected = selected[^count..];
        foreach (var item in selected)
        {
            ulong? requestId = item.TryGetProperty("requestId", out var id) && id.TryGetUInt64(out var value) ? value : null;
            var action = ReadString(item, "action", "type");
            var nodeId = ReadString(item, "nodeId");
            items.Add(new(requestId, kind, action, nodeId));
        }
    }

    private static string ReadString(JsonElement item, params string[] names)
    {
        foreach (var name in names)
            if (item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String) return value.GetString() ?? string.Empty;
        return string.Empty;
    }
}
