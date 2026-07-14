#!/usr/bin/env bun

import { runGuaMcpServer } from "./index.js";

const command = Bun.argv[2] ?? "mcp";

if (command === "mcp") {
  await runGuaMcpServer().catch((error) => {
    process.stderr.write(`[gui-mcp] error: ${(error as Error).message}\n`);
    process.exitCode = 1;
  });
} else if (command === "--help" || command === "-h" || command === "help") {
  process.stdout.write(`Usage:
  gui-mcp mcp

Environment:
  GUA_BRIDGE_URL  WebSocket URL for the Gua runtime bridge. Defaults to ws://127.0.0.1:8765.
  GUA_ARTIFACT_DIR  Root for recordings, baselines, and visual artifacts. Defaults to .gua.
`);
} else {
  process.stderr.write(`[gui-mcp] error: unknown command: ${command}\n`);
  process.stderr.write("Run `gui-mcp --help` for usage.\n");
  process.exitCode = 1;
}
