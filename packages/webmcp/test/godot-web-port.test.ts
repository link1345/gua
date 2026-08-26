import { afterEach, describe, expect, test } from "bun:test";

type GodotWebPort = {
  __guaUninstall(): void;
  invoke(command: unknown, options?: { signal?: AbortSignal }): Promise<unknown>;
};

const godotGlobals = globalThis as typeof globalThis & {
  __guaGodotWebPort?: GodotWebPort;
  __guaGodotGetTree?: () => string;
  __guaGodotEnqueueAction?: (request: string) => string;
  __guaGodotPollAction?: (requestId: string) => string;
  __guaGodotCancelAction?: (requestId: string) => number;
};

afterEach(() => {
  godotGlobals.__guaGodotWebPort?.__guaUninstall();
  delete godotGlobals.__guaGodotWebPort;
  delete godotGlobals.__guaGodotGetTree;
  delete godotGlobals.__guaGodotEnqueueAction;
  delete godotGlobals.__guaGodotPollAction;
  delete godotGlobals.__guaGodotCancelAction;
});

async function installGodotWebPort(cancelled: string[]) {
  const source = await Bun.file(new URL(
    "../../../examples/godot-gdscript/addons/gua/gua_webmcp_bridge.gd",
    import.meta.url,
  )).text();
  const match = source.match(/JavaScriptBridge\.eval\("""([\s\S]*?)"""/);
  if (!match) throw new Error("Godot WebMCP install script was not found.");
  godotGlobals.__guaGodotGetTree = () => JSON.stringify({ screen: "title", nodes: [] });
  godotGlobals.__guaGodotEnqueueAction = () => JSON.stringify({ requestId: 17 });
  godotGlobals.__guaGodotPollAction = () => "null";
  godotGlobals.__guaGodotCancelAction = (requestId) => { cancelled.push(requestId); return 1; };
  new Function(match[1]!.replaceAll("%s", "test-owner"))();
  return godotGlobals.__guaGodotWebPort!;
}

describe("Godot Web same-page port", () => {
  test("cancels the native request when the bridge signal aborts", async () => {
    const cancelled: string[] = [];
    const port = await installGodotWebPort(cancelled);
    const controller = new AbortController();
    const pending = port
      .invoke({ type: "perform_action", request: { action: "click", nodeId: "start" } }, { signal: controller.signal })
      .catch((error) => error);

    controller.abort();

    await expect(pending).resolves.toMatchObject({ code: "aborted" });
    expect(cancelled).toEqual(["17"]);
  });

  test("rejects and cancels pending actions when the port is uninstalled", async () => {
    const cancelled: string[] = [];
    const port = await installGodotWebPort(cancelled);
    const pending = port
      .invoke({ type: "perform_action", request: { action: "click", nodeId: "start" } })
      .catch((error) => error);

    port.__guaUninstall();

    await expect(pending).resolves.toMatchObject({ code: "engine_unsupported" });
    expect(cancelled).toEqual(["17"]);
  });
});
