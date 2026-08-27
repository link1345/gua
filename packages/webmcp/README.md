# gua-webmcp

Browser-native WebMCP adapter for a Gua-enabled Godot Web Export or Unity WebGL page.
It registers tools on the experimental `document.modelContext` API and calls an
engine bridge in the same page. It does not start an MCP server or WebSocket.
Alongside Semantic UI Tree actions, the Godot and Unity bridge helpers register
the read-only `get_world_object_tree`, `find_world_objects`, and
`wait_for_world_object` tools when the engine-owned world adapter is available.
The helpers also register Semantic Game Action and Raw Input tools only for
capabilities reported by an initialized engine input pump. One page-local input
owner is used for all such calls; timeout, cancellation, unregister, port
replacement, and engine shutdown release its held input.
World types and selector definitions come from the public `gua-world-tools`
package; observation remains host-filtered and browser callers cannot request a
debug profile.

```ts
import { createGodotWebBridge, registerGuaWebMcp } from "gua-webmcp";

const registration = await registerGuaWebMcp(createGodotWebBridge());
if (!registration.supported) console.info(registration.error);
```

Only objects explicitly opted into the engine adapter are observable. World v1
does not expose actions, arbitrary scene traversal, or host method invocation.

For Unity WebGL, use `createUnityWebGlBridge()` instead. The engine export must
run in the same document so that its `__guaGodotWebPort` or
`__guaUnityWebPort` is available before constructing the bridge.

The engine bridge is the source of truth. Its `performAction()` promise must not
resolve on enqueue; it resolves with the request-correlated completion emitted
after the engine adapter applies the action. `performAction(request, { signal })`
must cancel an accepted request that is still queued when the signal aborts;
`registerGuaWebMcp` uses this to propagate caller cancellation and its configured
action timeout into Godot or Unity. If the host already consumed the action, the
caller remains cancelled while the engine bridge drains its eventual correlated
completion. `getScreenshot` is an optional read
of the latest published image, and the tool is not registered when the engine
has no drawable-frame readback path.

Feature detection is non-fatal. Each browser tab creates its own bridge and tool
registrations, so no Gua session router or cross-tab session ID is introduced.
Actions with `requiresConfirmation` are checked against the current action map
immediately before dispatch and require `confirmed: true`.

The currently released Godot Web addon supports debug Web exports only. Release
Web GDExtension builds are tracked in
[`link1345/gua#75`](https://github.com/link1345/gua/issues/75).
