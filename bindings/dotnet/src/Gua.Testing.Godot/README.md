# Gua.Testing.Godot

`Gua.Testing.Godot` provides a small Godot process test host over `Gua.Core` and
`Gua.Testing`. It starts a Godot project, connects to the Gua WebSocket bridge,
and lets tests assert against the live semantic UI tree.

Godot visual capture is opt-in. After the adapter publishes a viewport PNG,
`GodotSceneTestHost.WaitForScreenshotAsync` waits for that existing protocol
payload. Add `Gua.Testing.Visual` only in visual test projects; the base Godot
testing package deliberately has no PNG codec dependency.

```csharp
using Gua.Testing;
using Gua.Testing.Godot;

using var host = GodotSceneTestHost.Load("game/scenes/title_screen.tscn");

GuaAssertions.GetByRole(host.Context, "button", "開始").ToBeVisible();

host.Click("CenterPanel/Content/ButtonBox/StartButton", nextScene: "game/scenes/village_list.tscn");

GuaAssertions.GetByRole(host.Context, "button", "Create").ToBeVisible();
```

Set `GODOT_EXECUTABLE` or pass `GodotSceneTestHostOptions.GodotExecutablePath`
when Godot is not on `PATH`. The running game must start a Gua bridge, normally
at `ws://127.0.0.1:8765`.

When `ProjectPath` is omitted, the host walks up from the scene file to the
nearest `project.godot` and uses that directory as Godot's `--path`. Pass
`ProjectPath` explicitly when loading a `res://` scene path.

`nextScene` may be a logical Gua screen name such as `loading` or a Godot scene
path such as `game/scenes/village_list.tscn`. Scene paths are matched against
common logical screen names derived from the file name, and also accept a screen
change from the previous value because Godot adapters often publish logical
screen names instead of resource paths.

For failure artifacts, `host.CreateDiagnosticOptions(testName)` includes the
Godot process id, bridge URL, and project metadata. The remote context captures
the same versioned diagnostics JSON as an in-process context.

## Protocol v2 form testing

Typed operations wait for the correlated action result, not merely enqueue
acceptance. Request-id-specific polling leaves unrelated results and ordinary
events queued. Async methods accept timeout, poll interval, and cancellation.

```csharp
using var host = GodotSceneTestHost.Load("res://Main.tscn", new GodotSceneTestHostOptions
{
    ProjectPath = projectPath,
    StartupReset = new GuaResetOptions(Strict: true), // opt-in
});

var userName = GuaAssertions.Query(host.Context)
    .ByRole("textbox").Within("LoginForm").ByAction("set_value").Get();
await userName.SetValueAsync("alice");
await userName.FocusAsync();
await GuaAssertions.PressKeyAsync(host.Context, "Enter"); // current focus
await GuaAssertions.GetById(host.Context, "ProviderOption").SelectAsync("google");
await GuaAssertions.GetById(host.Context, "RememberMe").SetCheckedAsync(true);
await GuaAssertions.GetById(host.Context, "ManagementContent").ScrollAsync(0, 500);
await userName.WaitForValueAsync("alice");
await userName.WaitUntilFocusedAsync();

var saved = host.SaveScreenshot(TestContext.CurrentContext.WorkDirectory,
    TestContext.CurrentContext.Test.Name);
TestContext.AddTestAttachment(saved.AbsolutePath, $"{saved.Width}x{saved.Height}");
```

Use `SetValueAsync(value, sensitive: true)` for secrets. Plaintext is redacted
from action results, logs, diagnostics, and snapshots for adapter-marked
sensitive controls. A screenshot can still contain a rendered secret: saving,
attaching, retaining, and publishing PNG files is the caller's responsibility.
`SaveScreenshot` creates a collision-resistant absolute path and distinguishes
unpublished, malformed data-URI, and invalid-PNG payloads. A headless renderer
without viewport capture is reported as unpublished rather than an empty file.
