using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gua.Core;
using Gua.Testing;

namespace Gua.Testing.Recording;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GuaRecordedAction { click, focus, set_value, set_checked, select, scroll, press_key }

public sealed record GuaRecordingTarget(
    string? Id = null,
    string? Role = null,
    string? Name = null,
    string? Scope = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool CurrentFocus = false);

public sealed record GuaCoordinateFallback(float X, float Y);

public sealed record GuaRecordingStep(
    GuaRecordedAction Action,
    long RelativeMilliseconds,
    ulong PreRevision,
    ulong PostRevision,
    bool Sensitive,
    ulong RequestId = 0,
    ulong EventId = 0,
    GuaRecordingTarget? Target = null,
    GuaCoordinateFallback? CoordinateFallback = null,
    string? WaitCondition = null,
    string? Value = null,
    string? SecretKey = null,
    float DeltaX = 0,
    float DeltaY = 0,
    int ScrollUnit = 0,
    uint Modifiers = 0);

public sealed record GuaRecording(int SchemaVersion, IReadOnlyList<GuaRecordingStep> Steps);

public sealed record GuaDiagnosticsRecordingImport(
    GuaRecording Recording,
    bool UsedLegacySyntheticTiming,
    int PairedRequestCount,
    int UnpairedStepCount);

