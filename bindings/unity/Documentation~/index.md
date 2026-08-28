# Gua for Unity

The package automatically starts the Gua runtime in Play Mode and Windows
players, reflects UI Toolkit, uGUI, and TextMeshPro runtime controls, and listens on
`GUA_BRIDGE_PORT` (8765 by default). Add `GuaId` only where a stable explicit id
is required; semantic registration is otherwise automatic.

The stable support range is Windows x64, Unity 6000.5 or newer, and Mono. The
package contains precompiled managed assemblies and Windows Editor/Player native
libraries. Other IL2CPP targets, other operating systems, IMGUI, and EditorWindow
UI automation remain outside that stable range; the WebGL path below is
experimental.

## Unity WebGL and browser-native WebMCP (experimental)

WebGL builds install a tab-local `__guaUnityWebPort` from
`Runtime/Plugins/WebGL/GuaWebMcp.jslib`. A surrounding page passes it to
`gua-webmcp` with `createUnityWebGlBridge()` and `registerGuaWebMcp()`. Calls
remain in the page. Action promises resolve from request-correlated host
completion after Unity applies the action, not when it is enqueued.
UI reads and action enqueueing cross Player-only runtime entry points, so a
local Debug Inspector does not make private nodes available to browser tools.
The same bridge exposes the host-filtered World Object Tree through the
read-only `get_world_object_tree`, `find_world_objects`, and
`wait_for_world_object` tools. Shared browser-safe world contracts are provided
by `gua-world-tools`; WebMCP callers cannot elevate the runtime observation
profile or invoke actions on world objects.
When `GuaGameInputMap` initializes the input pump, the same page bridge exposes
only the matching Semantic Game Action and Raw Input capabilities. Calls wait
for correlated host completion and use a page-owned session that is released on
abort, timeout, bridge uninstall, or runtime destruction.

The WebGL build must include the Gua C ABI runtime as a WebAssembly native plugin;
the managed adapter remains a P/Invoke wrapper and does not implement another UI
model. The initial Unity bridge does not advertise screenshot support.

## Semantic game actions and raw input

Add `GuaGameInputMap` to one scene object and register only the actions intended
for automation. Game code reads semantic values with
`GuaUnityRuntime.GetGameInputValue` or subscribes to `GameInputChanged`.
Enabling **Raw Input** creates virtual keyboard, mouse, and gamepad devices with
Unity Input System 1.20.0. The adapter queues host-frame state events and
neutralizes/removes the devices when it stops. Raw capabilities are omitted
when Input System support or the opt-in map setting is unavailable.
`AllowPlayerAgentSemanticInput` and `AllowPlayerAgentRawInput` are independent,
default-off permissions for WebMCP/Public Agent callers. `EnableRawInput` alone
only initializes the local Debug path and never grants browser input access.
Physical keyboard tools accept the cross-adapter W3C code subset enumerated by
`commands.schema.json`, including left/right modifiers and common numpad operators.
When scenes change, the persistent runtime neutralizes the previous map and
publishes the `GuaGameInputMap` found in the new scene before accepting more
input.

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
