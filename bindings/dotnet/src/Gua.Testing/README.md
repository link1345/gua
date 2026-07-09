# Gua.Testing

`Gua.Testing` adds locator, assertion, wait, and test-host helpers on top of
`Gua.Core`.

```csharp
using var ui = new GuaContext();
var host = new GuaTestHost(ui);
var loading = false;

host.Frame("title", frame =>
{
    if (frame.Button("start", "Start Game", new GuaBounds(0, 0, 200, 40)))
    {
        loading = true;
    }
});

GuaAssertions.GetByRole(ui, "button", "Start Game").Click();

host.Frame("title", frame =>
{
    if (frame.Button("start", "Start Game", new GuaBounds(0, 0, 200, 40)))
    {
        loading = true;
    }
});

host.Frame("loading", frame =>
{
    if (loading)
    {
        frame.Text("loading", "Loading...", new GuaBounds(0, 48, 200, 24));
    }
});

GuaAssertions.WaitForText(ui, "Loading...").ToBeVisible();
```

`Click()` enqueues a click request. A real adapter or `GuaTestHost` must consume
that request and emit the click event while advancing frames.

For real .NET tests, use NUnit, xUnit, or MSTest as the test runner and use
`Gua.Testing` inside each test. The repository's recommended sample is NUnit:

```csharp
using var _ = GuaAssertionScope.UseNUnit(Assert.Fail);

GuaAssertions.GetById(ui, "start").ToBeVisible();
```

See `examples/dotnet-nunit` for a complete NUnit project with multiple `[Test]`
methods in one file.
