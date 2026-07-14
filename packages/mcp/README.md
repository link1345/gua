# gui-mcp

`gui-mcp` is the MCP server for the Gua runtime UI automation protocol.

It proxies MCP tool calls to a running Gua WebSocket bridge, so game runtimes keep
owning the semantic UI tree while MCP clients consume the protocol.

## Usage

```sh
bunx gui-mcp@latest mcp
```

The server connects to `ws://127.0.0.1:8765` by default. Set
`GUA_BRIDGE_URL` for another runtime adapter and `GUA_ARTIFACT_DIR` to control
where recordings, baselines, and visual failure artifacts are written. The
artifact directory defaults to `.gua`; tool-provided names are sanitized and
cannot escape that directory.

## Semantic actions

AI clients can inspect the tree and invoke all protocol v1 semantic actions:

- `get_ui_tree`, `wait_for_node`, `get_screenshot`, `get_logs`
- `click_node`, `focus_node`, `set_value`, `set_checked`, `select`, `scroll`, `press_key`
- `run_test` for a small wait/click sequence

When the bridge returns a `requestId`, action tools poll the correlated host
completion event. Enqueue acceptance alone is not reported as completion.

## Recording and replay

`start_recording` records subsequent semantic action tools. `stop_recording`
returns a `recording.schema.json` v1 document, and `save_recording` writes the
last completed recording under `<artifact-dir>/recordings`.

`replay_recording` accepts an inline recording, a saved recording name, or the
last completed recording. It supports recorded semantic wait conditions and
request-correlated completion. Sensitive `set_value` steps store only a
`secretKey`; replay values are supplied through the tool's in-memory `secrets`
map and are not written to the recording.
Coordinate fallback documents can be loaded, but replay rejects them by default;
MCP automation stays on semantic targets.

## Visual comparison

`compare_screenshot` compares the latest `data:image/png;base64` screenshot with
an explicit test name and renderer/OS variant. Baseline creation or replacement
requires `updateBaseline: true`. A failure writes `actual.png`, `expected.png`
when available, `diff.png`, and `comparison.json` under the artifact directory.
`get_visual_artifacts` returns the latest manifest and artifact paths.

