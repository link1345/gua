# Gua

**Gua** is a runtime UI automation protocol for games.

It exposes a semantic UI tree from a running game, so test runners and AI agents
can inspect, query, click, and verify in-game UI without relying on fragile image
recognition or coordinate-based input.

```ts
await game.getByRole("button", { name: "Start Game" }).click()
await expect(game.getById("loading")).toBeVisible()
```

Current v0.3 implementation exposes that shape for C++ and C# over the C ABI
and adds Inspector payloads for UI tree, logs, and screenshots:

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
MCP, saved files, or a future native bridge.

Run the browser version:

```powershell
bun run --filter @gua/inspector dev
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

## Repository Layout

```text
protocol/             Protocol specs and JSON schemas
native/gua-core/      C ABI runtime core and C++ reference implementation
native/gua-imgui/     ImGui adapter layer
native/gua-testing/   C++ testing helpers over the C ABI
bindings/dotnet/      .NET P/Invoke binding and C# testing helpers
packages/mcp/         MCP server prototype
packages/inspector/   Inspector UI prototype
examples/             Minimal demos and samples
docs/                 Project planning and design notes
```

## Planning

The project plan is kept in two places:

- `plan.md`: original working plan
- `docs/planning/gua-project-plan.md`: repository planning copy

Keep these aligned when the product direction changes.

## License

MIT
