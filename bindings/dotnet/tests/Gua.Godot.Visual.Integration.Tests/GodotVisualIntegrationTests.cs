using System.Text;
using System.Text.Json;
using Gua.Core;
using Gua.Testing.Godot;
using Gua.Testing;
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

    [Test]
    public async Task V2TestingApisOperateRealGodotControlsThroughTheBridge()
    {
        const string secret = "v2-sensitive-secret-marker";
        using var host = StartGodot(diff: false, v2: true);
        await host.WaitForScreenshotAsync(TimeSpan.FromSeconds(15));

        var userName = GuaAssertions.GetById(host.Context, "v2-user-name");
        var setValue = await userName.SetValueAsync("alice", timeout: TimeSpan.FromSeconds(5));
        var focus = await userName.FocusAsync(timeout: TimeSpan.FromSeconds(5));
        var key = await GuaAssertions.PressKeyAsync(host.Context, "Enter", timeout: TimeSpan.FromSeconds(5));
        var select = await GuaAssertions.GetById(host.Context, "v2-provider").SelectAsync("google", timeout: TimeSpan.FromSeconds(5));
        var check = GuaAssertions.GetById(host.Context, "v2-remember").SetCheckedAndWait(true, TimeSpan.FromSeconds(5));
        var scroll = await GuaAssertions.GetById(host.Context, "v2-scroll").ScrollAsync(0, 50, timeout: TimeSpan.FromSeconds(5));
        var sensitive = await GuaAssertions.GetById(host.Context, "v2-secret").SetValueAsync(secret, sensitive: true, timeout: TimeSpan.FromSeconds(5));

        await userName.WaitForValueAsync("alice", TimeSpan.FromSeconds(5));
        await userName.WaitUntilFocusedAsync(timeout: TimeSpan.FromSeconds(5));
        await GuaAssertions.GetById(host.Context, "v2-remember").WaitUntilCheckedAsync(timeout: TimeSpan.FromSeconds(5));

        var scoped = GuaAssertions.Query(host.Context).ByRole("textbox").Within("v2-form").ByValue("alice").WhereFocused().Get();
        var selected = GuaAssertions.Query(host.Context).ByRole("combobox").ByValue("google").ByAction("select").Get();
        var checkedNode = GuaAssertions.Query(host.Context).WhereChecked().Get();
        var screenshot = host.SaveScreenshot(Path.Combine(_root, "attachments"), TestContext.CurrentContext.Test.Name);
        TestContext.AddTestAttachment(screenshot.AbsolutePath, "Godot v2 fixture screenshot");

        var status = host.GetContextStatus();
        var reset = host.ResetContext(new GuaResetOptions(Strict: true));
        Assert.Multiple(() =>
        {
            Assert.That(new[] { setValue, focus, key, select, check, scroll, sensitive }, Has.All.Property(nameof(GuaActionEvent.Succeeded)).True);
            Assert.That(key.NodeId, Is.Empty);
            Assert.That(sensitive.Value, Is.Empty);
            Assert.That(scoped.Id, Is.EqualTo("v2-user-name"));
            Assert.That(selected.Id, Is.EqualTo("v2-provider"));
            Assert.That(checkedNode.Id, Is.EqualTo("v2-remember"));
            Assert.That(host.Context.GetUiTreeJson(), Does.Not.Contain(secret));
            Assert.That(host.Context.GetDiagnosticsJson(), Does.Not.Contain(secret));
            Assert.That(screenshot.Width, Is.GreaterThan(0));
            Assert.That(screenshot.Height, Is.GreaterThan(0));
            Assert.That(Path.IsPathFullyQualified(screenshot.AbsolutePath), Is.True);
            Assert.That(status.IsClean, Is.True);
            Assert.That(reset.Result, Is.EqualTo(GuaResetResult.Succeeded));
        });
    }

    [Test]
    public async Task ParallelHostsUseDistinctAutomaticPortsAndExposeOnlyCompleteTrees()
    {
        using var first = StartGodotAutoPort();
        using var second = StartGodotAutoPort();
        await Task.WhenAll(
            first.WaitForScreenshotAsync(TimeSpan.FromSeconds(15)),
            second.WaitForScreenshotAsync(TimeSpan.FromSeconds(15)));

        var observedCounts = new List<int>();
        for (var sample = 0; sample < 100; sample++)
        {
            using var document = JsonDocument.Parse(first.Context.GetUiTreeJson());
            observedCounts.Add(document.RootElement.GetProperty("nodes").GetArrayLength());
            await Task.Delay(2);
        }
        Assert.Multiple(() =>
        {
            Assert.That(first.ProcessId, Is.Not.EqualTo(second.ProcessId));
            Assert.That(observedCounts, Has.All.EqualTo(observedCounts[0]));
            Assert.That(observedCounts[0], Is.GreaterThan(10));
        });
    }

    [Test]
    public async Task OnDemandCaptureReturnsANewerCorrelatedFrameAndCoalescesConcurrentRequests()
    {
        using var host = StartGodotAutoPort();
        var before = host.GetContextStatus();
        var captures = await Task.WhenAll(
            host.CaptureScreenshotAsync(TimeSpan.FromSeconds(15)),
            host.CaptureScreenshotAsync(TimeSpan.FromSeconds(15)));
        Assert.Multiple(() =>
        {
            Assert.That(captures[0].RequestId, Is.Not.EqualTo(captures[1].RequestId));
            Assert.That(captures, Has.All.Property(nameof(GuaScreenshot.SessionEpoch)).EqualTo(before.SessionEpoch));
            Assert.That(captures, Has.All.Property(nameof(GuaScreenshot.FrameSequence)).GreaterThan(before.FrameSequence));
            Assert.That(captures[0].DataUri, Is.EqualTo(captures[1].DataUri));
            Assert.That(captures, Has.All.Property(nameof(GuaScreenshot.Width)).GreaterThan(0));
            Assert.That(captures, Has.All.Property(nameof(GuaScreenshot.Height)).GreaterThan(0));
        });
    }

    [Test]
    public async Task OnDemandCaptureDistinguishesHeadlessAndCancellation()
    {
        using var headless = GodotSceneTestHost.Load("res://Main.tscn", new GodotSceneTestHostOptions
        {
            GodotExecutablePath = Environment.GetEnvironmentVariable("GODOT_EXECUTABLE"),
            ProjectPath = Path.Combine(FindRepositoryRoot(), "examples", "godot-gdscript"),
            UseAvailableBridgePort = true,
            Headless = true,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            RequestTimeout = TimeSpan.FromSeconds(10),
            SceneTimeout = TimeSpan.FromSeconds(15),
        });
        var unavailable = Assert.ThrowsAsync<GuaScreenshotException>(() =>
            headless.CaptureScreenshotAsync(TimeSpan.FromSeconds(5)));
        Assert.That(unavailable!.Error, Is.EqualTo(GuaScreenshotError.Headless));

        using var rendered = StartGodotAutoPort();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(() =>
            rendered.CaptureScreenshotAsync(TimeSpan.FromSeconds(5), cancellation.Token));
    }

    [Test]
    public void GodotDiagnosticsSessionCapturesLiveProcessOutputAndOnDemandScreenshot()
    {
        using var host = StartGodotAutoPort();
        var session = host.CreateDiagnosticsSession(
            TestContext.CurrentContext.Test.Name,
            Path.Combine(_root, "diagnostics"),
            captureScreenshot: true,
            callerMetadata: new Dictionary<string, string> { ["fixture"] = "real-renderer" });
        var primary = new InvalidOperationException("intentional Godot integration failure");
        var result = session.Capture(primary);

        Assert.Multiple(() =>
        {
            Assert.That(result.PrimaryException, Is.SameAs(primary));
            Assert.That(result.CaptureErrors, Is.Empty);
            Assert.That(result.Files, Has.Some.Property(nameof(GuaDiagnosticFile.Path)).EndsWith("godot-stdout.txt"));
            Assert.That(result.Files, Has.Some.Property(nameof(GuaDiagnosticFile.Path)).EndsWith("godot-stderr.txt"));
            Assert.That(result.Files, Has.Some.Property(nameof(GuaDiagnosticFile.Path)).EndsWith("godot-process.json"));
            Assert.That(result.Files, Has.Some.Property(nameof(GuaDiagnosticFile.MediaType)).EqualTo("image/png"));
        });
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

    private static GodotSceneTestHost StartGodot(bool diff, bool v2 = false)
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
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["GUA_VISUAL_E2E"] = "1",
                ["GUA_V2_E2E"] = v2 ? "1" : "0",
                ["GUA_BRIDGE_PORT"] = port.ToString(),
            },
            AdditionalArguments = ["--display-driver", "windows", "--rendering-method", "gl_compatibility", "--resolution", "1280x720", "--position", "0,0"],
        });
    }

    private static GodotSceneTestHost StartGodotAutoPort()
    {
        return GodotSceneTestHost.LoadRendered("res://Main.tscn", new GodotSceneTestHostOptions
        {
            GodotExecutablePath = Environment.GetEnvironmentVariable("GODOT_EXECUTABLE"),
            ProjectPath = Path.Combine(FindRepositoryRoot(), "examples", "godot-gdscript"),
            UseAvailableBridgePort = true,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            RequestTimeout = TimeSpan.FromSeconds(10),
            SceneTimeout = TimeSpan.FromSeconds(15),
            StartupReset = new GuaResetOptions(Strict: true),
            TeardownReset = new GuaResetOptions(Strict: true),
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["GUA_VISUAL_E2E"] = "1",
                ["GUA_V2_E2E"] = "1",
            },
            AdditionalArguments = ["--display-driver", "windows", "--rendering-method", "gl_compatibility", "--resolution", "1280x720"],
        });
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "CMakePresets.json"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the Gua repository root from the test output directory.");
    }
}
