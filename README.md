# Gua

English | [日本語](README.ja.md)

[![License](https://img.shields.io/github/license/link1345/gua)](https://github.com/link1345/gua/blob/main/LICENSE)
[![Discord](https://img.shields.io/discord/1329272750099136552)](https://discord.gg/Zy65k8AxH2)

**Gua** is a runtime UI automation protocol for games.

It exposes a semantic UI tree from a running game, so test runners and AI agents
can inspect, query, click, and verify in-game UI without relying on fragile image
recognition or coordinate-based input.

## NuGet Packages

- **Gua.Core:** [![NuGet Version](https://img.shields.io/nuget/v/Gua.Core)](https://www.nuget.org/packages/Gua.Core) ![NuGet Downloads](https://img.shields.io/nuget/dt/Gua.Core)<br>
  P/Invoke bindings for using the Gua C ABI runtime from .NET, including the
  Windows x64 native runtime.
- **Gua.Testing:** [![NuGet Version](https://img.shields.io/nuget/v/Gua.Testing)](https://www.nuget.org/packages/Gua.Testing) ![NuGet Downloads](https://img.shields.io/nuget/dt/Gua.Testing)<br>
  Adds Gua locators, waits, assertions, and adapter test loops to regular .NET
  tests.
- **Gua.Testing.Godot:** [![NuGet Version](https://img.shields.io/nuget/v/Gua.Testing.Godot)](https://www.nuget.org/packages/Gua.Testing.Godot) ![NuGet Downloads](https://img.shields.io/nuget/dt/Gua.Testing.Godot)<br>
  Starts a Godot process and provides helpers for controlling and verifying a
  running scene through the Gua bridge.

## MCP and Inspector

- **gui-mcp:** [![NPM Version](https://img.shields.io/npm/v/gui-mcp)](https://www.npmjs.com/package/gui-mcp) ![NPM Downloads](https://img.shields.io/npm/dw/gui-mcp)<br>
  A thin MCP server that exposes Gua runtime actions to AI agents through the
  same WebSocket bridge used by the Inspector.
- **Gua Inspector:** [![Inspector Release](https://img.shields.io/github/actions/workflow/status/link1345/gua/inspector-release.yml?branch=main&label=Inspector%20Release)](https://github.com/link1345/gua/actions/workflows/inspector-release.yml)<br>
  A browser and Windows desktop UI for inspecting the semantic UI tree, node
  state, screenshots, and logs, and for sending runtime commands.

```ts
await game.getByRole("button", { name: "Start Game" }).click()
await expect(game.getById("loading")).toBeVisible()
```

The current implementation exposes that shape for C++ and C# over the C ABI,
adds Inspector and MCP consumers, and includes Godot 4.7 adapters and samples:

```cpp
gua::testing::get_by_role(ctx, "button", "Start Game").click();
gua::testing::wait_for_text(ctx, "Loading...").to_be_visible();
```

```csharp
GuaAssertions.GetByRole(ui, "button", "Start Game").Click();
GuaAssertions.WaitForText(ui, "Loading...").ToBeVisible();
```

`Click()` intentionally enqueues a click request; it does not directly mutate
the game. A game adapter, such as the ImGui or Godot adapter, must consume the
request on a later frame and emit the observed click event. Tests that do not
run a real engine adapter can use `GuaTestHost` for the same loop:

```csharp
using var ui = new GuaContext();
var host = new GuaTestHost(ui);
var loading = false;

host.Frame("title", frame =>
{
    frame.Button("start", "Start Game", new GuaBounds(100, 100, 240, 64));
});

GuaAssertions.GetByRole(ui, "button", "Start Game").Click();

host.Frame("title", frame =>
{
    frame.Button("start", "Start Game", new GuaBounds(100, 100, 240, 64));
});

host.DrainClickEvents(id => loading = id == "start");

host.Frame("loading", frame =>
{
    if (loading)
    {
        frame.Text("loading", "Loading...", new GuaBounds(100, 180, 240, 24));
    }
});

GuaAssertions.WaitForText(ui, "Loading...").ToBeVisible();
```

Real .NET tests should use an existing test runner such as NUnit. `Gua.Testing`
does not try to become a Vitest-style runner; it provides Gua-specific locators,
waits, assertions, and adapter test loops inside normal NUnit tests. One C# file
can contain multiple `[Test]` methods:

```csharp
using Gua.Core;
using Gua.Testing;
using NUnit.Framework;

[TestFixture]
public sealed class TitleScreenTests
{
    [Test]
    public void StartClickShowsLoadingText()
    {
        using var _ = GuaAssertionScope.UseNUnit(Assert.Fail);
        using var ui = new GuaContext();
        var host = new GuaTestHost(ui);

        host.Frame("title", frame =>
        {
            frame.Button("start", "Start Game", new GuaBounds(100, 100, 240, 64));
        });

        GuaAssertions.GetByRole(ui, "button", "Start Game").Click();
    }
}
```

The repository includes a runnable NUnit sample in `examples/dotnet-nunit`.

Godot scene tests can use the `Gua.Testing.Godot` package to start a Godot
process and assert against the live Gua bridge:

```csharp
using var host = GodotSceneTestHost.Load("game/scenes/title_screen.tscn");

GuaAssertions.GetByRole(host.Context, "button", "開始").ToBeVisible();
host.Click("CenterPanel/Content/ButtonBox/StartButton", nextScene: "game/scenes/village_list.tscn");
GuaAssertions.GetByRole(host.Context, "button", "Create").ToBeVisible();
```

For v0.2.0 and later, repository-local `GuaLiveAssertions` helpers can migrate
directly to `WaitForId` / `WaitForText` / `WaitForVisible` / `WaitForEnabled`,
locator `WaitForCount*`, and correlated node actions such as `ClickAsync`.
Wait-returned expectations retain the successful completed-frame snapshot;
use `Refresh()` or `WaitUntil*` when a later frame is required. Repository
`GodotHostFactory` wrappers should keep only game-specific process and fixture
policy while `GodotSceneTestHost` owns executable/project discovery, automatic
loopback ports, rendered/headless startup, bridge diagnostics, and reset policy.
All APIs are additive over the existing C ABI and WebSocket protocol.

Failure diagnostics can be coordinated through the framework-independent
`GuaDiagnosticsSession`. Assertions and completed actions use the same artifact
layout, return typed absolute paths/media types/secondary capture errors, and
can attach through a callback. Godot sessions can include live process metadata,
stdout/stderr, runtime version, and an opt-in on-demand screenshot; successful
tests do not create artifacts unless capture is explicitly requested.

Want to try that in GitHub Actions without wiring every setup step by hand?
[`link1345/gua-tester`](https://github.com/link1345/gua-tester) provides
reusable Actions for Godot GDScript projects. It downloads Godot on the runner,
links the released Gua Godot addon into your project, sets `GODOT_EXECUTABLE`,
and runs your .NET tests that use `Gua.Testing.Godot`.

For a typical consumer repository, the workflow can be as small as:

```yaml
- uses: link1345/gua-tester@v1
  with:
    project-path: game
    test-project: tests/GuaTester.Tests.csproj
    godot-version: "4.7"
    godot-status: stable
```

If you have a Godot project that starts the Gua bridge, give it a spin in CI and
see whether the semantic UI tree catches regressions before they land.

```cpp
context.log(gua::LogLevel::info, "title screen opened");
context.set_screenshot("data:image/png;base64,...", 1280, 720);

std::cout << context.ui_tree_json() << '\n';
std::cout << context.logs_json() << '\n';
std::cout << context.screenshot_json() << '\n';
```

```csharp
while (ui.TryPollEvent(out var e))
{
    if (e.Type == GuaEventType.Click && e.NodeId == "start")
    {
        ShowLoading();
    }
}
```

* Gua is not a game engine.
* Gua is not an editor MCP.
* Gua is not an image-recognition QA bot.

Gua is a small bridge between a game runtime and automation tools.

## Scope

The first implementation focuses on a small, stable core:

- Protocol specification and JSON schemas
- C ABI runtime core
- Thin C++ wrapper
- ImGui adapter
- C++ and C# testing helpers
- .NET P/Invoke binding over the C ABI
- Inspector for tree, node detail, screenshot, logs, and runtime commands
- MCP server for AI-agent access to the runtime bridge
- Godot 4.7 C# sample using the shared native runtime bridge
- Godot 4.7 GDScript sample using the same runtime bridge through GDExtension

Engine-specific integrations such as Unity, Unreal Engine, Godot, and MonoGame
are expected to be adapters built on top of the protocol, not the center of the
project.

## Native Toolchain

Runtime compatibility can be inspected through the additive C ABI
`gua_copy_version_json`, the WebSocket `get_version` command, or .NET
`GetVersion()`. The response follows `protocol/schema/version.schema.json` and
lists stable capability IDs. Non-Godot runtimes report `godotPluginVersion` as
`null`; release workflows inject the release version and commit build ID.

Windows native development uses MSVC as the primary toolchain:

```powershell
cmake --preset windows-msvc-debug
cmake --build --preset windows-msvc-debug
```

The portable boundary remains the C ABI. macOS and iOS should use Apple Clang,
and Android should use Android NDK Clang when those targets are added.

## .NET Testing

The .NET packages are published on NuGet and can also be packed locally:

```powershell
dotnet pack bindings/dotnet/src/Gua.Core/Gua.Core.csproj --configuration Release
dotnet pack bindings/dotnet/src/Gua.Testing/Gua.Testing.csproj --configuration Release
```

The packages are written to `artifacts/packages`. `Gua.Testing` declares a NuGet
dependency on the matching `Gua.Core` package, so a test project only needs the
testing package:

```xml
<PackageReference Include="Gua.Testing" Version="0.5.0-preview.3" />
```

`Gua.Core` is also delivered as a NuGet package and `Gua.Testing` depends on the
matching version. The Windows x64 native runtime is included in `Gua.Core` under
`runtimes/win-x64/native/gua.dll`, so a normal package restore/build copies it to
the consuming app or test output. `GUA_NATIVE_DIR` remains as an override for
locally built native runtimes. A missing or wrong architecture native library is
reported with the exact checked paths.

Run the standalone testing sample:

```powershell
cmake --preset windows-msvc-debug
cmake --build --preset windows-msvc-debug --target gua
$env:GUA_NATIVE_DIR = "$PWD\build\windows-msvc-debug\native\gua-core\Debug"
dotnet run --project examples/dotnet-testing/GuaDotNetTestingSample.csproj
```

Run the NUnit testing sample:

```powershell
cmake --preset windows-msvc-release
cmake --build --preset windows-msvc-release --target gua
dotnet pack bindings/dotnet/src/Gua.Core/Gua.Core.csproj --configuration Release
dotnet pack bindings/dotnet/src/Gua.Testing/Gua.Testing.csproj --configuration Release
dotnet test examples/dotnet-nunit/GuaDotNetNUnitSample.csproj
```

## Inspector

The Inspector is a React application that consumes Gua protocol snapshots. It is
not tied to MCP. The UI talks to a `GuaInspectorClient` abstraction so transport
implementations can be added for mock data, WebSocket bridges, HTTP bridges,
MCP, saved files, or the native runtime bridge.

Run the browser version:

```powershell
bun run --filter @gua/inspector dev
```

Run the sample WebSocket bridge in another terminal:

```powershell
bun run bridge:ws
```

Or build and run the native C++ bridge that serves a real `gua::Context`:

```powershell
cmake --preset windows-msvc-debug
cmake --build --preset windows-msvc-debug --target gua-native-bridge-example
.\build\windows-msvc-debug\examples\native-bridge\Debug\gua-native-bridge-example.exe
```

The ImGui example also hosts the same WebSocket bridge while the UI is running:

```powershell
cmake --preset windows-msvc-debug
cmake --build --preset windows-msvc-debug --target gua-cpp-imgui-example
.\build\windows-msvc-debug\examples\cpp-imgui\Debug\gua-cpp-imgui-example.exe
```

Then connect the Inspector to:

```text
ws://127.0.0.1:8765
```

The ImGui bridge pushes snapshot notifications while the UI is running, so the
Inspector updates without polling. The `Poll` toggle in the Inspector remains as
a fallback for bridges that only implement request/response.

The bridge speaks the same JSON command shape used by Gua runtime adapters:

```json
{ "id": 1, "type": "get_ui_tree" }
{ "id": 2, "type": "click_node", "nodeId": "start" }
```

Build the static Inspector:

```powershell
bun run --filter @gua/inspector build
```

Run the Tauri desktop shell during development:

```powershell
bun run --filter @gua/inspector tauri:dev
```

Tauri requires a Rust toolchain in addition to the JavaScript dependencies.

## MCP

The MCP server is a thin protocol consumer over the same bridge used by the
Inspector. Start a runtime bridge first:

```powershell
bun run bridge:ws
```

Then run the MCP server over stdio:

```powershell
bun run mcp
```

The MCP server is published to npm as `gui-mcp`. MCP clients can start it with:

```powershell
bunx gui-mcp@latest mcp
```

By default it connects to `ws://127.0.0.1:8765`. Override that for another
runtime adapter:

```powershell
$env:GUA_BRIDGE_URL = "ws://127.0.0.1:8765"
bunx gui-mcp@latest mcp
```

The MCP tool surface is:

```text
get_ui_tree
click_node
press_key
wait_for_node
get_screenshot
get_logs
run_test
```

## Release automation

GitHub Actions publishes runtime artifacts from `main` when the relevant protocol
consumer changes:

- Inspector changes build the Tauri Inspector on Windows and attach the bundle
  outputs to a GitHub Release tagged as `inspector-<short-sha>`.
- Godot GDScript addon changes build `gua-godot` on Windows and attach a zipped
  `addons/gua` plugin, including the built GDExtension DLLs, to a GitHub Release
  tagged as `godot-plugin-<short-sha>`.
- MCP changes build and publish `gui-mcp` to npm as
  `0.0.0-main.<run-number>.<short-sha>` with the `latest` dist-tag.

The MCP workflow uses npm trusted publishing through GitHub Actions OIDC. The
`gui-mcp` package must be configured on npm with this repository, the
`.github/workflows/mcp-publish.yml` workflow, and the `release` environment.

## Godot 4.7 C# Sample

The v0.5 C# runtime sample lives in `examples/dotnet-godot`. It is a minimal
Godot 4.7 project using `Godot.NET.Sdk/4.7.0`, `net10.0`, project references to
`Gua.Core` and `Gua.Testing`, and a runtime addon in `addons/gua`. The addon
P/Invokes `gua_runtime.dll`, which owns both the Gua context and the Inspector
WebSocket bridge.

Build it with:

```powershell
dotnet build examples/dotnet-godot/GuaGodotSample.csproj -v:minimal
```

The sample uses `Gua.Godot.GuaGodotRuntime` from the addon, attaches it to the
root Godot `Control`, and starts an Inspector bridge on `ws://127.0.0.1:8765`.
The adapter collects standard controls into the semantic tree and dispatches
external click requests through normal Godot button signals.
`examples/dotnet-monogame` is still present as a future placeholder.

## Godot 4.7 GDScript Addon

The GDScript-facing adapter lives in `native/gua-godot` and builds a thin
GDExtension wrapper over `native/gua-runtime`. It exposes `GuaContext` to
GDScript without reimplementing the runtime core or the Inspector bridge:

```gdscript
var ui := GuaContext.new()
ui.begin_frame("title")
ui.register_node("start", "button", "Start Game", Rect2(512, 312, 256, 56), true, true)
ui.end_frame()
ui.start_inspector_bridge(8765)
ui.enqueue_click("start")

while true:
    var event := ui.poll_event()
    if event.is_empty():
        break
```

Build the Windows debug GDExtension:

```powershell
cmake --preset windows-msvc-debug
cmake --build --preset windows-msvc-debug --target gua-godot
```

The build writes the debug DLL into `examples/godot-gdscript/addons/gua/bin`.
Open `examples/godot-gdscript` with Godot 4.7 and run the project. The running
game process starts an Inspector bridge on `ws://127.0.0.1:8765`, which Gua
Inspector can connect to while the game is open. The addon includes `plugin.cfg`
only for standard Godot addon packaging; Gua's runtime API is provided by
`gua.gdextension`, not by an editor MCP.

For game scripts, instantiate the GDScript adapter through an explicit preload
instead of relying on `class_name` registration order:

```gdscript
const GuaAutoAdapterScript := preload("res://addons/gua/gua_auto_adapter.gd")

var ui := GuaAutoAdapterScript.new()
```

The adapter resolves the native `GuaContext` class through `ClassDB` on first
use and verifies that required methods such as `consume_click_request` exist
before dispatching Inspector click requests. If that check fails, rebuild
`gua-godot`; the stale vendored DLL is the problem, not the game script.

Run the GDScript smoke check with:

```powershell
C:\Users\testk\.local\bin\Godot_v4.7-stable_win64_console.exe --headless --path examples/godot-gdscript --script res://scripts/gua_smoke.gd
```

## Repository Layout

```text
protocol/             Protocol specs and JSON schemas
native/gua-core/      C ABI runtime core and C++ reference implementation
native/gua-runtime/   Shared native runtime bridge for Godot C# and GDScript
native/gua-imgui/     ImGui adapter layer
native/gua-testing/   C++ testing helpers over the C ABI
native/gua-godot/     Godot GDExtension adapter for GDScript
bindings/dotnet/      .NET P/Invoke binding and C# testing helpers
bindings/dotnet/src/Gua.Testing.Godot/
                      Godot process test helpers over Gua.Testing
packages/mcp/         Published MCP server package
packages/inspector/   Browser and Tauri desktop Inspector UI
examples/             Minimal demos and samples, including the Godot C# sample
docs/                 Native toolchain guidance
```

## License

MIT
