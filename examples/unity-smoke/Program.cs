using Gua.Core;
using Gua.Testing;

using var context = new GuaContext();
var host = new GuaTestHost(context);
_ = new GuaDiagnosticOptions { TestName = "unity-smoke" };

host.Frame("unity-smoke", frame =>
{
    frame.Button("unity-start", "開始", new GuaBounds(10, 20, 160, 40));
});

GuaAssertions.GetById(context, "unity-start").ToBeVisible();
var json = context.GetUiTreeJson();
if (!json.Contains("unity-start", StringComparison.Ordinal) ||
    !json.Contains("開始", StringComparison.Ordinal))
{
    throw new InvalidOperationException($"Unity smoke tree did not round-trip UTF-8 node data: {json}");
}

Console.WriteLine("Gua netstandard2.1 Unity smoke passed.");
