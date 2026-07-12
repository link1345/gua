# Gua Protocol Draft

Gua exposes the current UI state of a running game as a semantic UI tree and
accepts commands that interact with nodes in that tree.

## UI Tree

A UI tree response describes one frame or snapshot of runtime UI state.

- `screen`: logical screen name, such as `title` or `settings`
- `nodes`: flat list of semantic UI nodes
- `schemaVersion`: currently `2`
- `frameSequence`: increments once for every completed host frame
- `revision`: increments only when the semantic screen or node content changes
- `sessionEpoch`: identifies the current test-isolation session and increments after every successful context reset

Nodes are intentionally semantic. They describe role, label, state, bounds, and
supported actions, not rendering internals.

Version 2 adds `parentId`, `text`, `value`, and optional boolean state. An
omitted property means that the adapter cannot observe that state. A present
property whose value is `false` means that the adapter observed the state and
it was false. Adapters must not fill unsupported state with false. Values
crossing the initial C ABI are normalized to UTF-8 strings; the schema also
allows native JSON scalar values for future transports.

`frameSequence` and `revision` have different purposes. Rebuilding the same
semantic tree in consecutive frames increments `frameSequence` but leaves
`revision` unchanged. Changing the screen, node membership, hierarchy, bounds,
text, value, state, or actions increments `revision` at `end_frame`.

Frame construction is transactional. `begin_frame` creates a private staging
frame, registration functions update only that staging frame, and `end_frame`
atomically publishes the screen, nodes, `frameSequence`, and `revision` under
one context lock. Reads, selectors, action validation, diagnostics, and remote
transports only observe the last completed frame. Before the first successful
publish they observe the empty `unknown` snapshot. An invalid or abandoned
staging frame never changes the last completed snapshot or its metadata.

`sessionEpoch` starts at 1. A successful reset starts a new epoch and resets
`frameSequence` and `revision` to zero. Consumers must use the epoch together
with frame/revision metadata; values from an older epoch are stale.

## Test session inspection and reset

Context inspection reports semantic node count, pending and in-flight action
request counts, unconsumed event count, log count, screenshot presence, and the
first request/event action plus node id. It never includes action payload values,
so sensitive values cannot leak through teardown diagnostics.

Reset is scoped to one `gua_context_t` / `gua_runtime_t`; it does not use global
state and cannot affect another context. The selectable flags are nodes (1),
requests (2), events (4), retained diagnostic history (8), logs (16), and
screenshot (32). The default is 15: nodes, requests, events, and history are
cleared, while logs and screenshot are preserved unless explicitly selected.
Bridge server, port, active WebSocket connections, and context configuration are
never reset.

Strict reset checks selected request/event queues before mutation. If pending,
in-flight, or unconsumed state exists, it returns `dirty` with counts and a
redacted first-item summary and changes nothing. Non-strict reset reports how
many selected items it discarded. Every successful reset increments
`sessionEpoch`; local callers may pass zero to use the current epoch, but remote
`reset_context` commands must provide `expectedSessionEpoch`. A stale remote
epoch is rejected without mutation. Multiple clients of one runtime observe the
same reset because the isolation boundary is the shared runtime context, not a
WebSocket connection.

## Failure diagnostics and artifact version 1

`diagnostics.schema.json` is the source of truth for a best-effort failure
snapshot. The additive C ABI copy-JSON API and WebSocket `get_diagnostics`
command return the same versioned document. It includes the final semantic UI
tree, session/frame/revision, pending and in-flight requests, recent operation
and observed-event history, logs, optional screenshot, and caller-provided
environment metadata.

Operation and event history are context-owned bounded ring buffers. The default
limit is 100 entries per history and callers may set another non-negative limit;
zero disables retention. Polling an event never removes its retained history.
`GUA_RESET_HISTORY` clears both histories without changing queue semantics.

Sensitive action values are available only while the adapter consumes a
request. Pending diagnostics and retained history store an empty value with
`sensitive: true`; they never retain plaintext. A missing screenshot is `null`
and does not fail semantic diagnostics capture.

