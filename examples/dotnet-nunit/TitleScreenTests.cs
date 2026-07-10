using Gua.Core;
using Gua.Testing;
using NUnit.Framework;

namespace Gua.DotNetNUnitSample;

[TestFixture]
public sealed class TitleScreenTests
{
    [Test]
    public void StrictSelectorsShareNativeMatchingAndScopeRules()
    {
        using var ui = new GuaContext();
        ui.BeginFrame("selectors");
        ui.RegisterNode(new GuaNodeDescriptor("left", "panel", "Left", new GuaBounds(0, 0, 10, 10)));
        ui.RegisterNode(new GuaNodeDescriptor("right", "panel", "Right", new GuaBounds(0, 0, 10, 10)));
        ui.RegisterNode(new GuaNodeDescriptor("group", "panel", "Group", new GuaBounds(0, 0, 10, 10), ParentId: "left"));
        ui.RegisterNode(new GuaNodeDescriptor("left-save", "button", "保存", new GuaBounds(0, 0, 1, 1), ParentId: "group", Text: "保存"));
        ui.RegisterNode(new GuaNodeDescriptor("right-save", "button", "保存", new GuaBounds(0, 0, 1, 1), ParentId: "right", Text: "保存"));
        ui.RegisterNode(new GuaNodeDescriptor("hidden", "button", "Hidden", new GuaBounds(0, 0, 1, 1), ParentId: "left", Text: "Hidden", Visible: false));
        ui.EndFrame();

        var left = GuaAssertions.Query(ui).ByText("保", GuaMatchMode.Contains).Within("left").WhereVisible().Get();
        Assert.That(left.Snapshot.Id, Is.EqualTo("left-save"));
        GuaAssertions.Query(ui).ByRole("button").Within("left", directChild: true).AssertCount(1);

        var ambiguous = Assert.Throws<GuaAssertionException>(() => GuaAssertions.GetByText(ui, "保存"));
        Assert.That(ambiguous!.Message, Does.Contain("left-save").And.Contain("right-save").And.Contain("Within"));

        var invalid = Assert.Throws<GuaAssertionException>(() => GuaAssertions.Query(ui).ByText("[", GuaMatchMode.Regex).QueryAll());
        Assert.That(invalid!.Message, Does.Contain("Invalid Gua selector"));
    }

    [Test]
    public void V2NodeStatePreservesUnknownAndStableRevision()
    {
        using var _ = GuaAssertionScope.UseNUnit(Assert.Fail);
        using var ui = new GuaContext();

        void Render(bool isChecked)
        {
            ui.BeginFrame("settings");
            ui.RegisterNode(new GuaNodeDescriptor(
                "remember", "checkbox", "Remember me", new GuaBounds(10, 20, 100, 24),
                ParentId: "form", Text: "Remember me", Focused: false, Checked: isChecked));
            ui.EndFrame();
        }

        Render(false);
        var first = GuaAssertions.GetById(ui, "remember").Snapshot;
        Assert.Multiple(() =>
        {
            Assert.That(first.SchemaVersion, Is.EqualTo(2));
            Assert.That(first.FrameSequence, Is.EqualTo(1));
            Assert.That(first.Revision, Is.EqualTo(1));
            Assert.That(first.ParentId, Is.EqualTo("form"));
            Assert.That(first.Checked, Is.False);
            Assert.That(first.Selected, Is.Null);
        });

        Render(false);
        Assert.That(GuaAssertions.GetById(ui, "remember").Snapshot.Revision, Is.EqualTo(1));
        Render(true);
        Assert.That(GuaAssertions.GetById(ui, "remember").Snapshot.Revision, Is.EqualTo(2));

        var nativeState = ui.GetNodeStateV2("remember");
        Assert.That(nativeState.Checked, Is.True);
        Assert.That(nativeState.Selected, Is.Null);
    }

