using System.Text.Json;
using Gua.Core;
using Gua.Testing.Visual;
using NUnit.Framework;
using StbImageWriteSharp;

namespace Gua.Visual.Tests;

public sealed class VisualTests
{
    private string _root = null!;
    [SetUp] public void SetUp() => _root = Path.Combine(Path.GetTempPath(), "gua-visual-tests", Guid.NewGuid().ToString("N"));
    [TearDown] public void TearDown() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Test]
    public async Task BaselineUpdateThenExactMatchUsesExplicitVariant()
    {
        var context = new FakeContext(Png(2, 2, 10)); var options = Options("windows", update: true);
        var updated = await GuaVisualAssertions.ExpectScreenshotAsync(context, "title", options);
        Assert.That(updated.BaselineUpdated, Is.True);
        var matched = await GuaVisualAssertions.ExpectScreenshotAsync(context, "title", Options("windows"));
        Assert.Multiple(() => { Assert.That(matched.Matched, Is.True); Assert.That(matched.BaselinePath, Does.EndWith(Path.Combine("title", "windows.png"))); });
    }

    [Test]
    public async Task ThresholdRatioAndMaskExcludeMaskedPixels()
    {
        var baseline = Png(2, 1, 10); var context = new FakeContext(baseline);
        await GuaVisualAssertions.ExpectScreenshotAsync(context, "masked", Options("default", update: true));
        context.Set(Png(2, 1, 20, second: 200));
        var result = await GuaVisualAssertions.ExpectScreenshotAsync(context, "masked", Options("default", threshold: .05f, ratio: 0, masks: [new(1, 0, 1, 1)]));
        Assert.Multiple(() => { Assert.That(result.Matched, Is.True); Assert.That(result.ComparedPixels, Is.EqualTo(1)); Assert.That(result.DifferentPixels, Is.Zero); });
    }

    [Test]
    public async Task MissingAndDimensionMismatchWriteMachineReadableArtifacts()
    {
        var context = new FakeContext(Png(1, 1, 1));
        var missing = Assert.ThrowsAsync<InvalidOperationException>(() => GuaVisualAssertions.ExpectScreenshotAsync(context, "missing", Options("default")));
        Assert.That(missing!.Message, Does.Contain("baseline is missing"));
        var missingDirectory = Directory.GetDirectories(Path.Combine(_root, "artifacts", "missing")).Single();
        using var missingMetadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(missingDirectory, "comparison.json")));
        Assert.Multiple(() =>
        {
            Assert.That(Directory.GetFiles(missingDirectory).Select(Path.GetFileName), Is.EquivalentTo(new[] { "actual.png", "comparison.json" }));
            Assert.That(missingMetadata.RootElement.GetProperty("name").GetString(), Is.EqualTo("missing"));
            Assert.That(missingMetadata.RootElement.GetProperty("variant").GetString(), Is.EqualTo("default"));
            Assert.That(missingMetadata.RootElement.GetProperty("baselineCreated").GetBoolean(), Is.False);
            Assert.That(missingMetadata.RootElement.GetProperty("reason").GetString(), Is.EqualTo("baseline_missing"));
            Assert.That(missingMetadata.RootElement.GetProperty("width").GetInt32(), Is.EqualTo(1));
            Assert.That(missingMetadata.RootElement.GetProperty("height").GetInt32(), Is.EqualTo(1));
        });
        await GuaVisualAssertions.ExpectScreenshotAsync(context, "size", Options("default", update: true));
        context.Set(Png(2, 1, 1));
        var mismatch = Assert.ThrowsAsync<InvalidOperationException>(() => GuaVisualAssertions.ExpectScreenshotAsync(context, "size", Options("default")));
        Assert.That(mismatch!.Message, Does.Contain("dimensions differ"));
        var mismatchDirectory = Directory.GetDirectories(Path.Combine(_root, "artifacts", "size")).Single();
        using var mismatchMetadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(mismatchDirectory, "comparison.json")));
        Assert.Multiple(() =>
        {
            Assert.That(Directory.GetFiles(mismatchDirectory).Select(Path.GetFileName), Is.EquivalentTo(new[] { "expected.png", "actual.png", "comparison.json" }));
            Assert.That(mismatchMetadata.RootElement.GetProperty("reason").GetString(), Is.EqualTo("dimension_mismatch"));
            Assert.That(mismatchMetadata.RootElement.GetProperty("width").GetInt32(), Is.EqualTo(2));
            Assert.That(mismatchMetadata.RootElement.GetProperty("height").GetInt32(), Is.EqualTo(1));
            Assert.That(mismatchMetadata.RootElement.GetProperty("expectedWidth").GetInt32(), Is.EqualTo(1));
            Assert.That(mismatchMetadata.RootElement.GetProperty("expectedHeight").GetInt32(), Is.EqualTo(1));
            Assert.That(Directory.GetFiles(Path.Combine(_root, "artifacts"), "index.html", SearchOption.AllDirectories), Is.Empty);
        });
    }

    [Test]
    public async Task PixelDifferenceWritesPortableDataAndPreservesNamesAsJson()
    {
        const string name = "danger<script>alert(1)<script>";
        const string variant = "variant<script>alert(2)<script>";
        var context = new FakeContext(Png(2, 1, 10));
        await GuaVisualAssertions.ExpectScreenshotAsync(context, name, Options(variant, update: true));
        context.Set(Png(2, 1, 200));

        var failure = Assert.ThrowsAsync<InvalidOperationException>(
            () => GuaVisualAssertions.ExpectScreenshotAsync(context, name, Options(variant, threshold: .1f, ratio: .2, masks: [new(0, 0, 0, 0)])));
        var comparison = Directory.GetFiles(Path.Combine(_root, "artifacts"), "comparison.json", SearchOption.AllDirectories).Single();
        using var metadata = JsonDocument.Parse(File.ReadAllText(comparison));
        var directory = Path.GetDirectoryName(comparison)!;

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain(Path.GetFullPath(directory)));
            Assert.That(Directory.GetFiles(directory).Select(Path.GetFileName), Is.EquivalentTo(new[] { "expected.png", "actual.png", "diff.png", "comparison.json" }));
            Assert.That(Directory.GetFiles(directory, "*.html"), Is.Empty);
            Assert.That(metadata.RootElement.GetProperty("name").GetString(), Is.EqualTo(name));
            Assert.That(metadata.RootElement.GetProperty("variant").GetString(), Is.EqualTo(variant));
            Assert.That(metadata.RootElement.GetProperty("baselineCreated").GetBoolean(), Is.False);
            Assert.That(metadata.RootElement.GetProperty("reason").GetString(), Is.EqualTo("pixel_difference"));
            Assert.That(metadata.RootElement.GetProperty("width").GetInt32(), Is.EqualTo(2));
            Assert.That(metadata.RootElement.GetProperty("height").GetInt32(), Is.EqualTo(1));
            Assert.That(metadata.RootElement.GetProperty("comparedPixels").GetInt64(), Is.EqualTo(2));
            Assert.That(metadata.RootElement.GetProperty("differentPixels").GetInt64(), Is.EqualTo(2));
            Assert.That(metadata.RootElement.GetProperty("differentPixelRatio").GetDouble(), Is.EqualTo(1));
            Assert.That(metadata.RootElement.GetProperty("pixelThreshold").GetSingle(), Is.EqualTo(.1f));
            Assert.That(metadata.RootElement.GetProperty("maxDifferentPixelRatio").GetDouble(), Is.EqualTo(.2));
            Assert.That(metadata.RootElement.GetProperty("masks")[0].GetProperty("x").GetInt32(), Is.Zero);
            Assert.That(Path.IsPathFullyQualified(metadata.RootElement.GetProperty("baselinePath").GetString()!), Is.True);
        });
    }

    private ScreenshotOptions Options(string variant, bool update = false, float threshold = 0, double ratio = 0, IReadOnlyList<GuaMaskRectangle>? masks = null) => new() { BaselineDirectory = Path.Combine(_root, "baselines"), ArtifactDirectory = Path.Combine(_root, "artifacts"), BaselineVariant = variant, UpdateBaselines = update, PixelThreshold = threshold, MaxDifferentPixelRatio = ratio, Masks = masks ?? [] };
    private static byte[] Png(int width, int height, byte first, byte? second = null) { var pixels = new byte[width * height * 4]; for (var i = 0; i < width * height; i++) { var value = i == 1 && second.HasValue ? second.Value : first; pixels[i * 4] = pixels[i * 4 + 1] = pixels[i * 4 + 2] = value; pixels[i * 4 + 3] = 255; } using var stream = new MemoryStream(); new ImageWriter().WritePng(pixels, width, height, ColorComponents.RedGreenBlueAlpha, stream); return stream.ToArray(); }

    private sealed class FakeContext(byte[] screenshot) : IGuaContext
    {
        private byte[] _screenshot = screenshot; public GuaActionRequest LastRequest { get; private set; }
        public void Set(byte[] value) => _screenshot = value;
        public string GetDiagnosticsJson() => JsonSerializer.Serialize(new { schemaVersion = 1, revision = 1, screenshot = new { dataUri = "data:image/png;base64," + Convert.ToBase64String(_screenshot), width = 1, height = 1 }, operations = Array.Empty<object>(), events = Array.Empty<object>() });
        public string GetUiTreeJson() => "{\"schemaVersion\":2,\"screen\":\"test\",\"frameSequence\":1,\"revision\":1,\"sessionEpoch\":1,\"nodes\":[{\"id\":\"password\",\"role\":\"textbox\",\"label\":\"Password\",\"bounds\":{\"x\":0,\"y\":0,\"w\":1,\"h\":1},\"visible\":true,\"enabled\":true,\"actions\":[\"set_value\"]}]}";
        public GuaQueryResult Query(GuaSelector selector) => selector.Id == "password" ? new(true, [new("password", "textbox", "Password", null)]) : new(true, []);
        public GuaActionError EnqueueAction(GuaActionRequest request, out ulong requestId) { LastRequest = request; requestId = 1; return GuaActionError.None; }
        public GuaNodeState GetNodeState(string id) => new(true, true); public string FindNodeById(string id) => id; public string FindNodeByRole(string role, string? name = null) => "password"; public string FindNodeByText(string text) => "password"; public bool EnqueueClick(string id) => true; public bool TryPollActionEvent(out GuaActionEvent e) { e = default; return false; } public bool TryPollActionEvent(ulong id, out GuaActionEvent e) { e = default; return false; } public bool TryPollEvent(out GuaEvent e) { e = default; return false; }
    }
}
