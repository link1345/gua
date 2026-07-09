using Gua.Core;
using Gua.Testing;
using NUnit.Framework;

namespace Gua.DotNetNUnitSample;

[TestFixture]
public sealed class TitleScreenTests
{
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
}
