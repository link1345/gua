# gua-world-tools

Browser-safe World Object Tree v1 types, payload validators, MCP tool definitions,
and provider adapters shared by `gui-mcp` and browser WebMCP integrations.

Browser integrations inject a `GuaWorldProvider` and call
`registerWorldWebMcpTools(document.modelContext, provider)`. The adapter always
requests the `player` projection from the provider. Engines must explicitly opt
objects into their world-frame pump; arbitrary scene objects are never exposed.

The package only defines read-only `get_world_object_tree`,
`find_world_objects`, and `wait_for_world_object` tools. It has no Node or Bun
runtime dependency and intentionally provides no world action API.

```ts
import { parseWorldObjectTree, selectorFromArguments } from "gua-world-tools";

const tree = parseWorldObjectTree(await engine.getWorldObjectTree());
const selector = selectorFromArguments({
  kind: "enemy",
  relativeToObjectId: "player",
  maxDistance: 12,
  limit: 5,
});
```

Nearby queries use the provider's world units and one Player-projected snapshot.
Results include `sessionEpoch`, `frameSequence`, `revision`, and aligned spatial
distances ordered by distance and then object ID compared as UTF-8 bytes. Private,
unknown, or coordinate-omitted reference objects all produce
the same valid empty result.
