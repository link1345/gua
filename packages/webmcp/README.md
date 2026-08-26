# gua-webmcp

Browser-native WebMCP adapter for a Gua-enabled Godot Web Export or Unity WebGL page.
It registers tools on the experimental `document.modelContext` API and calls an
engine bridge in the same page. It does not start an MCP server or WebSocket.

```ts
import { createGodotWebBridge, registerGuaWebMcp } from "gua-webmcp";

const registration = await registerGuaWebMcp(createGodotWebBridge());
if (!registration.supported) console.info(registration.error);
```

For Unity WebGL, use `createUnityWebGlBridge()` instead. The engine export must
run in the same document so that its `__guaGodotWebPort` or
`__guaUnityWebPort` is available before constructing the bridge.

The engine bridge is the source of truth. Its `performAction()` promise must not
resolve on enqueue; it resolves with the request-correlated completion emitted
after the engine adapter applies the action. `performAction(request, { signal })`
must cancel an accepted request that is still queued when the signal aborts;
`registerGuaWebMcp` uses this to propagate caller cancellation and its configured
action timeout into Godot or Unity. `getScreenshot` is an optional read
of the latest published image, and the tool is not registered when the engine
has no drawable-frame readback path.

Feature detection is non-fatal. Each browser tab creates its own bridge and tool
registrations, so no Gua session router or cross-tab session ID is introduced.

The currently released Godot Web addon supports debug Web exports only. Release
Web GDExtension builds are tracked in
[`link1345/gua#75`](https://github.com/link1345/gua/issues/75).
