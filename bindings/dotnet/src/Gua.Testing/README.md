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

Use `GuaTestSession` as the explicit process-reuse boundary. The default reset
clears semantic nodes, requests, events, and retained history while preserving
logs and screenshots. Strict teardown detects leaked requests/events without
discarding them:

```csharp
var session = new GuaTestSession(context);
session.Reset(); // setup; starts a new session epoch
// ...test...
session.Reset(new GuaResetOptions(Strict: true)); // teardown; throws if dirty
```

`ResetAsync` provides the same contract for remote contexts and honors
`CancellationToken`. Remote reset always includes the inspected session epoch,
so a stale client cannot reset a newer shared runtime session.
methods in one file.
Semantic locators are strict: `GetBy*` fails when zero or multiple nodes match,
while `QueryAll()` is the explicit multi-result API. String matching is exact by
default and can opt into ordinal contains or ECMAScript regex matching:

```csharp
var save = GuaAssertions.Query(ui)
    .ByRole("button")
    .ByText("^保存", GuaMatchMode.Regex)
    .Within("settings-panel")
    .WhereVisible()
    .WhereEnabled()
    .Get();

GuaAssertions.Query(ui).ByRole("listitem").Within("servers").AssertCount(3);
```

`Within(parentId)` searches descendants and excludes the parent itself. Pass
`directChild: true` to limit the query to immediate children. Local and Godot
remote contexts send the same selector to the native evaluator.

Node expectations expose `Focus`, `SetValue`, `SetChecked`, `Select`, `Scroll`,
and `PressKey`. These methods enqueue requests and return a request ID;
`WaitForAction` waits for the adapter's correlated observed result rather than
treating enqueue acceptance as completion.
