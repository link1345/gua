# Gua for Unity

The package automatically starts the Gua runtime in Play Mode and Windows
players, reflects UI Toolkit, uGUI, and TextMeshPro runtime controls, and listens on
`GUA_BRIDGE_PORT` (8765 by default). Add `GuaId` only where a stable explicit id
is required; semantic registration is otherwise automatic.

Supported in the initial release: Windows x64, Unity 6000.0 or newer, and Mono.
The package contains precompiled managed assemblies and Windows Editor/Player
native libraries. IL2CPP, other operating systems, IMGUI, and EditorWindow UI
automation are outside the supported range.

## World objects

Add `GuaWorldObject` only to scene objects that are safe to observe. Assign a
stable `Id`, semantic `Kind`, 2D/3D space, player visibility, exposure, tags,
and primitive state explicitly:

```csharp
var door = gameObject.AddComponent<Gua.Unity.GuaWorldObject>();
door.Id = "door-a";
door.Kind = "door";
door.Space = Gua.Core.GuaWorldSpace.World2D;
door.VisibleToPlayer = true;
door.SetState("locked", true);
```

The adapter publishes global transform positions each frame and links the
nearest opted-in ancestor. It does not expose ordinary GameObjects or UI objects
automatically. Do not place secrets in labels, tags, or state.

Add `GuaAgentPolicyComponent` to a uGUI or `GuaWorldObject` GameObject to mark it
private, transform Player-visible fields, or override allowed UI actions. For UI
Toolkit and custom adapters, call `GuaUnityAdapterRegistry.SetAgentPolicy` with
the reflected target object. These policies affect Player profile only.
Enable `Override Exposure` only when the component should replace the exposure
already declared by `GuaWorldObject`; field-only policies inherit that setting.
