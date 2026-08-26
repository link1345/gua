# Gua for Unity

The package automatically starts the Gua runtime in Play Mode and Windows
players, reflects UI Toolkit, uGUI, and TextMeshPro runtime controls, and listens on
`GUA_BRIDGE_PORT` (8765 by default). Add `GuaId` only where a stable explicit id
is required; semantic registration is otherwise automatic.

The stable support range is Windows x64, Unity 6000.0 or newer, and Mono. The
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

The WebGL build must include the Gua C ABI runtime as a WebAssembly native plugin;
the managed adapter remains a P/Invoke wrapper and does not implement another UI
model. The initial Unity bridge does not advertise screenshot support.
