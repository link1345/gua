# @gua/world-tools

Runtime-dependency-free World Object Tree v1 types, MCP tool definitions, and
provider adapters shared by `gui-mcp` and browser WebMCP integrations.

Browser integrations inject a `GuaWorldProvider` and call
`registerWorldWebMcpTools(navigator.modelContext, provider)`. The adapter always
requests the `player` projection from the provider. Engines must explicitly opt
objects into their world-frame pump; arbitrary scene objects are never exposed.

The package only defines read-only `get_world_object_tree`,
`find_world_objects`, and `wait_for_world_object` tools. It has no Node or Bun
runtime dependency and intentionally provides no world action API.
