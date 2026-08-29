import { resolve } from "node:path";
import { createServer } from "node:net";
import { GuaBridgeClient } from "../packages/mcp/src/index";

const executable = resolve(
  process.argv[2] ?? "artifacts/godot-windows-release/GuaGodotSample.exe",
);
if (!(await Bun.file(executable).exists())) {
  throw new Error(`Godot Windows Release export is missing: ${executable}`);
}

const port = await findAvailablePort();
const gameProcess = Bun.spawn([executable, "--headless"], {
  env: { ...Bun.env, GUA_BRIDGE_PORT: String(port) },
  stdout: "pipe",
  stderr: "pipe",
});
let stdout = "";
let stderr = "";
const stdoutTask = captureOutput(
  gameProcess.stdout,
  (chunk) => (stdout += chunk),
);
const stderrTask = captureOutput(
  gameProcess.stderr,
  (chunk) => (stderr += chunk),
);

let client: GuaBridgeClient | undefined;
try {
  const deadline = performance.now() + 20_000;
  const listeningMessage = `Gua Inspector bridge listening on ws://127.0.0.1:${port}`;
  while (!stdout.includes(listeningMessage)) {
    if (stderr.includes("Failed to start Gua Inspector bridge")) {
      throw new Error(
        `Spawned Godot Release failed to start its Bridge on port ${port}.`,
      );
    }
    if (gameProcess.exitCode !== null) {
      throw new Error(
        `Godot Release exited before starting its Bridge (${gameProcess.exitCode}).`,
      );
    }
    if (performance.now() >= deadline) {
      throw new Error(
        `Timed out waiting for the spawned Godot Release Bridge on port ${port}.`,
      );
    }
    await Bun.sleep(25);
  }

  let initial;
  while (performance.now() < deadline) {
    client?.close();
    client = new GuaBridgeClient(`ws://127.0.0.1:${port}`, 1_000);
    try {
      initial = await client.getUiTree();
      if (initial.screen === "title") break;
    } catch {
      if (gameProcess.exitCode !== null)
        throw new Error(
          `Godot Release exited before accepting an external connection (${gameProcess.exitCode}).`,
        );
    }
    await Bun.sleep(100);
  }

  if (
    initial?.screen !== "title" ||
    !initial.nodes.some(
      (node) => node.id === "start" && node.actions.includes("click"),
    )
  ) {
    throw new Error(
      `Godot Windows Release did not expose the allowed start action: ${JSON.stringify(initial)}`,
    );
  }

  const receipt = await client.performAction({
    action: "click",
    nodeId: "start",
  });
  if (
    receipt === null ||
    !Number.isSafeInteger(receipt.requestId) ||
    receipt.requestId <= 0
  ) {
    throw new Error(
      `Godot Windows Release did not accept the external click: ${JSON.stringify(receipt)}`,
    );
  }
  const completion = await client.waitForAction(receipt.requestId, 5_000);
  if (!completion.succeeded) {
    throw new Error(
      `Godot Windows Release rejected the external click: ${JSON.stringify(completion)}`,
    );
  }

  let finalTree;
  while (performance.now() < deadline) {
    finalTree = await client.getUiTree();
    if (finalTree.screen === "loading") break;
    await Bun.sleep(25);
  }
  if (finalTree?.screen !== "loading") {
    throw new Error(
      `Godot Windows Release click did not publish the loading screen: ${JSON.stringify(finalTree)}`,
    );
  }
  console.log(
    `Godot Windows Release external-control smoke passed (requestId=${receipt.requestId}).`,
  );
} finally {
  client?.close();
  gameProcess.kill();
  await gameProcess.exited.catch(() => undefined);
  await Promise.allSettled([stdoutTask, stderrTask]);
  if (stdout.trim().length > 0) console.log(stdout.trim());
  if (stderr.trim().length > 0) console.error(stderr.trim());
}

function findAvailablePort(): Promise<number> {
  return new Promise((resolvePort, reject) => {
    const server = createServer();
    server.unref();
    server.once("error", reject);
    server.listen(0, "127.0.0.1", () => {
      const address = server.address();
      if (address === null || typeof address === "string") {
        server.close();
        reject(
          new Error(
            "Could not allocate a loopback port for the Godot Release smoke test.",
          ),
        );
        return;
      }
      server.close((error) => {
        if (error) reject(error);
        else resolvePort(address.port);
      });
    });
  });
}

async function captureOutput(
  stream: ReadableStream<Uint8Array>,
  append: (chunk: string) => void,
): Promise<void> {
  const reader = stream.getReader();
  const decoder = new TextDecoder();
  try {
    while (true) {
      const result = await reader.read();
      if (result.done) break;
      append(decoder.decode(result.value, { stream: true }));
    }
    append(decoder.decode());
  } finally {
    reader.releaseLock();
  }
}
