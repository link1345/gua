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

Async condition waits are the primary synchronization API. They use a monotonic
timeout, honor cancellation, and work with both local `GuaContext` and remote
`GuaRemoteContext` snapshot polling:

```csharp
await GuaAssertions.WaitForVisibleAsync(ui, "status", cancellationToken: token);
await GuaAssertions.WaitForTextAsync(ui, "status", "Ready", pollInterval: TimeSpan.FromMilliseconds(20));
await GuaAssertions.WaitForValueAsync(ui, "progress", "100");
await GuaAssertions.WaitForStableSnapshotAsync(ui, stableFrames: 3);
```

Stable snapshot waiting counts only distinct `frameSequence` values whose
`revision` remains unchanged; repeatedly polling one stopped frame never
satisfies the wait. Hidden waiting succeeds for either `visible=false` or a
removed node. Timeout messages include the condition, last node state, frame,
and revision. Sync wrappers remain available for compatibility.

`WaitForStateAsync(context, id, predicate)` polls fresh snapshots for detailed state such as caret/selection,
scroll offsets, range bounds, and selected index. Action completion includes session/frame/revision metadata,
but remains distinct from observing the requested state; chain a state wait when the UI result matters.

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

## Failure diagnostics

Configure `GuaAssertionOptions.Diagnostics` to capture a unique artifact
directory automatically when a semantic assertion or wait fails:

```csharp
using var scope = GuaAssertionScope.Use(new GuaAssertionOptions
{
    Diagnostics = new GuaDiagnosticOptions
    {
        TestName = TestContext.CurrentContext.Test.FullName,
        OutputDirectory = Path.Combine("artifacts", "gua"),
    },
});
```

The directory contains the final UI tree, bounded operation/event history,
pending requests, logs, environment metadata, and an optional PNG. A wait also
writes its initial tree and a deterministic node-id diff. Sensitive action
values are redacted before the writer receives them. If capture fails, the
original assertion delegate still determines the exception type and a secondary
capture error is appended to its message.

Protocol v2 operations have one-step sync and async completion APIs for focus,
set value, set checked, select, scroll, and key press. They return the correlated
`GuaActionEvent`; `GuaActionException` exposes rejection, host failure, timeout,
or cancellation together with request/action/node/error and snapshot metadata.
`GuaAssertions.PressKeyAsync(context, key)` targets the adapter's current focus.

Queries can add `Within`, `ByValue`, `WhereFocused`, `WhereSelected`,
`WhereChecked`, and `ByAction`. Corresponding async state waits re-fetch the
latest UI tree on every poll instead of holding the first snapshot.
Wait-returned expectations retain the exact successful node snapshot, including
`sessionEpoch`, `frameSequence`, and `revision`, so chained assertions evaluate
one completed frame. Call `Refresh()` or a `WaitUntil*` method to opt into a
newer published frame. A retained snapshot from an older session epoch remains
readable evidence but must be refreshed before making current-session decisions.

Locator counts can wait on every selector dimension, including scope, state,
value, and action:

```csharp
await GuaAssertions.Query(context).ByRole("listitem").Within("ServerList")
    .WaitForCountAsync(count => count >= 3, timeout, pollInterval, cancellationToken);
GuaAssertions.Query(context).ByAction("scroll").WaitForCount(1, timeout, pollInterval);
```

Node expectations expose correlated sync/async action completion for `click`,
`focus`, `set_value`, `set_checked`, `select`, `scroll`, and `press_key`. These
helpers wait for the same `requestId`; unrelated events remain queued.