Filesystem layout and overwrite policy belong to testing helpers, not the
runtime core. Writers use a unique failure directory, preserve the original
assertion/timeout when capture fails, and report capture errors as a secondary
note. When a wait captured an initial tree, the writer creates a deterministic
node-id diff with added, removed, and changed IDs.

## Adapter-Owned Reflection

Runtime adapters should rebuild the semantic tree from the host UI every frame
or snapshot. Game code should not have to restate its visible buttons, labels,
and controls as separate Gua calls when the adapter can observe those controls.

- If a host UI element disappears, the next adapter snapshot omits its Gua node.
- If a host UI element remains in the host tree but becomes hidden, the adapter
  keeps the node and publishes `visible: false`.
- If a host UI element is clicked by the user, the adapter emits a Gua event.
- If automation requests `click_node`, the core records a pending click request.
  The adapter consumes that request when it reaches the matching host UI element
  and then activates the host control through the same path normal game code
  already uses.

This keeps the C ABI as the stable protocol boundary while letting ImGui, Godot,
and later engine adapters own the engine-specific reflection details.

### Initial adapter mapping

| Host control | Gua role | Adapter-owned fields |
| --- | --- | --- |
| ImGui `Button` facade | `button` | text, visible, enabled, focused, hovered, pressed |
| ImGui `Text` facade | `text` | text, visible |
| Godot `Button` | `button` | parentId, text, visible, enabled, focused |
| Godot `CheckBox` | `checkbox` | parentId, text, focused, checked |
| Godot `LineEdit` / `TextEdit` | `textbox` | parentId, text, value, focused |
| Godot `SpinBox` | `slider` | parentId, value, enabled from editable state, focused through the inner editor |
| Godot `OptionButton` | `combobox` | parentId, value, focused |
| Godot `ItemList` | `list` + `listitem` children | stable derived child id, parentId, text, selected |
| Godot `TabContainer` | `tablist` + `tab` children | stable derived child id, parentId, text, selected |
| Godot `ScrollContainer` | `scrollarea` | bounds, visible, enabled, scroll action |

## Semantic selectors

`selector.schema.json` is the source of truth for semantic queries. String
criteria are ordinal and case-sensitive. Their default match mode is `exact`;
callers may explicitly select `contains` or ECMAScript `regex`. An invalid regex
is a selector syntax error, never a zero-match result.

Criteria combine with logical AND. `name` means the node's accessible `label`.
`text` uses the v2 `text` field when known and falls back to `label` for legacy
nodes. `visible` and `enabled` are tri-state at ABI level (`any`, `false`,
`true`) so omitting a filter differs from requiring `false`.

A scope parent is excluded from its own results. The default scope searches all
descendants by following `parentId`; `directChild: true` limits it to immediate
children. A strict single query fails for both zero and multiple matches.
`QueryAll` is the only query form that accepts multiple results. Ambiguity
diagnostics include candidate `id`, `role`, `label`, and `parentId` and suggest
adding a stable id, state filter, or narrower scope.

The C ABI evaluates selectors through versioned `gua_selector_v1_t` and
`gua_query_nodes_json`. C++, .NET local contexts, and the remote `query_nodes`
command all use that evaluator. The legacy `gua_find_node_by_*` exports remain
available for ABI compatibility but retain first-match behavior.

The legacy `gua_register_node` and `gua_get_node_state` C exports remain valid.
New integrations use `gua_node_descriptor_v2_t` / `gua_node_state_v2_t`, whose
`struct_size` protects ABI versioning and whose `known_mask` distinguishes
unsupported properties from observed false or empty values. Readers should
continue accepting legacy payloads that contain only `screen` and `nodes`
during migration.

## Inspector Snapshots

The v0.3 Inspector consumes three protocol payloads:

- UI tree: the current semantic UI snapshot, matching `ui-tree.schema.json`
- Screenshot: the latest runtime screenshot, matching `screenshot.schema.json`
- Logs: ordered runtime log entries, matching `logs.schema.json`

The screenshot payload stores an already encoded `dataUri` plus `width` and
`height`. This keeps the C ABI small and avoids forcing the core protocol to own
PNG encoding, GPU readback, or platform-specific capture code.

