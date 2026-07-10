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
        await GuaVisualAssertions.ExpectScreenshotAsync(context, "size", Options("default", update: true));
        context.Set(Png(2, 1, 1));
        var mismatch = Assert.ThrowsAsync<InvalidOperationException>(() => GuaVisualAssertions.ExpectScreenshotAsync(context, "size", Options("default")));
        Assert.That(mismatch!.Message, Does.Contain("dimensions differ"));
        Assert.That(Directory.GetFiles(Path.Combine(_root, "artifacts"), "comparison.json", SearchOption.AllDirectories), Is.Not.Empty);
    }

    [Test]
    public void RecordingRejectsUnknownVersionAndNeverSerializesSensitiveValue()
    {
        Assert.Throws<InvalidDataException>(() => GuaRecordingFile.Validate(new(2, [])));
        var recording = new GuaRecording(1, [new(GuaRecordedAction.set_value, 0, 1, 2, true, 7, 7, new(Id: "password"), SecretKey: "login-password")]);
        var path = Path.Combine(_root, "recording.json"); GuaRecordingFile.Save(path, recording); var json = File.ReadAllText(path);
        Assert.Multiple(() => { Assert.That(json, Does.Contain("login-password")); Assert.That(json, Does.Not.Contain("secret-marker")); Assert.That(GuaRecordingFile.Load(path).SchemaVersion, Is.EqualTo(1)); });
    }

    [Test]
    public void RecorderDeduplicatesAutomationAndKeepsHostEvents()
    {
        const string json = "{\"revision\":9,\"operations\":[{\"requestId\":4,\"action\":\"click\",\"nodeId\":\"start\",\"sensitive\":false}],\"events\":[{\"requestId\":4,\"action\":\"click\",\"nodeId\":\"start\",\"sensitive\":false},{\"requestId\":0,\"action\":\"focus\",\"nodeId\":\"name\",\"sensitive\":false}]}";
        var recording = GuaRecordingFile.FromDiagnostics(json);
        Assert.Multiple(() => { Assert.That(recording.Steps, Has.Count.EqualTo(2)); Assert.That(recording.Steps[0].Target!.Id, Is.EqualTo("start")); Assert.That(recording.Steps[1].Target!.Id, Is.EqualTo("name")); });
    }

    [Test]
    public async Task ReplayPrefersSemanticIdRefusesCoordinatesAndRequiresSecretResolver()
    {
        var context = new FakeContext(Png(1, 1, 0));
        var semantic = new GuaRecording(1, [new(GuaRecordedAction.set_value, 0, 1, 2, true, Target: new(Id: "password"), SecretKey: "password")]);
        Assert.ThrowsAsync<InvalidOperationException>(() => GuaReplayer.ReplayAsync(context, semantic));
        await GuaReplayer.ReplayAsync(context, semantic, new() { SecretResolver = key => key == "password" ? "secret-marker" : null });
        Assert.Multiple(() => { Assert.That(context.LastRequest.NodeId, Is.EqualTo("password")); Assert.That(context.LastRequest.Sensitive, Is.True); });
        var coordinate = new GuaRecording(1, [new(GuaRecordedAction.click, 0, 1, 2, false, CoordinateFallback: new(3, 4))]);
        Assert.ThrowsAsync<InvalidOperationException>(() => GuaReplayer.ReplayAsync(context, coordinate));
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
