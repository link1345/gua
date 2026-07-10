using System.Text;
using System.Text.Json;
using Gua.Core;
using Gua.Testing.Godot;
using Gua.Testing.Visual;
using NUnit.Framework;

namespace Gua.Godot.Visual.Integration.Tests;

[NonParallelizable]
public sealed class GodotVisualIntegrationTests
{
    private const string Secret = "gua-visual-secret-marker";
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("GUA_GODOT_REAL_RENDERER_TEST"), "1", StringComparison.Ordinal))
            Assert.Ignore("Set GUA_GODOT_REAL_RENDERER_TEST=1 to run the Godot real-renderer integration test.");
        _root = Path.Combine(Path.GetTempPath(), "gua-godot-visual", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("GUA_VISUAL_E2E", null);
        Environment.SetEnvironmentVariable("GUA_VISUAL_E2E_DIFF", null);
        Environment.SetEnvironmentVariable("GUA_BRIDGE_PORT", null);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Test]
    public async Task RealViewportSupportsBaselineDiffReplayAndSecretRedaction()
    {
        var baselines = Path.Combine(_root, "baselines");
        var failures = Path.Combine(_root, "failure");
        var options = new ScreenshotOptions
        {
            BaselineDirectory = baselines,
            FailureDirectory = failures,
            BaselineVariant = "windows-gl-compatibility",
            PixelThreshold = 0,
            MaxDifferentPixelRatio = 0,
        };

        using (var update = StartGodot(diff: false))
        {
            var screenshot = await update.WaitForScreenshotAsync(TimeSpan.FromSeconds(15));
            Assert.That(screenshot, Does.Contain("data:image/png;base64,"));
            var result = await GuaVisualAssertions.ExpectScreenshotAsync(update.Context, "title", new ScreenshotOptions
            {
                BaselineDirectory = baselines,
                FailureDirectory = failures,
                BaselineVariant = options.BaselineVariant,
                UpdateBaselines = true,
            });
            Assert.That(result.BaselineUpdated, Is.True);
        }

        using (var normal = StartGodot(diff: false))
        {
            await normal.WaitForScreenshotAsync(TimeSpan.FromSeconds(15));
            var matched = await GuaVisualAssertions.ExpectScreenshotAsync(normal.Context, "title", options);
            Assert.That(matched.Matched, Is.True);

            var recording = new GuaRecording(1,
            [
                new(GuaRecordedAction.set_checked, 0, 0, 0, false,
                    Target: new GuaRecordingTarget(Id: "visual-checkbox"), Value: "true"),
                new(GuaRecordedAction.select, 1, 0, 0, false,
                    Target: new GuaRecordingTarget(Id: "visual-select"), Value: "Beta"),
                new(GuaRecordedAction.set_value, 2, 0, 0, true,
                    Target: new GuaRecordingTarget(Id: "visual-password"), SecretKey: "password"),
            ]);
            await GuaReplayer.ReplayAsync(normal.Context, recording, new GuaReplayOptions
            {
                SecretResolver = key => key == "password" ? Secret : null,
            });
            var events = await WaitForActionEventsAsync(normal.Context, 3, TimeSpan.FromSeconds(5));

            var diagnostics = normal.Context.GetDiagnosticsJson();
            var recorded = JsonSerializer.Serialize(GuaRecordingFile.FromDiagnostics(diagnostics));
            Assert.Multiple(() =>
            {
                Assert.That(events, Has.All.Property(nameof(GuaActionEvent.Succeeded)).True);
                Assert.That(events.Select(value => value.NodeId), Is.EquivalentTo(new[] { "visual-checkbox", "visual-select", "visual-password" }));
                Assert.That(diagnostics, Does.Not.Contain(Secret));
                Assert.That(recorded, Does.Not.Contain(Secret));
                Assert.That(recorded, Does.Contain("visual-checkbox"));
                Assert.That(recorded, Does.Contain("visual-select"));
                Assert.That(recorded, Does.Contain("visual-password"));
            });
        }

        using (var different = StartGodot(diff: true))
        {
            await different.WaitForScreenshotAsync(TimeSpan.FromSeconds(15));
            var error = Assert.ThrowsAsync<InvalidOperationException>(
                () => GuaVisualAssertions.ExpectScreenshotAsync(different.Context, "title", options));
            Assert.That(error!.Message, Does.Not.Contain(Secret));
        }

        var failureDirectory = Directory.GetDirectories(failures).Single();
        var artifactNames = Directory.GetFiles(failureDirectory).Select(Path.GetFileName).ToArray();
        Assert.That(artifactNames, Is.EquivalentTo(new[] { "expected.png", "actual.png", "diff.png", "comparison.json" }));
        foreach (var path in Directory.GetFiles(failureDirectory))
            Assert.That(Encoding.UTF8.GetString(File.ReadAllBytes(path)), Does.Not.Contain(Secret));
    }

    private static async Task<IReadOnlyList<GuaActionEvent>> WaitForActionEventsAsync(
        IGuaContext context, int count, TimeSpan timeout)
    {
        var events = new List<GuaActionEvent>();
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (events.Count < count && DateTimeOffset.UtcNow < deadline)
        {
            while (context.TryPollActionEvent(out var value)) events.Add(value);
            if (events.Count < count) await Task.Delay(25);
        }
        Assert.That(events, Has.Count.EqualTo(count), "Godot did not emit all semantic replay completion events.");
        return events;
    }

    private static GodotSceneTestHost StartGodot(bool diff)
    {
        const int port = 18765;
        Environment.SetEnvironmentVariable("GUA_VISUAL_E2E", "1");
        Environment.SetEnvironmentVariable("GUA_VISUAL_E2E_DIFF", diff ? "1" : null);
        Environment.SetEnvironmentVariable("GUA_BRIDGE_PORT", port.ToString());
        return GodotSceneTestHost.Load("res://Main.tscn", new GodotSceneTestHostOptions
        {
            GodotExecutablePath = Environment.GetEnvironmentVariable("GODOT_EXECUTABLE"),
            ProjectPath = Path.Combine(FindRepositoryRoot(), "examples", "godot-gdscript"),
            BridgeUrl = $"ws://127.0.0.1:{port}",
            Headless = false,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            RequestTimeout = TimeSpan.FromSeconds(10),
            SceneTimeout = TimeSpan.FromSeconds(15),
            AdditionalArguments = ["--display-driver", "windows", "--rendering-method", "gl_compatibility", "--resolution", "1280x720", "--position", "0,0"],
        });
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "CMakePresets.json"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the Gua repository root from the test output directory.");
    }
}
