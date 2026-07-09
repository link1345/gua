# Gua Godot GDExtension

This target exposes the shared Gua native runtime bridge to Godot 4.7 as a small
GDExtension class usable from GDScript.

Build the adapter:

```powershell
cmake --preset windows-msvc-debug
cmake --build --preset windows-msvc-debug --target gua-godot
```

The debug Windows DLL is emitted into:

```text
examples/godot-gdscript/addons/gua/bin/
```

On Windows, the adapter links `gua-runtime`, which owns both the core Gua context
and the shared WebSocket bridge. A GDScript runtime can call
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
