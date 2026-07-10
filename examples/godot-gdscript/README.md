# Gua GDScript Sample

This sample uses the Gua Godot GDExtension from GDScript.

Build the native extension first:

```powershell
cmake --preset windows-msvc-debug
cmake --build --preset windows-msvc-debug --target gua-godot
```

Then open this directory in the Godot 4.7 editor. The extension exposes
`GuaContext` to GDScript through `addons/gua/gua.gdextension`, and
`addons/gua/gua_auto_adapter.gd` provides the standard-Control auto collector.
Game scripts should instantiate the adapter from an explicit preload instead of
depending on Godot's global `class_name` registration order:

```gdscript
const GuaAutoAdapterScript := preload("res://addons/gua/gua_auto_adapter.gd")

var ui := GuaAutoAdapterScript.new()
```

The adapter resolves the native `GuaContext` class through `ClassDB` when it is
first used. If the GDExtension is not loaded, or if the vendored DLL is stale and
does not expose a required method such as `consume_click_request`, the adapter
prints an actionable error and stops before Godot raises a generic invalid-call
error. Rebuild the vendored DLL with:

```powershell
cmake --build --preset windows-msvc-debug --target gua-godot
```

Run the project from Godot. The sample starts the Inspector bridge inside the
running game process on:

```text
ws://127.0.0.1:8765
```

Connect Gua Inspector to that URL while the game is running. If the bridge cannot
listen, Godot prints a `Failed to start Gua Inspector bridge` warning, usually
because another process already owns the port or the native extension was not
rebuilt.

The sample attaches `GuaAutoAdapter` to the root Godot `Control`; the adapter
collects standard controls into the semantic UI tree and dispatches semantic
actions through the real controls. The v1 matrix covers focus, `LineEdit` /
`TextEdit` / slider values, checkbox state, `OptionButton` / `ItemList` /
`TabContainer` selection, `ScrollContainer`, and key input. Every requested host
operation emits an observed result carrying the same `requestId`.

`GuaAutoAdapter.reset_context()` resets the shared runtime context and its
temporary control/request-dispatch caches as one operation. Strict reset first
checks pending/in-flight requests and unconsumed events and changes nothing when
it reports a leak. The default preserves logs and screenshots; all clients of
the same runtime observe the new `sessionEpoch`.

For a headless smoke check of the load-order-safe path:

```powershell
C:\Users\testk\.local\bin\Godot_v4.7-stable_win64_console.exe --headless --path examples/godot-gdscript --script res://scripts/gua_smoke.gd
```
