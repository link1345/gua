using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gua.Core;
using Gua.Testing;

namespace Gua.Testing.Visual;

[JsonConverter(typeof(JsonStringEnumConverter<GuaRecordedAction>))]
public enum GuaRecordedAction { click, focus, set_value, set_checked, select, scroll, press_key }
public sealed record GuaRecordingTarget(string? Id = null, string? Role = null, string? Name = null, string? Scope = null);
public sealed record GuaCoordinateFallback(float X, float Y);
public sealed record GuaRecordingStep(GuaRecordedAction Action, long RelativeMilliseconds, ulong PreRevision, ulong PostRevision, bool Sensitive, ulong RequestId = 0, ulong EventId = 0, GuaRecordingTarget? Target = null, GuaCoordinateFallback? CoordinateFallback = null, string? WaitCondition = null, string? Value = null, string? SecretKey = null);
public sealed record GuaRecording(int SchemaVersion, IReadOnlyList<GuaRecordingStep> Steps);
public enum GuaReplayTimingMode { PreserveDelays, PreferConditions }
public sealed class GuaReplayOptions { public GuaReplayTimingMode TimingMode { get; init; } = GuaReplayTimingMode.PreferConditions; public bool FailOnCoordinateFallback { get; init; } = true; public Func<string, string?>? SecretResolver { get; init; } public Func<GuaCoordinateFallback, CancellationToken, Task>? CoordinateExecutor { get; init; } }

public static class GuaRecordingFile
{
    public static void Save(string path, GuaRecording recording) { Validate(recording); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllText(path, JsonSerializer.Serialize(recording, JsonOptions)); }
    public static GuaRecording Load(string path) { var value = JsonSerializer.Deserialize<GuaRecording>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidDataException("Recording is empty."); Validate(value); return value; }
    public static GuaRecording FromDiagnostics(string json)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        var revision = root.TryGetProperty("revision", out var rev) ? rev.GetUInt64() : 0;
        var seen = new HashSet<ulong>(); var steps = new List<GuaRecordingStep>(); long relative = 0;
        foreach (var op in root.GetProperty("operations").EnumerateArray())
        {
            var requestId = op.TryGetProperty("requestId", out var request) ? request.GetUInt64() : 0;
            if (requestId != 0 && !seen.Add(requestId)) continue;
            var actionText = String(op, "action"); if (!Enum.TryParse<GuaRecordedAction>(actionText, out var action)) continue;
            var sensitive = Bool(op, "sensitive"); var nodeId = String(op, "nodeId"); var value = sensitive ? null : String(op, "value");
            steps.Add(new(action, relative++, revision, revision, sensitive, requestId, requestId,
                string.IsNullOrEmpty(nodeId) ? null : new GuaRecordingTarget(Id: nodeId), Value: string.IsNullOrEmpty(value) ? null : value,
                SecretKey: sensitive ? $"request-{requestId}" : null));
        }
        foreach (var observed in root.GetProperty("events").EnumerateArray())
        {
            var requestId = observed.TryGetProperty("requestId", out var request) ? request.GetUInt64() : 0;
            if (requestId != 0 && seen.Contains(requestId)) continue;
            var actionText = String(observed, "action"); if (!Enum.TryParse<GuaRecordedAction>(actionText, out var action)) continue;
            var sensitive = Bool(observed, "sensitive"); var nodeId = String(observed, "nodeId"); var value = sensitive ? null : String(observed, "value");
            steps.Add(new(action, relative++, revision, revision, sensitive, requestId, requestId,
                string.IsNullOrEmpty(nodeId) ? null : new GuaRecordingTarget(Id: nodeId), Value: string.IsNullOrEmpty(value) ? null : value,
                SecretKey: sensitive ? $"event-{relative}" : null));
        }
        return new(1, steps);
    }
    public static void Validate(GuaRecording recording)
    {
        if (recording.SchemaVersion != 1) throw new InvalidDataException($"Unsupported Gua recording schemaVersion: {recording.SchemaVersion}.");
        foreach (var step in recording.Steps) { if (step.Target is null && step.CoordinateFallback is null) throw new InvalidDataException("Recording step requires a semantic target or coordinate fallback."); if (step.Sensitive && (string.IsNullOrWhiteSpace(step.SecretKey) || step.Value is not null)) throw new InvalidDataException("Sensitive recording steps require secretKey and cannot store value."); }
    }
    private static string String(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static bool Bool(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}

public static class GuaReplayer
{
    public static async Task ReplayAsync(IGuaContext context, GuaRecording recording, GuaReplayOptions? options = null, CancellationToken cancellationToken = default)
    {
        GuaRecordingFile.Validate(recording); options ??= new(); long previous = 0;
        foreach (var step in recording.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (options.TimingMode == GuaReplayTimingMode.PreferConditions && step.WaitCondition is { } wait) await Wait(context, wait, cancellationToken);
            else if (step.RelativeMilliseconds > previous) await Task.Delay(TimeSpan.FromMilliseconds(step.RelativeMilliseconds - previous), cancellationToken);
            previous = step.RelativeMilliseconds;
            if (step.Target is null) { if (options.FailOnCoordinateFallback) throw new InvalidOperationException("Coordinate fallback is disabled for this replay."); if (options.CoordinateExecutor is null) throw new InvalidOperationException("Coordinate replay requires CoordinateExecutor."); await options.CoordinateExecutor(step.CoordinateFallback!, cancellationToken); continue; }
            var nodeId = Resolve(context, step.Target); var value = step.Sensitive ? options.SecretResolver?.Invoke(step.SecretKey!) : step.Value;
            if (step.Sensitive && value is null) throw new InvalidOperationException($"A secret resolver value is required for key '{step.SecretKey}'.");
            var error = context.EnqueueAction(Request(step, nodeId, value), out _); if (error != GuaActionError.None) throw new InvalidOperationException($"Recorded action '{step.Action}' was rejected for '{nodeId}': {error}.");
        }
    }
    private static string Resolve(IGuaContext c, GuaRecordingTarget t) { if (!string.IsNullOrWhiteSpace(t.Id)) return GuaAssertions.Query(c).ById(t.Id).Get().Id; if (string.IsNullOrWhiteSpace(t.Role)) throw new InvalidDataException("Semantic target requires id or role."); var q = GuaAssertions.Query(c).ByRole(t.Role, t.Name); if (!string.IsNullOrWhiteSpace(t.Scope)) q = q.Within(t.Scope); return q.Get().Id; }
    private static GuaActionRequest Request(GuaRecordingStep s, string id, string? value) => s.Action switch { GuaRecordedAction.click => new(GuaActionType.Click, id), GuaRecordedAction.focus => new(GuaActionType.Focus, id), GuaRecordedAction.set_value => new(GuaActionType.SetValue, id, value, Sensitive: s.Sensitive), GuaRecordedAction.set_checked => new(GuaActionType.SetChecked, id, BoolValue: bool.TryParse(value, out var b) && b), GuaRecordedAction.select => new(GuaActionType.Select, id, value), GuaRecordedAction.scroll => new(GuaActionType.Scroll, id), GuaRecordedAction.press_key => new(GuaActionType.PressKey, id, Key: value), _ => throw new UnreachableException() };
    private static async Task Wait(IGuaContext c, string condition, CancellationToken token) { var p = condition.Split(':', 2); if (p.Length != 2 || p[0] != "visible") throw new InvalidDataException($"Unsupported recording wait condition: {condition}."); await GuaAssertions.WaitForVisibleAsync(c, p[1], cancellationToken: token); }
}
