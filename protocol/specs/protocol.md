# Gua Protocol Draft

Gua exposes the current UI state of a running game as a semantic UI tree and
accepts commands that interact with nodes in that tree.

## UI Tree

A UI tree response describes one frame or snapshot of runtime UI state.

- `screen`: logical screen name, such as `title` or `settings`
- `nodes`: flat list of semantic UI nodes

Nodes are intentionally semantic. They describe role, label, state, bounds, and
supported actions, not rendering internals.

## Inspector Snapshots

The v0.3 Inspector consumes three protocol payloads:

- UI tree: the current semantic UI snapshot, matching `ui-tree.schema.json`
- Screenshot: the latest runtime screenshot, matching `screenshot.schema.json`
- Logs: ordered runtime log entries, matching `logs.schema.json`

The screenshot payload stores an already encoded `dataUri` plus `width` and
`height`. This keeps the C ABI small and avoids forcing the core protocol to own
PNG encoding, GPU readback, or platform-specific capture code.

## Commands

Commands are external automation requests.

Initial command types:

- `get_ui_tree`
- `get_node`
- `click_node`
- `focus_node`
- `press_key`
- `text_input`
- `move_gamepad`
- `wait_for_node`
- `get_screenshot`
- `get_logs`
- `poll_events`

For the Inspector WebSocket bridge, commands are sent as JSON text messages with
a numeric `id`. Responses echo the same `id` and either include `result` or
`error`.

```json
{ "id": 1, "type": "get_ui_tree" }
{ "id": 2, "type": "click_node", "nodeId": "start" }
```

```json
{ "id": 1, "ok": true, "result": { "screen": "title", "nodes": [] } }
{ "id": 2, "ok": false, "error": "Gua node not found: start" }
```

Bridges may also push snapshots without a request. Inspectors should treat these
as authoritative runtime updates and refresh the visible panels immediately.

```json
{
  "type": "snapshot",
  "snapshot": {
    "uiTree": { "screen": "title", "nodes": [] },
    "logs": [],
    "screenshot": { "dataUri": "", "width": 0, "height": 0 }
  }
}
```

## MCP Tools

The v0.4 MCP server is a protocol consumer. It does not own runtime state and it
does not replace the Inspector bridge. By default it connects to the same Gua
WebSocket bridge at `ws://127.0.0.1:8765`; set `GUA_BRIDGE_URL` to target another
runtime adapter. The npm-ready CLI entrypoint is `gui-mcp mcp`, so a published
package can be launched with `bunx gui-mcp@latest mcp`.

Initial MCP tools:

- `get_ui_tree`: returns the current semantic UI tree
- `click_node`: sends a click command for a node id
- `press_key`: sends a key command when the connected bridge supports it
- `wait_for_node`: polls `get_ui_tree` until a node id appears
- `get_screenshot`: returns the latest screenshot payload
- `get_logs`: returns ordered runtime logs
- `run_test`: executes a small list of `wait_for_node` and `click_node` steps

The MCP server uses stdio JSON-RPC for MCP clients and the existing Gua
request/response WebSocket payloads for the runtime side.

## Events

Events are queued by the runtime core when commands should be observed by the
game code. Language bindings should poll events instead of passing callbacks
across ABI boundaries.

Initial event types:

- `click`
- `focus`
- `key`
- `text`
- `gamepad`

## Transport

The protocol should not depend on one transport. Early implementations may use
in-process calls, TCP, WebSocket, stdio, or MCP tool calls as long as the payloads
match the schemas.