public static class GuaRecordingFile
{
    public static void Save(string path, GuaRecording recording)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Recording path is required.", nameof(path));
        Validate(recording);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(recording, JsonOptions));
    }

    public static GuaRecording Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Recording path is required.", nameof(path));
        var value = JsonSerializer.Deserialize<GuaRecording>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Recording is empty.");
        Validate(value);
        return value;
    }

    public static GuaRecording FromDiagnostics(string json) => ImportDiagnostics(json).Recording;

    public static GuaDiagnosticsRecordingImport ImportDiagnostics(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Diagnostics JSON is required.", nameof(json));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var rootRevision = UInt64(root, "revision");
        var operations = ReadHistory(root, "operations")
            .Where(value => value.Phase is "" or "enqueued")
            .GroupBy(value => value.RequestId == 0 ? $"sequence:{value.Sequence}" : $"request:{value.RequestId}")
            .Select(group => group.OrderBy(value => value.Sequence).First())
            .ToArray();
        var events = ReadHistory(root, "events").Where(value => value.Phase is "" or "observed").ToArray();
        var eventByRequest = events.Where(value => value.RequestId != 0)
            .GroupBy(value => value.RequestId)
            .ToDictionary(group => group.Key, group => group.OrderBy(value => value.Sequence).Last());
        var source = operations.Concat(events.Where(value => value.RequestId == 0))
            .OrderBy(value => value.ElapsedMilliseconds ?? (long)value.Sequence)
            .ThenBy(value => value.Sequence)
            .ToArray();
        var legacyTiming = source.Any(value => value.ElapsedMilliseconds is null);
        var origin = source.Length == 0 ? 0 : source.Min(value => value.ElapsedMilliseconds ?? (long)value.Sequence);
        var paired = 0;
        var unpaired = 0;
        var steps = new List<GuaRecordingStep>(source.Length);

        foreach (var item in source)
        {
            if (!Enum.TryParse<GuaRecordedAction>(item.Action, ignoreCase: true, out var action)) continue;
            var completion = item.RequestId != 0 && eventByRequest.TryGetValue(item.RequestId, out var observed)
                ? observed
                : null;
            if (completion is null) unpaired++; else paired++;
            var sensitive = item.Sensitive;
            var target = string.IsNullOrWhiteSpace(item.NodeId)
                ? action == GuaRecordedAction.press_key ? new GuaRecordingTarget(CurrentFocus: true) : null
                : new GuaRecordingTarget(Id: item.NodeId);
            if (target is null) continue;
            var relative = (item.ElapsedMilliseconds ?? (long)item.Sequence) - origin;
            var value = sensitive ? null : ActionValue(item, action);
            steps.Add(new GuaRecordingStep(
                action,
                relative,
                item.Revision ?? rootRevision,
                completion?.Revision ?? item.Revision ?? rootRevision,
                sensitive,
                item.RequestId,
                completion?.Sequence ?? 0,
                target,
                WaitCondition: null,
                Value: value,
                SecretKey: sensitive ? item.RequestId == 0 ? $"event-{item.Sequence}" : $"request-{item.RequestId}" : null,
                DeltaX: item.DeltaX,
                DeltaY: item.DeltaY,
                ScrollUnit: item.ScrollUnit,
                Modifiers: item.Modifiers));
        }

        var recording = new GuaRecording(1, steps);
        Validate(recording);
        return new(recording, legacyTiming, paired, unpaired);
    }

    public static void Validate(GuaRecording recording)
    {
        if (recording is null) throw new ArgumentNullException(nameof(recording));
        if (recording.SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported Gua recording schemaVersion: {recording.SchemaVersion}.");
        long previous = -1;
        for (var index = 0; index < recording.Steps.Count; index++)
        {
            var step = recording.Steps[index];
            if (step.RelativeMilliseconds < 0 || step.RelativeMilliseconds < previous)
                throw new InvalidDataException($"Recording step {index} has non-monotonic relativeMilliseconds.");
            previous = step.RelativeMilliseconds;
            if (step.PostRevision < step.PreRevision)
                throw new InvalidDataException($"Recording step {index} has postRevision before preRevision.");
            if ((step.Target is null) == (step.CoordinateFallback is null))
                throw new InvalidDataException($"Recording step {index} requires exactly one semantic target or coordinate fallback.");
            if (step.Target is { } target) ValidateTarget(target, step.Action, index);
            if (step.Sensitive && (string.IsNullOrWhiteSpace(step.SecretKey) || step.Value is not null))
                throw new InvalidDataException($"Sensitive recording step {index} requires secretKey and cannot store value.");
            if (!step.Sensitive && step.SecretKey is not null)
                throw new InvalidDataException($"Non-sensitive recording step {index} cannot contain secretKey.");
            if (step.Sensitive && step.Action != GuaRecordedAction.set_value)
                throw new InvalidDataException($"Only set_value recording steps can be sensitive (step {index}).");
            if (step.Action == GuaRecordedAction.set_value && !step.Sensitive && step.Value is null)
                throw new InvalidDataException($"Recording step {index} requires a value.");
            if (step.Action is GuaRecordedAction.select or GuaRecordedAction.press_key && string.IsNullOrEmpty(step.Value))
                throw new InvalidDataException($"Recording step {index} requires a non-empty value.");
            if (step.Action == GuaRecordedAction.set_checked && !bool.TryParse(step.Value, out _))
                throw new InvalidDataException($"set_checked recording step {index} requires a boolean value.");
            if (step.CoordinateFallback is { } coordinate && (!IsFinite(coordinate.X) || !IsFinite(coordinate.Y)))
                throw new InvalidDataException($"Recording step {index} has a non-finite coordinate fallback.");
            if (!IsFinite(step.DeltaX) || !IsFinite(step.DeltaY))
                throw new InvalidDataException($"Recording step {index} has a non-finite scroll delta.");
            if (step.ScrollUnit is < 0 or > 1)
                throw new InvalidDataException($"Recording step {index} has an invalid scrollUnit.");
            if ((step.Modifiers & ~15U) != 0)
                throw new InvalidDataException($"Recording step {index} has unsupported key modifier bits.");
            if (step.Action != GuaRecordedAction.scroll && (step.DeltaX != 0 || step.DeltaY != 0 || step.ScrollUnit != 0))
                throw new InvalidDataException($"Only scroll recording steps can contain scroll arguments (step {index}).");
            if (step.Action != GuaRecordedAction.press_key && step.Modifiers != 0)
                throw new InvalidDataException($"Only press_key recording steps can contain modifiers (step {index}).");
        }
    }

    internal static void ValidateTarget(GuaRecordingTarget target, GuaRecordedAction action, int index)
    {
        var choices = (string.IsNullOrWhiteSpace(target.Id) ? 0 : 1) +
            (string.IsNullOrWhiteSpace(target.Role) ? 0 : 1) + (target.CurrentFocus ? 1 : 0);
        if (choices != 1)
            throw new InvalidDataException($"Recording step {index} target requires exactly one of id, role, or currentFocus.");
        if ((target.Name is not null || target.Scope is not null) && string.IsNullOrWhiteSpace(target.Role))
            throw new InvalidDataException($"Recording step {index} name/scope requires a role target.");
        if (target.Scope is not null && string.IsNullOrWhiteSpace(target.Scope))
            throw new InvalidDataException($"Recording step {index} scope cannot be empty.");
        if (target.CurrentFocus && action != GuaRecordedAction.press_key)
            throw new InvalidDataException($"Recording step {index} can use currentFocus only with press_key.");
    }

    private static IReadOnlyList<HistoryItem> ReadHistory(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array) return [];
        var result = new List<HistoryItem>();
        foreach (var item in array.EnumerateArray())
        {
            result.Add(new(
                UInt64(item, "sequence"),
                String(item, "phase"),
                UInt64(item, "requestId"),
                String(item, "action"),
                String(item, "nodeId"),
                String(item, "value"),
                Bool(item, "sensitive"),
                OptionalInt64(item, "elapsedMilliseconds"),
                OptionalUInt64(item, "revision"),
                Float(item, "deltaX"),
                Float(item, "deltaY"),
                Bool(item, "boolValue"),
                String(item, "key"),
                UInt32(item, "modifiers"),
                Int32(item, "scrollUnit")));
        }
        return result;
    }

    private static string? ActionValue(HistoryItem item, GuaRecordedAction action) => action switch
    {
        GuaRecordedAction.set_checked => item.BoolValue.ToString().ToLowerInvariant(),
        GuaRecordedAction.press_key => item.Key,
        GuaRecordedAction.set_value or GuaRecordedAction.select => item.Value,
        _ => null,
    };

    private static string String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    private static ulong UInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetUInt64(out var result) ? result : 0;
    private static ulong? OptionalUInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetUInt64(out var result) ? result : null;
    private static long? OptionalInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var result) ? result : null;
    private static uint UInt32(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetUInt32(out var result) ? result : 0;
    private static int Int32(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : 0;
    private static float Float(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetSingle(out var result) ? result : 0;
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private sealed record HistoryItem(ulong Sequence, string Phase, ulong RequestId, string Action, string NodeId,
        string Value, bool Sensitive, long? ElapsedMilliseconds, ulong? Revision, float DeltaX, float DeltaY,
        bool BoolValue, string Key, uint Modifiers, int ScrollUnit);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public static class GuaWaitConditions
{
    public static string Visible(string id) => One("visible", id);
    public static string Hidden(string id) => One("hidden", id);
    public static string Enabled(string id) => One("enabled", id);
    public static string Disabled(string id) => One("disabled", id);
    public static string Focused(string id) => One("focused", id);
    public static string Unfocused(string id) => One("unfocused", id);
    public static string Checked(string id) => One("checked", id);
    public static string Unchecked(string id) => One("unchecked", id);
    public static string Text(string id, string expected) => Two("text", id, expected);
    public static string Value(string id, string expected) => Two("value", id, expected);
    private static string One(string kind, string id) => $"{kind}:{Uri.EscapeDataString(id)}";
    private static string Two(string kind, string id, string value) => $"{kind}:{Uri.EscapeDataString(id)}:{Uri.EscapeDataString(value)}";
}
