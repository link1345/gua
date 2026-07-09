using Gua.Core;
using Gua.Testing;

using var ui = new GuaContext();
var host = new GuaTestHost(ui);
var loading = false;

Render();

GuaAssertions.GetByRole(ui, "button", "Start Game")
    .ToBeVisible()
    .ToBeEnabled()
    .Click();

Render();
host.DrainClickEvents(nodeId =>
{
    if (nodeId == "start")
    {
        loading = true;
    }
});
Render();

GuaAssertions.WaitForText(ui, "Loading...", TimeSpan.FromSeconds(1))
    .ToBeVisible()
    .ToHaveRole("text");

Console.WriteLine("Gua.Testing sample passed.");

void Render()
{
    host.Frame(loading ? "loading" : "title", frame =>
    {
        if (!loading && frame.Button("start", "Start Game", new GuaBounds(100, 100, 240, 64)))
        {
            // GuaTestHost emits the click event; test code decides how the game state changes.
        }

        if (loading)
        {
            frame.Text("loading", "Loading...", new GuaBounds(100, 180, 240, 24));
        }
    });
}
