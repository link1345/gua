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
