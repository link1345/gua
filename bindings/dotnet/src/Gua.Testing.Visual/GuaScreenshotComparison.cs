using System.Text.Json;
using Gua.Core;
using Gua.Testing;
using StbImageSharp;
using StbImageWriteSharp;

namespace Gua.Testing.Visual;

public readonly record struct GuaMaskRectangle(int X, int Y, int Width, int Height);

public sealed class ScreenshotOptions
{
    public string BaselineDirectory { get; init; } = "baselines";
    public string ArtifactDirectory { get; init; } = Path.Combine("artifacts", "gua");
    public string? FailureDirectory { get; init; }
    public string BaselineVariant { get; init; } = "default";
    public float PixelThreshold { get; init; }
    public double MaxDifferentPixelRatio { get; init; }
    public IReadOnlyList<GuaMaskRectangle> Masks { get; init; } = [];
    public bool UpdateBaselines { get; init; }
    public bool WaitForStableSnapshot { get; init; }
    public int StableFrames { get; init; } = 3;
}

public sealed record ScreenshotComparisonResult(bool Matched, int Width, int Height, long ComparedPixels,
    long DifferentPixels, double DifferentPixelRatio, string BaselinePath, string? ArtifactPath, bool BaselineUpdated);

public static class GuaVisualAssertions
{
    private const string PngPrefix = "data:image/png;base64,";

