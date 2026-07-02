# Gua C# Godot Addon

This addon exposes a small Godot-friendly C# runtime wrapper over the shared
native `gua_runtime` bridge.

Use `Gua.Godot.GuaGodotRuntime` from a Godot C# script:

```csharp
using Gua.Godot;

private readonly GuaGodotRuntime _gua = new();

public override void _Ready()
{
    _gua.StartInspectorBridge(8765);
}
```

The addon is a runtime adapter. `plugin.cfg` and `plugin.gd` exist only so the
directory follows Godot addon packaging conventions.

On Windows, place `gua_runtime.dll` in `addons/gua/bin` or build the sample
project, which copies it there automatically.
