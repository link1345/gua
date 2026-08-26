# Gua for Unity

The package automatically starts the Gua runtime in Play Mode and Windows
players, reflects UI Toolkit, uGUI, and TextMeshPro runtime controls, and listens on
`GUA_BRIDGE_PORT` (8765 by default). Add `GuaId` only where a stable explicit id
is required; semantic registration is otherwise automatic.

Supported in the initial release: Windows x64, Unity 6000.0 or newer, and Mono.
The package contains precompiled managed assemblies and Windows Editor/Player
native libraries. IL2CPP, other operating systems, IMGUI, and EditorWindow UI
automation are outside the supported range.