`capture_screenshot` is explicit and on demand. The runtime queues and correlates
requests while the adapter owns viewport readback and PNG encoding on the next
drawable frame. Concurrent pending requests are coalesced into one capture and
receive the same image with distinct request IDs. Results include `sessionEpoch`
and `frameSequence`; `headless`, `rendering_disabled`, and `unsupported` are
distinct unavailable errors defined by `screenshot-capture.schema.json`; a reset
while queued produces `stale_session`. `get_screenshot` remains the latest-published-image
compatibility API. Screenshots can contain rendered secrets and are not redacted.

## Visual comparison and operation recording v1

Visual comparison is an opt-in consumer of the existing PNG screenshot payload;
semantic assertions remain the primary test path. Baselines use an explicit test
name and variant, are updated only by an API option or `GUA_UPDATE_BASELINES=1`,
and are never inferred from OS/GPU state. Masks are removed from both diff output
and the ratio denominator. Dimension mismatch never performs an implicit resize.

`recording.schema.json` is the source of truth for recording version 1. Targets
prefer stable `id`, then strict role/name/scope selection, with coordinate fallback
only when explicitly recorded and permitted. Request IDs deduplicate retained
operation/event history. Replay preserves delays or prefers recorded semantic wait
conditions. Sensitive steps contain a `secretKey`, never plaintext, and require a
caller resolver.

## Commands

Commands are external automation requests.

Initial command types:

- `get_ui_tree`
- `get_node`
- `click_node`
- `focus_node`
- `press_key`
- `set_value`
- `set_checked`
- `select`
- `scroll`

### Semantic action lifecycle (v1)

Semantic actions follow `enqueue -> consume -> host action -> observed event`. Enqueue acceptance only records a request; it is never completion. Each accepted request receives a monotonically increasing `requestId`, and the adapter must copy that ID into its success or failure event after attempting the host operation.

The v1 action names map directly to the additive C ABI action enum: `click`, `focus`, `set_value`, `set_checked`, `select`, `scroll`, and `press_key`. Enqueue validation distinguishes `node_not_found`, `hidden`, `disabled`, `unsupported`, and `invalid_value`. Existing click functions remain compatibility wrappers over the generic queue.

`sensitive=true` permits the adapter to receive the requested value, but event values, logs, diagnostics, and recordings must use an empty or redacted representation. `scrollUnit=0` means host pixels and `scrollUnit=1` means semantic lines. A key request may omit `nodeId` to target the host's current focus; when a node is provided it must expose `press_key`.
Key modifiers use a transport-neutral bit mask: Shift is `1`, Alt is `2`, Control is `4`, and Meta/Command is `8`. Adapters must route both key-down and key-up through the host input pipeline and report success only after accepting the complete key gesture.
- `text_input`
- `move_gamepad`
- `wait_for_node`
- `get_screenshot`
- `capture_screenshot` with optional `afterFrameSequence`
- `get_logs`
- `get_diagnostics`
- `get_version`
- `poll_events`
- `get_context_status`
- `reset_context` with `expectedSessionEpoch`, optional reset `flags`, and optional `strict`

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
- `get_diagnostics`: returns the versioned best-effort diagnostics snapshot
- `get_version`: returns `version.schema.json` for the components actually loaded. `godotPluginVersion` is `null` outside Godot. Capability IDs are stable public identifiers; new IDs are additive.
- `run_test`: executes a small list of `wait_for_node` and `click_node` steps

The MCP server uses stdio JSON-RPC for MCP clients and the existing Gua
request/response WebSocket payloads for the runtime side.

## Events

Events are queued by the runtime core when adapters observe host UI input.
Language bindings should poll events instead of passing callbacks across ABI
boundaries. External commands such as `click_node` are requests first; adapters
consume them and emit events only after the corresponding host UI action has
been applied.

Testing clients may combine enqueue and request-id-specific polling in one
convenience operation. Such helpers must not consume unrelated results or
ordinary events, must preserve sensitive-value redaction, and should report the
final screen/frame/revision when completion fails.

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
