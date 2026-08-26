# Gua WebMCP same-page demo shell

This shell hosts an exported Godot or Unity game and `@gua/webmcp` in one page.
Copy the engine's generated loader/canvas markup into `index.html`, build this
script with `bun run build`, and serve the resulting directory over HTTP.

The engine addon installs `__guaGodotWebPort` or `__guaUnityWebPort`. The page
waits for that tab-local port and registers Gua tools with
`document.modelContext`. It never starts `gui-mcp`, opens a WebSocket, or assigns
a cross-tab session ID. Open the page in two tabs to get two independent game
instances and tool sets.

The status text remains useful in browsers without WebMCP: the game continues to
run and the page reports that `document.modelContext` is unavailable.
