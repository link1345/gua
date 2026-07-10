using System.Text;
using System.Text.Json;
using Gua.Core;

namespace Gua.Testing;

public sealed class GuaDiagnosticOptions
{
    public required string TestName { get; init; }
    public string OutputDirectory { get; init; } = Path.Combine("artifacts", "gua");
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;
}

public sealed record GuaDiagnosticCapture(string? ArtifactPath, string? Error)
{
    internal string MessageSuffix => ArtifactPath is not null
        ? $" Gua diagnostics: {ArtifactPath}"
        : Error is not null ? $" Gua diagnostics capture error: {Error}" : string.Empty;
}

public static class GuaDiagnosticWriter
{
    public static GuaDiagnosticCapture Capture(
        IGuaContext context, string failureMessage, GuaDiagnosticOptions options, string? initialUiTreeJson = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();
        string diagnosticsJson;
        try
        {
            diagnosticsJson = context.GetDiagnosticsJson();
        }
        catch (Exception error)
        {
            return new GuaDiagnosticCapture(null, $"{error.GetType().Name}: {error.Message}");
        }

        try
        {
            using var diagnostics = JsonDocument.Parse(diagnosticsJson);
            var root = diagnostics.RootElement;
            var directory = CreateUniqueDirectory(options);
            WriteNew(Path.Combine(directory, "failure-summary.txt"), failureMessage + Environment.NewLine);
            var environment = new Dictionary<string, object?>
            {
                ["testName"] = options.TestName,
                ["processId"] = Environment.ProcessId,
                ["framework"] = ".NET",
                ["runtime"] = Environment.Version.ToString(),
                ["capturedAt"] = options.Clock().ToString("O"),
                ["host"] = options.Environment,
                ["runtimeMetadata"] = root.GetProperty("environment").Clone(),
            };
            WriteNew(Path.Combine(directory, "environment.json"), JsonSerializer.Serialize(environment, JsonOptions));
            var finalTree = root.GetProperty("uiTree").GetRawText();
            WriteNew(Path.Combine(directory, "ui-tree.json"), Pretty(finalTree));
            WriteNew(Path.Combine(directory, "pending-requests.json"), Pretty(root.GetProperty("pendingRequests").GetRawText()));
            WriteNew(Path.Combine(directory, "logs.json"), Pretty(root.GetProperty("logs").GetRawText()));
            WriteJsonLines(Path.Combine(directory, "operations.jsonl"), root.GetProperty("operations"));
            WriteJsonLines(Path.Combine(directory, "events.jsonl"), root.GetProperty("events"));

            if (initialUiTreeJson is not null)
            {
                WriteNew(Path.Combine(directory, "ui-tree-before.json"), Pretty(initialUiTreeJson));
                WriteNew(Path.Combine(directory, "ui-tree.diff.json"), BuildNodeDiff(initialUiTreeJson, finalTree));
            }

            if (root.TryGetProperty("screenshot", out var screenshot) && screenshot.ValueKind == JsonValueKind.Object)
            {
                try
                {
                    WriteScreenshot(directory, screenshot);
                }
                catch (Exception error)
                {
                    errors.Add($"screenshot: {error.GetType().Name}: {error.Message}");
                }
            }
            if (errors.Count > 0)
                WriteNew(Path.Combine(directory, "capture-errors.json"), JsonSerializer.Serialize(errors, JsonOptions));
            return new GuaDiagnosticCapture(Path.GetFullPath(directory), errors.Count == 0 ? null : string.Join("; ", errors));
        }
        catch (Exception error)
        {
            return new GuaDiagnosticCapture(null, $"{error.GetType().Name}: {error.Message}");
        }
    }

    private static string CreateUniqueDirectory(GuaDiagnosticOptions options)
    {
        var testName = Sanitize(options.TestName);
        var parent = Path.Combine(options.OutputDirectory, testName);
        Directory.CreateDirectory(parent);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var id = Guid.NewGuid().ToString("N")[..8];
            var name = $"{options.Clock():yyyyMMddTHHmmssfffZ}-{id}";
            var path = Path.Combine(parent, name);
            if (Directory.Exists(path)) continue;
            Directory.CreateDirectory(path);
            return path;
        }
        throw new IOException("Could not allocate a unique Gua diagnostics directory.");
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) || char.IsControl(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "unnamed-test" : sanitized;
    }

    private static void WriteNew(string path, string contents)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(contents);
    }

    private static void WriteJsonLines(string path, JsonElement array)
    {
        var lines = array.ValueKind == JsonValueKind.Array
            ? string.Join(Environment.NewLine, array.EnumerateArray().Select(item => item.GetRawText()))
            : string.Empty;
        if (lines.Length > 0) lines += Environment.NewLine;
        WriteNew(path, lines);
    }

    private static string Pretty(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement, JsonOptions);
    }

    private static string BuildNodeDiff(string beforeJson, string afterJson)
    {
        using var before = JsonDocument.Parse(beforeJson);
        using var after = JsonDocument.Parse(afterJson);
        var oldNodes = NodesById(before.RootElement);
        var newNodes = NodesById(after.RootElement);
        var added = newNodes.Keys.Except(oldNodes.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var removed = oldNodes.Keys.Except(newNodes.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var changed = oldNodes.Keys.Intersect(newNodes.Keys, StringComparer.Ordinal)
            .Where(id => !string.Equals(oldNodes[id], newNodes[id], StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();
        return JsonSerializer.Serialize(new { schemaVersion = 1, added, removed, changed }, JsonOptions);
    }

    private static Dictionary<string, string> NodesById(JsonElement root) =>
        root.GetProperty("nodes").EnumerateArray().ToDictionary(
            node => node.GetProperty("id").GetString() ?? string.Empty,
            node => node.GetRawText(), StringComparer.Ordinal);

    private static void WriteScreenshot(string directory, JsonElement screenshot)
    {
        var dataUri = screenshot.GetProperty("dataUri").GetString() ?? string.Empty;
        const string prefix = "data:image/png;base64,";
        if (dataUri.Length == 0) return;
        if (!dataUri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Only PNG data URIs are written by diagnostics v1.");
        var bytes = Convert.FromBase64String(dataUri[prefix.Length..]);
        var path = Path.Combine(directory, "screenshot.png");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        stream.Write(bytes);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
