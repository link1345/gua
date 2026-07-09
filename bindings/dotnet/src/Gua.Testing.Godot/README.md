# Gua.Testing.Godot

`Gua.Testing.Godot` provides a small Godot process test host over `Gua.Core` and
`Gua.Testing`. It starts a Godot project, connects to the Gua WebSocket bridge,
and lets tests assert against the live semantic UI tree.

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
