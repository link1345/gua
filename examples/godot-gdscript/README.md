# Gua GDScript Sample

This sample uses the Gua Godot GDExtension from GDScript.

Build the native extension first:

```powershell
cmake --preset windows-msvc-debug
cmake --build --preset windows-msvc-debug --target gua-godot
```

Then open this directory in the Godot 4.7 editor. The extension exposes
`GuaContext` to GDScript through `addons/gua/gua.gdextension`.

Run the project from Godot. The sample starts the Inspector bridge inside the
running game process on:

```text
ws://127.0.0.1:8765
```

Connect Gua Inspector to that URL while the game is running. If the bridge cannot
listen, Godot prints a `Failed to start Gua Inspector bridge` warning, usually
because another process already owns the port or the native extension was not
rebuilt.

The sample registers a title-screen semantic UI tree, publishes snapshots to the
Inspector, polls Gua events, and transitions to a loading screen when the
Inspector or the in-game UI clicks the `start` node.
