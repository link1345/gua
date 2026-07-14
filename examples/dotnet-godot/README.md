# Gua Godot Sample

This is an experimental v0.5 C# runtime sample for Godot 4.7. It demonstrates
basic semantic tree reflection, screenshots, and button clicks; it is not
feature-equivalent to the GDScript adapter.

For new integrations, including .NET-enabled Godot projects, use the
`examples/godot-gdscript/addons/gua` addon as the recommended runtime adapter.
Godot projects can use that GDScript addon alongside C# game scripts. Keep this
sample when evaluating an all-C# integration or the lower-level managed runtime
bridge, but do not treat it as the complete Godot adapter.

It uses the C# addon in `addons/gua`, which wraps the shared native
`gua_runtime` bridge. The same native bridge owns the Gua core context and the
Inspector WebSocket server, so the C# sample does not ship a separate managed
bridge implementation.

Build the managed project:

```powershell
dotnet build examples/dotnet-godot/GuaGodotSample.csproj -v:minimal
```

The project build also builds and copies the native `gua_runtime.dll` into the
Godot C# output directory and `addons/gua/bin` on Windows.

Run the project from the .NET-enabled Godot 4.7 editor. The sample starts the
Inspector bridge inside the running game process on:

```text
ws://127.0.0.1:8765
```

Connect Gua Inspector to that URL while the game is running. The sample attaches
the Gua adapter to the root Godot `Control`; the adapter collects standard
labels and buttons into the semantic UI tree, publishes snapshots, observes
button clicks, and dispatches Inspector `click_node` requests through the normal
Godot button signal.
