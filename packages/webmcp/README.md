# gua-webmcp

Browser-native WebMCP adapter for a Gua-enabled Godot Web Export or Unity WebGL page.
It registers tools on the experimental `document.modelContext` API and calls an
engine bridge in the same page. It does not start an MCP server or WebSocket.

```ts
import { registerGuaWebMcp } from "gua-webmcp";

const registration = await registerGuaWebMcp(window.guaEngineBridge);
if (!registration.supported) console.info(registration.error);
```

The engine bridge is the source of truth. Its `performAction()` promise must not
resolve on enqueue; it resolves with the request-correlated completion emitted
after the engine adapter applies the action. `getScreenshot` is optional, and the
tool is not registered when the engine has no drawable-frame readback path.

Feature detection is non-fatal. Each browser tab creates its own bridge and tool
registrations, so no Gua session router or cross-tab session ID is introduced.
