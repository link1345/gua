# Gua for Unity

The package automatically starts the Gua runtime in Play Mode and Windows
players, reflects UI Toolkit, uGUI, and TextMeshPro runtime controls, and listens on
`GUA_BRIDGE_PORT` (8765 by default). Add `GuaId` only where a stable explicit id
is required; semantic registration is otherwise automatic.

Supported in the initial release: Windows x64, Unity 6000.5 or newer, and Mono.
The package contains precompiled managed assemblies and Windows Editor/Player
native libraries. IL2CPP, other operating systems, IMGUI, and EditorWindow UI
automation are outside the supported range.

## Semantic game actions and raw input

Add `GuaGameInputMap` to one scene object and register only the actions intended
for automation. Game code reads semantic values with
`GuaUnityRuntime.GetGameInputValue` or subscribes to `GameInputChanged`.
Enabling **Raw Input** creates virtual keyboard, mouse, and gamepad devices with
Unity Input System 1.20.0. The adapter queues host-frame state events and
neutralizes/removes the devices when it stops. Raw capabilities are omitted
when Input System support or the opt-in map setting is unavailable.
