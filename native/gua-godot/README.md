# Gua Godot GDExtension

This target exposes the shared Gua native runtime bridge to Godot 4.7 as a small
GDExtension class usable from GDScript.

Build both Windows configurations in separate configured build trees:

```powershell
cmake --preset windows-msvc-debug
cmake --build --preset windows-msvc-debug --target gua-godot
cmake --preset windows-msvc-release
cmake --build --preset windows-msvc-release --target gua-godot
```

The Debug and Release Windows DLLs are emitted into:

```text
examples/godot-gdscript/addons/gua/bin/
```

On Windows, the adapter statically embeds the `gua-runtime` implementation,
which owns both the core Gua context and the WebSocket bridge. This lets the
Debug and Release GDExtensions coexist in one addon without a shared
configuration-specific runtime DLL. A GDScript runtime can call
`start_inspector_bridge(8765)` on `GuaContext` so Gua Inspector can connect to
the running Godot game at `ws://127.0.0.1:8765`.

The GDScript addon should be loaded from scripts with an explicit preload:

```gdscript
const GuaAutoAdapterScript := preload("res://addons/gua/gua_auto_adapter.gd")

var ui := GuaAutoAdapterScript.new()
```

`gua_auto_adapter.gd` resolves the native `GuaContext` class through `ClassDB`
instead of calling `GuaContext.new()` at script load time. It also checks the
required GDExtension method surface before dispatching click requests. If a game
reports that `consume_click_request` does not exist, the vendored DLL is stale;
rebuild this target and use the DLL emitted into `examples/godot-gdscript/addons/gua/bin`.

The GDExtension is intentionally thin. It does not reimplement Gua runtime
behavior and does not turn Gua into a Godot UI framework or editor MCP.

`get_context_status` and `reset_context` expose the shared C ABI isolation
contract. Use the higher-level auto adapter when control lookup and suppressed
click caches must be cleared together with the runtime context.
