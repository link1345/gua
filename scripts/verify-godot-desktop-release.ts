import { resolve } from "node:path";
import { createServer } from "node:net";
import { GuaBridgeClient } from "../packages/mcp/src/index";

const executable = resolve(process.argv[2] ?? "");
const label = process.argv[3] ?? process.platform;
if (!executable || !(await Bun.file(executable).exists())) {
  throw new Error(`Godot ${label} Release export is missing: ${executable}`);
}

const port = await findAvailablePort();
const gameProcess = Bun.spawn([executable, "--headless"], {
  env: { ...Bun.env, GUA_BRIDGE_PORT: String(port) }, stdout: "pipe", stderr: "pipe",
});
let stdout = ""; let stderr = "";
const stdoutTask = captureOutput(gameProcess.stdout, (chunk) => (stdout += chunk));
const stderrTask = captureOutput(gameProcess.stderr, (chunk) => (stderr += chunk));
let client: GuaBridgeClient | undefined;
try {
  const deadline = performance.now() + 20_000;
  while (!stdout.includes(`Gua WebSocket bridge listening on ws://127.0.0.1:${port}`)) {
    if (gameProcess.exitCode !== null) throw new Error(`Godot ${label} Release exited before starting its bridge (${gameProcess.exitCode}).`);
    if (performance.now() >= deadline) throw new Error(`Timed out waiting for the Godot ${label} Release bridge.`);
    await Bun.sleep(25);
  }
  let initial;
  while (performance.now() < deadline) {
    client?.close(); client = new GuaBridgeClient(`ws://127.0.0.1:${port}`, 1_000);
    try { initial = await client.getUiTree(); if (initial.screen === "title") break; } catch { /* retry startup */ }
    await Bun.sleep(100);
  }
  if (initial?.screen !== "title" || !initial.nodes.some((node) => node.id === "start" && node.actions.includes("click")))
    throw new Error(`Godot ${label} Release did not expose the start action: ${JSON.stringify(initial)}`);
  const receipt = await client.performAction({ action: "click", nodeId: "start" });
  if (receipt === null) throw new Error(`Godot ${label} Release did not accept the external click.`);
  const completion = await client.waitForAction(receipt.requestId, 5_000);
  if (!completion.succeeded) throw new Error(`Godot ${label} Release rejected the click: ${JSON.stringify(completion)}`);
  let finalTree;
  while (performance.now() < deadline) { finalTree = await client.getUiTree(); if (finalTree.screen === "loading") break; await Bun.sleep(25); }
  if (finalTree?.screen !== "loading") throw new Error(`Godot ${label} Release did not publish the loading screen.`);
  console.log(`Godot ${label} Release external-control smoke passed (requestId=${receipt.requestId}).`);
} finally {
  client?.close(); gameProcess.kill(); await gameProcess.exited.catch(() => undefined);
  await Promise.allSettled([stdoutTask, stderrTask]);
  if (stdout.trim()) console.log(stdout.trim()); if (stderr.trim()) console.error(stderr.trim());
}

function findAvailablePort(): Promise<number> {
  return new Promise((resolvePort, reject) => {
    const server = createServer(); server.unref(); server.once("error", reject);
    server.listen(0, "127.0.0.1", () => {
      const address = server.address();
      if (address === null || typeof address === "string") { server.close(); reject(new Error("Could not allocate a loopback port.")); return; }
      server.close((error) => error ? reject(error) : resolvePort(address.port));
    });
  });
}

async function captureOutput(stream: ReadableStream<Uint8Array>, append: (chunk: string) => void): Promise<void> {
  const reader = stream.getReader(); const decoder = new TextDecoder();
  try { while (true) { const result = await reader.read(); if (result.done) break; append(decoder.decode(result.value, { stream: true })); } append(decoder.decode()); }
  finally { reader.releaseLock(); }
}
