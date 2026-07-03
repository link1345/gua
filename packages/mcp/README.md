# gui-mcp

`gui-mcp` is the MCP server for the Gua runtime UI automation protocol.

It proxies MCP tool calls to a running Gua WebSocket bridge, so game runtimes keep
owning the semantic UI tree while MCP clients consume the protocol.

## Usage

```sh
bunx gui-mcp@latest mcp
```