    [Test]
    public void LegacySnapshotWithoutVersionMetadataRemainsReadable()
    {
        using var _ = GuaAssertionScope.UseNUnit(Assert.Fail);
        var snapshot = GuaAssertions.GetById(new LegacyContext(), "legacy").Snapshot;
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Label, Is.EqualTo("Legacy"));
            Assert.That(snapshot.SchemaVersion, Is.Null);
            Assert.That(snapshot.FrameSequence, Is.Null);
            Assert.That(snapshot.Checked, Is.Null);
        });
    }

    [TestCase("10", "10")]
    [TestCase("true", "true")]
    [TestCase("null", null)]
    public void SnapshotScalarValueRemainsReadable(string jsonValue, string? expected)
    {
        using var _ = GuaAssertionScope.UseNUnit(Assert.Fail);
        var tree = $$$"""{"schemaVersion":2,"frameSequence":1,"revision":1,"screen":"test","nodes":[{"id":"status","role":"status","label":"Status","value":{{{jsonValue}}},"visible":true,"enabled":true,"bounds":{"x":0,"y":0,"w":1,"h":1},"actions":[]}]}""";
        var snapshot = GuaAssertions.GetById(new SequenceContext(tree), "status").Snapshot;
        Assert.That(snapshot.Value, Is.EqualTo(expected));
    }

    [Test]
    public void StartClickShowsLoadingText()
    {
        using var _ = GuaAssertionScope.UseNUnit(Assert.Fail);
        using var ui = new GuaContext();
        var host = new GuaTestHost(ui);
        var loading = false;

        RenderTitle(host, loading);

        GuaAssertions.GetByRole(ui, "button", "Start Game")
            .ToBeVisible()
            .ToBeEnabled()
            .Click();

        RenderTitle(host, loading);
        host.DrainClickEvents(nodeId =>
        {
            if (nodeId == "start")
            {
                loading = true;
            }
        });
        RenderTitle(host, loading);

        GuaAssertions.WaitForText(ui, "Loading...", TimeSpan.FromSeconds(1))
            .ToBeVisible()
            .ToHaveRole("text");
    }

    [Test]
    public void DisabledStartButtonCannotBeClicked()
    {
        using var _ = GuaAssertionScope.UseNUnit(Assert.Fail);
        using var ui = new GuaContext();
        var host = new GuaTestHost(ui);

        host.Frame("title", frame =>
        {
            frame.Button("start", "Start Game", new GuaBounds(100, 100, 240, 64), enabled: false);
        });

        GuaAssertions.GetById(ui, "start")
            .ToBeVisible()
            .ToBeDisabled();
    }

    [Test]
    public void GenericActionCorrelatesRequestAndRedactsSensitiveValue()
    {
        using var ui = new GuaContext();
        ui.BeginFrame("form");
        ui.RegisterNode(new GuaNodeDescriptor(
            "name", "textbox", "Name", new GuaBounds(0, 0, 100, 20), Value: string.Empty));
        ui.RegisterNode(new GuaNodeDescriptor("remember", "checkbox", "Remember", new GuaBounds(0, 20, 100, 20), Checked: false));
        ui.RegisterNode(new GuaNodeDescriptor("difficulty", "combobox", "Difficulty", new GuaBounds(0, 40, 100, 20), Value: "Easy"));
        ui.RegisterNode(new GuaNodeDescriptor("content", "scrollarea", "Content", new GuaBounds(0, 60, 100, 100)));
        ui.EndFrame();

        var name = GuaAssertions.GetById(ui, "name");
        var requestId = name.SetValue("secret-marker", sensitive: true);
        Assert.That(name.Focus(), Is.GreaterThan(requestId));
        Assert.That(name.PressKey("A"), Is.GreaterThan(requestId));
        Assert.That(GuaAssertions.GetById(ui, "remember").SetChecked(true), Is.GreaterThan(requestId));
        Assert.That(GuaAssertions.GetById(ui, "difficulty").Select("Hard"), Is.GreaterThan(requestId));
        Assert.That(GuaAssertions.GetById(ui, "content").Scroll(1, 2), Is.GreaterThan(requestId));
        Assert.That(ui.TryConsumeAction(GuaActionType.SetValue, "name", out var request), Is.True);
        Assert.That(request.RequestId, Is.EqualTo(requestId));
        Assert.That(request.Value, Is.EqualTo("secret-marker"));

        Assert.That(ui.EmitActionResult(new GuaActionEvent(
            requestId, GuaActionType.SetValue, true, GuaActionError.None, "name", request.Value!, true)), Is.True);
        Assert.That(ui.TryPollActionEvent(out var observed), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(observed.RequestId, Is.EqualTo(requestId));
            Assert.That(observed.Succeeded, Is.True);
            Assert.That(observed.Sensitive, Is.True);
            Assert.That(observed.Value, Is.Empty);
        });
    }

    [Test]
    public void StrictSessionResetDetectsLeakWithoutDiscardingIt()
    {
        using var ui = new GuaContext();
        var session = new GuaTestSession(ui);
        ui.BeginFrame("form");
        ui.RegisterNode(new GuaNodeDescriptor("password", "textbox", "Password", new GuaBounds(0, 0, 100, 20), Value: string.Empty));
        ui.EndFrame();
        Assert.That(GuaAssertions.GetById(ui, "password").SetValue("secret-marker", sensitive: true), Is.GreaterThan(0));

        var error = Assert.Throws<GuaAssertionException>(() =>
            session.Reset(new GuaResetOptions(Strict: true)));
        Assert.That(error!.Message, Does.Contain("pending=1").And.Not.Contain("secret-marker"));
        Assert.That(session.Inspect().PendingRequestCount, Is.EqualTo(1));
    }

    [Test]
    public void NonStrictResetStartsNewEpochAndPreservesOptionalStateByDefault()
    {
        using var ui = new GuaContext();
        using var other = new GuaContext();
        var session = new GuaTestSession(ui);
        ui.BeginFrame("title");
        ui.RegisterNode("start", "button", "Start", new GuaBounds(0, 0, 1, 1));
        ui.EndFrame();
        ui.AddLog(GuaLogLevel.Info, "keep");
        ui.SetScreenshot("data:image/png;base64,AA==", 1, 1);
        other.BeginFrame("other");
        other.RegisterNode("other", "button", "Other", new GuaBounds(0, 0, 1, 1));
        other.EndFrame();

        var report = session.Reset();
        var status = session.Inspect();
        Assert.Multiple(() =>
        {
            Assert.That(report.Result, Is.EqualTo(GuaResetResult.Succeeded));
            Assert.That(report.PreviousSessionEpoch, Is.EqualTo(1));
            Assert.That(report.SessionEpoch, Is.EqualTo(2));
            Assert.That(status.FrameSequence, Is.Zero);
            Assert.That(status.Revision, Is.Zero);
            Assert.That(status.NodeCount, Is.Zero);
            Assert.That(status.LogCount, Is.EqualTo(1));
            Assert.That(status.HasScreenshot, Is.True);
            Assert.That(other.GetContextStatus().NodeCount, Is.EqualTo(1));
        });

        var cleared = session.Reset(new GuaResetOptions(GuaResetTargets.All));
        Assert.Multiple(() =>
        {
            Assert.That(cleared.DiscardedLogCount, Is.EqualTo(1));
            Assert.That(cleared.DiscardedScreenshot, Is.True);
            Assert.That(session.Inspect().LogCount, Is.Zero);
            Assert.That(session.Inspect().HasScreenshot, Is.False);
        });
    }

    [Test]
    public void DiagnosticsWriterCreatesVersionedRedactedArtifacts()
    {
        using var ui = new GuaContext();
        ui.ConfigureDiagnostics(2, "{\"bridge\":\"local\"}");
        ui.BeginFrame("form");
        ui.RegisterNode(new GuaNodeDescriptor("password", "textbox", "Password", new GuaBounds(0, 0, 100, 20), Value: string.Empty));
        ui.EndFrame();
        var before = ui.GetUiTreeJson();
        var requestId = GuaAssertions.GetById(ui, "password").SetValue("secret-marker", sensitive: true);
        Assert.That(ui.TryConsumeAction(GuaActionType.SetValue, "password", out var request), Is.True);
        Assert.That(ui.EmitActionResult(new GuaActionEvent(requestId, GuaActionType.SetValue, true,
            GuaActionError.None, "password", request.Value!, true)), Is.True);
        Assert.That(ui.TryPollActionEvent(requestId, out _), Is.True);
        ui.BeginFrame("form");
        ui.RegisterNode(new GuaNodeDescriptor("password", "textbox", "Password changed", new GuaBounds(0, 0, 100, 20), Value: string.Empty));
        ui.EndFrame();

        var output = Path.Combine(TestContext.CurrentContext.WorkDirectory, "gua-diagnostics", Guid.NewGuid().ToString("N"));
        try
        {
            var capture = GuaDiagnosticWriter.Capture(ui, "intentional failure", new GuaDiagnosticOptions
            {
                TestName = "diagnostics/redaction",
                OutputDirectory = output,
                Environment = new Dictionary<string, string> { ["bridge"] = "in-process" },
            }, before);
            Assert.That(capture.Error, Is.Null);
            Assert.That(capture.ArtifactPath, Is.Not.Null);
            var path = capture.ArtifactPath!;
            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(Path.Combine(path, "failure-summary.txt")), Is.True);
                Assert.That(File.Exists(Path.Combine(path, "environment.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(path, "ui-tree.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(path, "ui-tree.diff.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(path, "events.jsonl")), Is.True);
                Assert.That(File.Exists(Path.Combine(path, "operations.jsonl")), Is.True);
                Assert.That(File.Exists(Path.Combine(path, "pending-requests.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(path, "logs.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(path, "screenshot.png")), Is.False);
                Assert.That(string.Join("\n", Directory.EnumerateFiles(path).Select(File.ReadAllText)), Does.Not.Contain("secret-marker"));
                Assert.That(File.ReadAllText(Path.Combine(path, "ui-tree.diff.json")), Does.Contain("password").And.Contain("changed"));
            });
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        }
    }

    [Test]
    public void AssertionAndWaitFailuresCaptureArtifactsAndPreserveFailureOnWriterError()
    {
        using var ui = new GuaContext();
        ui.BeginFrame("test");
        ui.RegisterNode(new GuaNodeDescriptor("status", "status", "Status", new GuaBounds(0, 0, 1, 1), Visible: false));
        ui.EndFrame();
        var output = Path.Combine(TestContext.CurrentContext.WorkDirectory, "gua-diagnostics", Guid.NewGuid().ToString("N"));
        try
        {
            using (GuaAssertionScope.Use(new GuaAssertionOptions
            {
                Diagnostics = new GuaDiagnosticOptions { TestName = "assertion", OutputDirectory = output },
            }))
            {
                var assertion = Assert.Throws<GuaAssertionException>(() => GuaAssertions.GetById(ui, "status").ToBeVisible());
                Assert.That(assertion!.Message, Does.Contain("Gua diagnostics:"));
            }
            using (GuaAssertionScope.Use(new GuaAssertionOptions
            {
                Diagnostics = new GuaDiagnosticOptions { TestName = "wait", OutputDirectory = output },
            }))
            {
                var timeout = Assert.ThrowsAsync<GuaAssertionException>(async () =>
                    await GuaAssertions.WaitForVisibleAsync(ui, "status", TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(1)));
                Assert.That(timeout!.Message, Does.Contain("Gua diagnostics:"));
            }

            var blockingFile = Path.Combine(output, "not-a-directory");
            Directory.CreateDirectory(output);
            File.WriteAllText(blockingFile, "block");
            using (GuaAssertionScope.Use(new GuaAssertionOptions
            {
                Diagnostics = new GuaDiagnosticOptions { TestName = "writer-failure", OutputDirectory = blockingFile },
            }))
            {
                var original = Assert.Throws<GuaAssertionException>(() => GuaAssertions.GetById(ui, "status").ToBeVisible());
                Assert.That(original!.Message, Does.StartWith("Expected Gua node").And.Contain("capture error"));
            }
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        }
    }

    [Test]
    public async Task AsyncWaitsObserveStateTextValueAndRemovalWithoutFixedSleeps()
    {
        using var _ = GuaAssertionScope.UseNUnit(Assert.Fail);
        var changing = new SequenceContext(
            Tree(1, 1, Node(visible: false, enabled: false, text: "Old", value: "1")),
            Tree(2, 2, Node(visible: true, enabled: true, text: "Ready", value: "2")));

        await GuaAssertions.WaitForVisibleAsync(changing, "status", pollInterval: TimeSpan.FromMilliseconds(1));
        await GuaAssertions.WaitForEnabledAsync(changing, "status", pollInterval: TimeSpan.FromMilliseconds(1));
        await GuaAssertions.WaitForTextAsync(changing, "status", "Ready", pollInterval: TimeSpan.FromMilliseconds(1));
        await GuaAssertions.WaitForValueAsync(changing, "status", "2", pollInterval: TimeSpan.FromMilliseconds(1));

        var removed = new SequenceContext(Tree(1, 1, Node()), Tree(2, 2));
        await GuaAssertions.WaitForHiddenAsync(removed, "status", pollInterval: TimeSpan.FromMilliseconds(1));
    }

    [Test]
    public async Task StableSnapshotCountsDistinctFramesOnly()
    {
        var advancing = new SequenceContext(Tree(1, 7, Node()), Tree(1, 7, Node()), Tree(2, 7, Node()), Tree(3, 7, Node()));
        await GuaAssertions.WaitForStableSnapshotAsync(advancing, 3, pollInterval: TimeSpan.FromMilliseconds(1));

        var stopped = new SequenceContext(Tree(9, 7, Node()));
        var error = Assert.ThrowsAsync<GuaAssertionException>(async () =>
            await GuaAssertions.WaitForStableSnapshotAsync(stopped, 3, TimeSpan.FromMilliseconds(15), TimeSpan.FromMilliseconds(1)));
        Assert.That(error!.Message, Does.Contain("observed 1").And.Contain("frameSequence=9").And.Contain("revision=7"));
    }

    [Test]
    public void AsyncWaitHonorsCancellationAndReportsLastSemanticState()
    {
        var stopped = new SequenceContext(Tree(4, 8, Node(visible: false, enabled: false, text: "Old", value: "1")));
        var timeout = Assert.ThrowsAsync<GuaAssertionException>(async () =>
            await GuaAssertions.WaitForVisibleAsync(stopped, "status", TimeSpan.FromMilliseconds(15), TimeSpan.FromMilliseconds(1)));
        Assert.That(timeout!.Message, Does.Contain("id 'status'").And.Contain("be visible").And.Contain("visible=False")
            .And.Contain("frameSequence=4").And.Contain("revision=8"));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await GuaAssertions.WaitForEnabledAsync(stopped, "status", cancellationToken: cancellation.Token));
    }

    private static string Tree(ulong frame, ulong revision, string? node = null) =>
        $$$"""{"schemaVersion":2,"sessionEpoch":1,"frameSequence":{{{frame}}},"revision":{{{revision}}},"screen":"test","nodes":[{{{node}}}]}""";

    private static string Node(bool visible = true, bool enabled = true, string text = "Ready", string value = "2") =>
        $$$"""{"id":"status","role":"status","label":"Status","text":"{{{text}}}","value":"{{{value}}}","visible":{{{visible.ToString().ToLowerInvariant()}}},"enabled":{{{enabled.ToString().ToLowerInvariant()}}},"bounds":{"x":0,"y":0,"w":1,"h":1},"actions":[],"state":{}}""";

    private static void RenderTitle(GuaTestHost host, bool loading)
    {
        host.Frame(loading ? "loading" : "title", frame =>
        {
            if (!loading)
            {
                frame.Button("start", "Start Game", new GuaBounds(100, 100, 240, 64));
            }

            if (loading)
            {
                frame.Text("loading", "Loading...", new GuaBounds(100, 180, 240, 24));
            }
        });
    }

    private sealed class LegacyContext : IGuaContext
    {
        private const string Tree = """
            {"screen":"legacy","nodes":[{"id":"legacy","role":"button","label":"Legacy","visible":true,"enabled":true,"bounds":{"x":0,"y":0,"w":1,"h":1},"actions":["click"]}]}
            """;

        public string GetUiTreeJson() => Tree;
        public GuaNodeState GetNodeState(string id) => new(true, true);
        public string FindNodeById(string id) => id == "legacy" ? id : throw new InvalidOperationException();
        public string FindNodeByRole(string role, string? name = null) => "legacy";
        public string FindNodeByText(string text) => "legacy";
        public bool EnqueueClick(string id) => true;
        public GuaActionError EnqueueAction(GuaActionRequest request, out ulong requestId) { requestId = 1; return GuaActionError.None; }
        public bool TryPollActionEvent(out GuaActionEvent e) { e = default; return false; }
        public bool TryPollActionEvent(ulong requestId, out GuaActionEvent e) { e = default; return false; }
        public bool TryPollEvent(out GuaEvent e) { e = default; return false; }
    }

    private sealed class SequenceContext(params string[] snapshots) : IGuaContext
    {
        private int _index;
        public string GetUiTreeJson()
        {
            var index = Math.Min(Interlocked.Increment(ref _index) - 1, snapshots.Length - 1);
            return snapshots[index];
        }
        public GuaNodeState GetNodeState(string id) => new(true, true);
        public string FindNodeById(string id) => id;
        public string FindNodeByRole(string role, string? name = null) => "status";
        public string FindNodeByText(string text) => "status";
        public bool EnqueueClick(string id) => true;
        public GuaActionError EnqueueAction(GuaActionRequest request, out ulong requestId) { requestId = 1; return GuaActionError.None; }
        public bool TryPollActionEvent(out GuaActionEvent e) { e = default; return false; }
        public bool TryPollActionEvent(ulong requestId, out GuaActionEvent e) { e = default; return false; }
        public bool TryPollEvent(out GuaEvent e) { e = default; return false; }
    }
}
