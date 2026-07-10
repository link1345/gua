using Gua.Core;
using Gua.Testing;
using NUnit.Framework;

namespace Gua.DotNetNUnitSample;

[TestFixture]
public sealed class TitleScreenTests
{
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
}
