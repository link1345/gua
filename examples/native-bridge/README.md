# Gua Native Bridge Example

This sample exposes a small `gua::Context` runtime over the Inspector WebSocket
command protocol.

It is intentionally Windows-only for now because the sample WebSocket server uses
WinSock directly. The protocol shape is the same as `packages/bridge-ws`.

Build:

```powershell
cmake --preset windows-msvc-debug
cmake --build --preset windows-msvc-debug --target gua-native-bridge-example
```

Run:

```powershell
.\build\windows-msvc-debug\examples\native-bridge\Debug\gua-native-bridge-example.exe
```

Then open the Inspector and connect to:

```text
ws://127.0.0.1:8765
```

The bridge handles:

- `get_ui_tree`
- `get_logs`
- `get_screenshot`
- `click_node`
- `focus_node`
