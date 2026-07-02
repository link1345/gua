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

The GDExtension is intentionally thin. It does not reimplement Gua runtime
behavior and does not turn Gua into a Godot UI framework or editor MCP.
