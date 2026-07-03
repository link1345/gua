# Gua

**Gua** is a runtime UI automation protocol for games.

It exposes a semantic UI tree from a running game, so test runners and AI agents
can inspect, query, click, and verify in-game UI without relying on fragile image
recognition or coordinate-based input.

```ts
await game.getByRole("button", { name: "Start Game" }).click()
await expect(game.getById("loading")).toBeVisible()
```

Current v0.5 implementation exposes that shape for C++ and C# over the C ABI,
adds Inspector and MCP consumers, and includes a Godot 4.7 C# runtime sample:

```cpp
gua::testing::get_by_role(ctx, "button", "Start Game").click();
gua::testing::wait_for_text(ctx, "Loading...").to_be_visible();
```

```csharp
GuaAssertions.GetByRole(ui, "button", "Start Game").Click();
GuaAssertions.WaitForText(ui, "Loading...").ToBeVisible();
```

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

Gua is not a game engine.
Gua is not an editor MCP.
Gua is not an image-recognition QA bot.

Gua is a small bridge between a game runtime and automation tools.

## Scope

The first implementation focuses on a small, stable core:

- Protocol specification and JSON schemas
- C ABI runtime core
- Thin C++ wrapper
- ImGui adapter
- C++ and C# testing helpers
- .NET P/Invoke binding over the C ABI
- Inspector prototype for tree, node detail, screenshot, and logs
- MCP server prototype
- Godot 4.7 C# sample using the shared native runtime bridge
- Godot 4.7 GDScript sample using the same runtime bridge through GDExtension

Engine-specific integrations such as Unity, Unreal Engine, Godot, and MonoGame
are expected to be adapters built on top of the protocol, not the center of the
project.

## Native Toolchain

Windows native development uses MSVC as the primary toolchain:

```powershell
cmake --preset windows-msvc-debug
cmake --build --preset windows-msvc-debug
```

The portable boundary remains the C ABI. macOS and iOS should use Apple Clang,
and Android should use Android NDK Clang when those targets are added.

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

The bridge speaks the same JSON command shape expected from a future game-side
adapter:

```json
{ "id": 1, "type": "get_ui_tree" }
{ "id": 2, "type": "click_node", "nodeId": "start" }
```

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

The package is shaped for npm publishing as `gui-mcp`. After publishing, MCP
clients can start it with:

```powershell
bunx gui-mcp@latest mcp
```

By default it connects to `ws://127.0.0.1:8765`. Override that for another
runtime adapter:

```powershell
$env:GUA_BRIDGE_URL = "ws://127.0.0.1:8765"
bunx gui-mcp@latest mcp
```

The v0.4 tool surface is:

```text
get_ui_tree
click_node
press_key
wait_for_node
get_screenshot
get_logs
run_test
```

Build the static Inspector:

```powershell
bun run --filter @gua/inspector build
```

The package is also prepared for a Tauri desktop shell:

```powershell
bun run --filter @gua/inspector tauri:dev
```

Tauri requires a Rust toolchain in addition to the JavaScript dependencies.

## Release automation

GitHub Actions publishes runtime artifacts from `main` when the relevant protocol
consumer changes:

- Inspector changes build the Tauri Inspector on Windows and attach the bundle
  outputs to a GitHub Release tagged as `inspector-<short-sha>`.
- MCP changes build and publish `gui-mcp` to npm as
  `0.0.0-main.<run-number>.<short-sha>` with the `latest` dist-tag.

The MCP workflow requires an `NPM_TOKEN` repository secret with permission to
publish `gui-mcp`.

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

The sample uses `Gua.Godot.GuaGodotRuntime` from the addon, starts an Inspector
bridge on `ws://127.0.0.1:8765`, registers a title-screen semantic tree, and
consumes click events to transition to the loading screen. `examples/dotnet-monogame`
is still present as a future placeholder, but Godot is the v0.5 C# engine sample.

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

## Repository Layout

```text
protocol/             Protocol specs and JSON schemas
native/gua-core/      C ABI runtime core and C++ reference implementation
native/gua-runtime/   Shared native runtime bridge for Godot C# and GDScript
native/gua-imgui/     ImGui adapter layer
native/gua-testing/   C++ testing helpers over the C ABI
native/gua-godot/     Godot GDExtension adapter for GDScript
bindings/dotnet/      .NET P/Invoke binding and C# testing helpers
packages/mcp/         MCP server prototype
packages/inspector/   Inspector UI prototype
examples/             Minimal demos and samples, including the Godot C# sample
docs/                 Project planning and design notes
```

## Planning

The project plan is kept in two places:

- `plan.md`: original working plan
- `docs/planning/gua-project-plan.md`: repository planning copy

Keep these aligned when the product direction changes.

## License

MIT
