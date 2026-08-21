# Gua

English | [日本語](README.ja.md)

[![License](https://img.shields.io/github/license/link1345/gua)](https://github.com/link1345/gua/blob/main/LICENSE)
[![Discord](https://img.shields.io/discord/1329272750099136552)](https://discord.gg/Zy65k8AxH2)

> **Playwright-style UI automation for games, with Godot 4.7 and Unity 6
> support plus MCP access for AI coding agents.**

**Gua** is a runtime UI automation protocol for games. It gives automated tests,
inspectors, and AI agents a semantic interface to the UI of a running game.
Instead of relying on fragile image recognition or screen coordinates, they can
find controls by ID, role, text, and state; interact with them; wait for changes;
read logs; capture screenshots; and verify results.

Gua provides runtime integrations for the **Godot 4.7 GDScript addon** and
**Unity 6**. The Unity package automatically reflects UI Toolkit, uGUI, and
TextMeshPro runtime UI on Windows x64 Mono builds. Its MCP server, `gui-mcp`,
makes the same runtime UI available to MCP-enabled AI coding agents for
AI-assisted game development and AI playtesting.

**Build → Run → Inspect → Test → Fix → Test again.**

Think of Gua as a Playwright-style automation layer for game UI: the browser DOM
becomes a Semantic UI Tree, while locators, actions, waits, assertions,
screenshots, and visual comparisons operate against the live game runtime.

## What can I do with Gua?

### Godot UI testing

- Expose standard Godot `Control` nodes as a Semantic UI Tree.
- Find controls by ID, role, text, value, and state instead of coordinates.
- Click, focus, enter values, select options, scroll, and press keys.
- Wait for UI state changes and assert them from regular .NET tests.
- Capture screenshots, compare visual baselines, and retain failure diagnostics.
- Run end-to-end Godot UI tests in CI with [`link1345/gua-tester`](https://github.com/link1345/gua-tester).

### Unity 6 UI testing

- Automatically expose UI Toolkit, uGUI, and TextMeshPro runtime UI.
- Exercise scenes in Editor Play Mode or a Windows x64 Mono Player.
- Locate and operate controls through the same semantic API used for Godot.
- Keep game-side adapter code separate from external NUnit test hosts.
- Capture logs, screenshots, and diagnostics when a Unity test fails.
- Build and test a Windows x64 Mono Player in CI with [`link1345/gua-tester`](https://github.com/link1345/gua-tester).

### AI coding and AI playtesting

`gui-mcp` connects an MCP-enabled AI coding agent to the same bridge used by the
Inspector. The agent can inspect the UI tree, operate semantic controls, wait for
expected state, read game logs, capture screenshots, replay recordings, compare
visuals, and run small test sequences against the game it is helping to build.

Gua complements an AI coding agent: the agent edits the game, while Gua lets it
observe, operate, and verify the running game. Gua does not replace the game
engine or coding agent, and semantic targeting does not depend on image
recognition.

## Godot 4.7 support

The recommended Godot integration is the GDScript addon backed by GDExtension.
It automatically reflects standard `Control` nodes and can be used by both
GDScript projects and .NET-enabled Godot projects. The separate C# runtime sample
is experimental and does not provide the full adapter feature set.

For Godot developers, Gua provides semantic UI automation, external end-to-end
testing, screenshot and visual regression testing, CI testing, Inspector-based
debugging, and MCP-based AI playtesting. See [Godot 4.7 GDScript Addon](#godot-47-gdscript-addon)
for setup details.

## Unity 6 support

The Unity package automatically starts the runtime adapter and reflects UI
Toolkit, uGUI, and TextMeshPro controls without manual semantic-node
registration. `Gua.Testing.Unity` can launch Editor Play Mode or a built Mono
Player from ordinary .NET tests. The first supported scope is Unity 6000.0+ on
Windows x64 with Mono; IL2CPP, non-Windows targets, Unity IMGUI, and EditorWindow
automation are not currently supported. See [Unity 6 Windows x64](#unity-6-windows-x64)
for installation and verification details.

## NuGet Packages

- **Gua.Core:** [![NuGet Version](https://img.shields.io/nuget/v/Gua.Core)](https://www.nuget.org/packages/Gua.Core) ![NuGet Downloads](https://img.shields.io/nuget/dt/Gua.Core)<br>
  P/Invoke bindings for using the Gua C ABI runtime from .NET, including the Windows x64 native runtime.
- **Gua.Testing:** [![NuGet Version](https://img.shields.io/nuget/v/Gua.Testing)](https://www.nuget.org/packages/Gua.Testing) ![NuGet Downloads](https://img.shields.io/nuget/dt/Gua.Testing)<br>
  Adds Gua locators, waits, assertions, and adapter test loops to regular .NET tests.
- **Gua.Testing.Godot:** [![NuGet Version](https://img.shields.io/nuget/v/Gua.Testing.Godot)](https://www.nuget.org/packages/Gua.Testing.Godot) ![NuGet Downloads](https://img.shields.io/nuget/dt/Gua.Testing.Godot)<br>
  Starts a Godot process and provides helpers for controlling and verifying a running scene through the Gua bridge.
- **Gua.Testing.Unity:** [![NuGet Version](https://img.shields.io/nuget/v/Gua.Testing.Unity)](https://www.nuget.org/packages/Gua.Testing.Unity) ![NuGet Downloads](https://img.shields.io/nuget/dt/Gua.Testing.Unity)<br>
  Starts a Unity process and provides helpers for controlling and verifying a running scene through the Gua bridge.
- **Gua.Runtime:** [![NuGet Version](https://img.shields.io/nuget/v/Gua.Runtime)](https://www.nuget.org/packages/Gua.Runtime) ![NuGet Downloads](https://img.shields.io/nuget/dt/Gua.Runtime)<br>
  Shared managed wrapper for authors of engine adapters. Use it to publish semantic frames, consume actions, complete screenshot requests, and host the Inspector bridge without duplicating P/Invoke code. Application test projects normally use an engine package instead.
- **Gua.Testing.Visual:** [![NuGet Version](https://img.shields.io/nuget/v/Gua.Testing.Visual)](https://www.nuget.org/packages/Gua.Testing.Visual) ![NuGet Downloads](https://img.shields.io/nuget/dt/Gua.Testing.Visual)<br>
  Adds opt-in PNG baseline comparison for rendering regressions that semantic assertions cannot detect, such as clipping, misplaced controls, incorrect assets, and unexpected overlays. Failures retain expected, actual, diff, and machine-readable comparison artifacts.
  `gua-tester` can combine those artifacts with its prebuilt Astro viewer for workflow artifacts and GitHub Pages.
- **Gua.Testing.Recording:** [![NuGet Version](https://img.shields.io/nuget/v/Gua.Testing.Recording)](https://www.nuget.org/packages/Gua.Testing.Recording) ![NuGet Downloads](https://img.shields.io/nuget/dt/Gua.Testing.Recording)<br>
  Records repeatable user journeys as semantic operations and replays every step with correlated host completion. Use it for regression flows, bug reproduction, and sharing a scenario without storing fragile coordinates or plaintext secrets.

See the [.NET package guide](https://gua.orizika.com/en/docs/dotnet-packages/)
for package selection, then use the dedicated guides for
[Gua.Runtime](https://gua.orizika.com/en/docs/gua-runtime/),
[Visual testing](https://gua.orizika.com/en/visual-testing/), and
[Recording](https://gua.orizika.com/en/recording/) for complete
workflows and diagrams.

## MCP and Inspector

- **gui-mcp:** [![NPM Version](https://img.shields.io/npm/v/gui-mcp)](https://www.npmjs.com/package/gui-mcp) ![NPM Downloads](https://img.shields.io/npm/dw/gui-mcp)<br>
  A thin MCP server that exposes Gua runtime actions to AI agents through the
  same WebSocket bridge used by the Inspector.
- **Gua Inspector:** [![Gua Release](https://img.shields.io/github/actions/workflow/status/link1345/gua/gua-release.yml?branch=main&label=Gua%20Release)](https://github.com/link1345/gua/actions/workflows/gua-release.yml)<br>
  A browser and Windows desktop UI for inspecting the semantic UI tree, node
  state, screenshots, and logs, and for sending runtime commands.

```ts
await game.getByRole("button", { name: "Start Game" }).click()
await expect(game.getById("loading")).toBeVisible()
```

The current implementation exposes that shape for C++ and C# over the C ABI,
adds Inspector and MCP consumers, and includes Godot 4.7 and Unity 6 adapters
and samples:

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
versioned workflows for both Godot and Unity. The Godot action downloads Godot,
links the released addon, sets `GODOT_EXECUTABLE`, and runs the external .NET
tests:

For a typical consumer repository, the workflow can be as small as:

```yaml
- uses: link1345/gua-tester/godot@v2.1
  with:
    project-path: game
    test-project: tests/GuaTester.Tests.csproj
    godot-version: "4.7"
    godot-status: stable
```

The Unity reusable workflow installs the released UPM package, builds a Windows
x64 Mono Player on Linux, transfers it to a Windows runner, and runs the
`Gua.Testing.Unity` NUnit project:

```yaml
jobs:
  unity:
    if: github.event_name != 'pull_request' || github.event.pull_request.head.repo.full_name == github.repository
    uses: link1345/gua-tester/.github/workflows/unity.yml@v2.1
    with:
      project-path: game
      scene-path: Assets/Scenes/Title.unity
      test-project: tests/GuaTester.Unity.Tests.csproj
      artifact-key: game
      unity-version: auto
      gua-tag: gua-v0.15.0
    secrets:
      UNITY_EMAIL: ${{ secrets.UNITY_EMAIL }}
      UNITY_PASSWORD: ${{ secrets.UNITY_PASSWORD }}
      UNITY_LICENSE: ${{ secrets.UNITY_LICENSE }}
      UNITY_SERIAL: ${{ secrets.UNITY_SERIAL }}
```

Keep the UPM release selected by `gua-tag` aligned with the
`Gua.Testing.Unity` NuGet version. Unity credentials are unavailable to fork
pull requests, so skip the Unity job for untrusted forks. The reusable workflow
fixes the Windows test runner display at 1920x1080 before launching the Player.

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

Gua is the runtime UI layer between a game and its automation tools. It works
alongside game engines, editor tools, test runners, and AI coding agents rather
than replacing them.

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
- Experimental Godot 4.7 C# sample for basic tree reflection and button clicks
  over the shared native runtime bridge
- Recommended Godot 4.7 GDScript addon using the same runtime bridge through
  GDExtension, including the full standard-Control adapter
- Unity 6 runtime package for UI Toolkit, uGUI, and TextMeshPro, plus external
  Editor Play Mode and Windows x64 Mono Player test hosts

Engine-specific integrations remain adapters built on top of the protocol, not
the center of the project. Godot and Unity are the current supported adapters;
additional engines can follow the same boundary.

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
dotnet pack bindings/dotnet/src/Gua.Runtime/Gua.Runtime.csproj --configuration Release
dotnet pack bindings/dotnet/src/Gua.Testing.Unity/Gua.Testing.Unity.csproj --configuration Release
dotnet pack bindings/dotnet/src/Gua.Testing.Godot/Gua.Testing.Godot.csproj --configuration Release
dotnet pack bindings/dotnet/src/Gua.Testing.Visual/Gua.Testing.Visual.csproj --configuration Release
dotnet pack bindings/dotnet/src/Gua.Testing.Recording/Gua.Testing.Recording.csproj --configuration Release
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

### Unity 6 Windows x64

`Gua.Core`, `Gua.Testing`, `Gua.Testing.Visual`, and
`Gua.Testing.Recording` target both `net10.0` and `netstandard2.1`.
Unity 6 projects using the default **.NET Standard 2.1** API Compatibility
Level can load the managed assemblies without changing the native C ABI.
The first verified native configuration is Windows Editor x64: place the
managed assemblies and their NuGet dependency closure under
`Assets/Plugins/Gua/Managed`, and place `gua.dll` under
`Assets/Plugins/x86_64`. Unity's Plugin Import Settings must enable the DLL for
the Windows Editor and Windows Standalone x86_64 targets.

Build the precompiled Unity Package Manager artifact and `.tgz` archive with:

```powershell
.\scripts\build-unity-package.ps1
```

Each automated Gua GitHub Release also attaches
`com.link1345.gua-<version>.tgz`. Download that file and install it from
Unity Package Manager with **Add package from tarball**. Release builds require
the `UNITY_LICENSE`, `UNITY_EMAIL`, and `UNITY_PASSWORD` secrets in the GitHub
`release` environment so the precompiled Unity assemblies are produced by the
same release workflow as the Windows native plugins.

The package starts automatically and reflects UI Toolkit, uGUI, and
TextMeshPro runtime UI. `GuaId` and `GuaScreen` are optional overrides; game
code does not register semantic nodes manually. `Gua.Testing.Unity` provides
Editor Play Mode and Mono Windows Player hosts. See
[`examples/unity-smoke`](examples/unity-smoke/README.md) for the verified Unity
6000.5.3f1 fixture. Windows x64, Unity 6000.0+, and Mono are supported in this
initial release; IL2CPP, other operating systems, IMGUI, and EditorWindow UI
automation are not.

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

The Inspector Automation panel can record semantic actions issued from the UI,
download or import a `recording.schema.json` document, replay every semantic
action, and supply sensitive replay values from an in-memory JSON map. Its Visual
comparison controls accept the current screenshot or a selected image as a
baseline, compare in the browser, and download Actual/Expected/Diff images plus a
machine-readable manifest. Browser Inspector files are explicit downloads; it
does not silently write arbitrary local paths.
Coordinate fallback recordings are accepted as schema v1 documents but are not
executed by the Inspector; replay remains semantic-target-only by default.

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
focus_node
set_value
set_checked
select
scroll
press_key
wait_for_node
get_screenshot
get_logs
start_recording
stop_recording
save_recording
replay_recording
compare_screenshot
get_visual_artifacts
run_test
```

Recording, baseline, and visual failure files default to `.gua`. Set
`GUA_ARTIFACT_DIR` to choose a different root. Names supplied to MCP tools cannot
escape that root. Semantic action tools wait for request-ID-correlated host
completion when the connected bridge supports it.

## Release automation

GitHub Actions publishes runtime artifacts from `main` when a related protocol
consumer changes. The Inspector, Godot plugin, and ImGui plugin are built
together and attached to one versioned `gua-v*` GitHub Release. MCP and .NET
packages continue to use their independent npm and NuGet publishing workflows.

The MCP workflow uses npm trusted publishing through GitHub Actions OIDC. The
`gui-mcp` package must be configured on npm with this repository, the
`.github/workflows/mcp-publish.yml` workflow, and the `release` environment.

## Godot 4.7 C# Sample

> **Experimental — basic functionality only.** This sample demonstrates the
> shared runtime bridge, basic semantic tree reflection, screenshots, and button
> clicks. It is not feature-equivalent to the GDScript adapter. For new Godot
> integrations, including .NET-enabled projects, use the GDScript addon below;
> Godot projects can use it alongside C# game scripts.

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

This is the recommended Godot integration for both GDScript projects and
.NET-enabled projects. Its standard-Control adapter implements the complete
Godot feature set currently documented by Gua.

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
bindings/dotnet/src/Gua.Testing.Visual/
                      Opt-in PNG baseline comparison helpers
bindings/dotnet/src/Gua.Testing.Recording/
                      Semantic operation recording and correlated replay
packages/mcp/         Published MCP server package
packages/inspector/   Browser and Tauri desktop Inspector UI
examples/             Minimal demos and samples, including the Godot C# sample
docs/                 Native toolchain guidance
```

## License

MIT
