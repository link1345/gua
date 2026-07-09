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
        public bool TryPollEvent(out GuaEvent e) { e = default; return false; }
    }
}