    public static async Task<ScreenshotComparisonResult> ExpectScreenshotAsync(
        IGuaContext context, string name, ScreenshotOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Screenshot name is required.", nameof(name));
        options ??= new ScreenshotOptions();
        Validate(options);
        if (options.WaitForStableSnapshot)
            await GuaAssertions.WaitForStableSnapshotAsync(context, options.StableFrames, cancellationToken: cancellationToken).ConfigureAwait(false);

        var actualBytes = ReadScreenshot(context.GetDiagnosticsJson());
        var safeName = Sanitize(name);
        var variant = Sanitize(options.BaselineVariant);
        var baseline = Path.GetFullPath(Path.Combine(options.BaselineDirectory, safeName, variant + ".png"));
        var update = options.UpdateBaselines || string.Equals(Environment.GetEnvironmentVariable("GUA_UPDATE_BASELINES"), "1", StringComparison.Ordinal);
        if (!File.Exists(baseline))
        {
            if (update)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(baseline)!);
                File.WriteAllBytes(baseline, actualBytes);
                return new(true, 0, 0, 0, 0, 0, baseline, null, true);
            }
            var missingDir = CreateArtifactDirectory(options, safeName);
            File.WriteAllBytes(Path.Combine(missingDir, "actual.png"), actualBytes);
            WriteComparison(missingDir, new { schemaVersion = 1, matched = false, reason = "baseline_missing", baselinePath = baseline });
            throw new InvalidOperationException($"Screenshot baseline is missing: {baseline}. Actual artifact: {missingDir}");
        }

        var expectedBytes = File.ReadAllBytes(baseline);
        var expected = ImageResult.FromMemory(expectedBytes, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        var actual = ImageResult.FromMemory(actualBytes, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        if (expected.Width != actual.Width || expected.Height != actual.Height)
            return FailDimension(expectedBytes, actualBytes, expected, actual, baseline, options, safeName);

        var diff = new byte[actual.Data.Length];
        long compared = 0, different = 0;
        for (var y = 0; y < actual.Height; y++)
        for (var x = 0; x < actual.Width; x++)
        {
            var offset = (y * actual.Width + x) * 4;
            if (options.Masks.Any(mask => x >= mask.X && y >= mask.Y && x < mask.X + mask.Width && y < mask.Y + mask.Height))
            {
                diff[offset + 3] = 255;
                continue;
            }
            compared++;
            var pixelDifferent = false;
            for (var channel = 0; channel < 4; channel++)
            {
                var delta = Math.Abs(expected.Data[offset + channel] - actual.Data[offset + channel]);
                diff[offset + channel] = (byte)delta;
                pixelDifferent |= delta / 255f > options.PixelThreshold;
            }
            if (pixelDifferent) different++;
            diff[offset + 3] = 255;
        }
        var ratio = compared == 0 ? 0 : (double)different / compared;
        if (ratio <= options.MaxDifferentPixelRatio)
            return new(true, actual.Width, actual.Height, compared, different, ratio, baseline, null, false);
        var directory = CreateArtifactDirectory(options, safeName);
        File.WriteAllBytes(Path.Combine(directory, "expected.png"), expectedBytes);
        File.WriteAllBytes(Path.Combine(directory, "actual.png"), actualBytes);
        WritePng(Path.Combine(directory, "diff.png"), diff, actual.Width, actual.Height);
        WriteComparison(directory, new { schemaVersion = 1, matched = false, actual.Width, actual.Height, comparedPixels = compared, differentPixels = different, differentPixelRatio = ratio, options.PixelThreshold, options.MaxDifferentPixelRatio, masks = options.Masks, baselinePath = baseline });
        throw new InvalidOperationException($"Screenshot comparison failed: ratio {ratio:F6} exceeds {options.MaxDifferentPixelRatio:F6}. Artifacts: {directory}");
    }

    private static ScreenshotComparisonResult FailDimension(byte[] expectedBytes, byte[] actualBytes, ImageResult expected, ImageResult actual, string baseline, ScreenshotOptions options, string name)
    {
        var directory = CreateArtifactDirectory(options, name);
        File.WriteAllBytes(Path.Combine(directory, "expected.png"), expectedBytes);
        File.WriteAllBytes(Path.Combine(directory, "actual.png"), actualBytes);
        WriteComparison(directory, new { schemaVersion = 1, matched = false, reason = "dimension_mismatch", expected = new { expected.Width, expected.Height }, actual = new { actual.Width, actual.Height }, baselinePath = baseline });
        throw new InvalidOperationException($"Screenshot dimensions differ: expected {expected.Width}x{expected.Height}, actual {actual.Width}x{actual.Height}. Artifacts: {directory}");
    }

    private static byte[] ReadScreenshot(string diagnosticsJson)
    {
        using var document = JsonDocument.Parse(diagnosticsJson);
        if (!document.RootElement.TryGetProperty("screenshot", out var screenshot) || screenshot.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Gua context has no screenshot.");
        var uri = screenshot.GetProperty("dataUri").GetString() ?? "";
        if (!uri.StartsWith(PngPrefix, StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Gua visual comparison v1 supports only data:image/png;base64 screenshots.");
        return Convert.FromBase64String(uri[PngPrefix.Length..]);
    }

    private static void Validate(ScreenshotOptions options)
    {
        if (options.PixelThreshold is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(options.PixelThreshold));
        if (options.MaxDifferentPixelRatio is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(options.MaxDifferentPixelRatio));
        if (string.IsNullOrWhiteSpace(options.BaselineVariant)) throw new ArgumentException("BaselineVariant is required.");
        if (options.Masks.Any(x => x.Width < 0 || x.Height < 0)) throw new ArgumentException("Mask dimensions cannot be negative.");
    }

    private static string CreateArtifactDirectory(ScreenshotOptions options, string name)
    {
        var parent = options.FailureDirectory is { Length: > 0 }
            ? Path.GetFullPath(options.FailureDirectory)
            : Path.GetFullPath(Path.Combine(options.ArtifactDirectory, name));
        var path = Path.Combine(parent, $"visual-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path); return path;
    }
    private static void WriteComparison(string directory, object value) => File.WriteAllText(Path.Combine(directory, "comparison.json"), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    private static void WritePng(string path, byte[] data, int width, int height) { using var stream = File.Create(path); new ImageWriter().WritePng(data, width, height, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream); }
    internal static string Sanitize(string value) { var invalid = Path.GetInvalidFileNameChars(); var result = new string(value.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim(); return string.IsNullOrEmpty(result) ? "unnamed" : result; }
}
