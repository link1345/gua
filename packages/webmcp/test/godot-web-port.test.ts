import { afterEach, describe, expect, test } from "bun:test";

type GodotWebPort = {
  __guaUninstall(): void;
  invoke(command: unknown, options?: { signal?: AbortSignal; timeoutMs?: number }): Promise<unknown>;
};

const godotGlobals = globalThis as typeof globalThis & {
  __guaGodotWebPort?: GodotWebPort;
  __guaGodotGetTree?: () => string;
  __guaGodotGetWorldTree?: () => string;
  __guaGodotQueryWorld?: (request: string) => string;
  __guaGodotEnqueueAction?: (request: string) => string;
  __guaGodotPollAction?: (requestId: string) => string;
  __guaGodotCancelAction?: (requestId: string) => number;
  __guaGodotGetGameInputCapabilities?: () => string;
  __guaGodotGetGameInputActions?: () => string;
  __guaGodotGetGameInputState?: () => string;
  __guaGodotEnqueueGameInput?: (request: string) => string;
  __guaGodotPollGameInput?: (requestId: string) => string;
  __guaGodotReleaseGameInput?: (recreate?: string) => number;
};

afterEach(() => {
  godotGlobals.__guaGodotWebPort?.__guaUninstall();
  delete godotGlobals.__guaGodotWebPort;
  delete godotGlobals.__guaGodotGetTree;
  delete godotGlobals.__guaGodotGetWorldTree;
  delete godotGlobals.__guaGodotQueryWorld;
  delete godotGlobals.__guaGodotEnqueueAction;
  delete godotGlobals.__guaGodotPollAction;
  delete godotGlobals.__guaGodotCancelAction;
  delete godotGlobals.__guaGodotGetGameInputCapabilities;
  delete godotGlobals.__guaGodotGetGameInputActions;
  delete godotGlobals.__guaGodotGetGameInputState;
  delete godotGlobals.__guaGodotEnqueueGameInput;
  delete godotGlobals.__guaGodotPollGameInput;
  delete godotGlobals.__guaGodotReleaseGameInput;
});

async function installGodotWebPort(
  cancelled: string[],
  options: {
    cancellationResult?: number;
    pollAction?: (requestId: string) => string;
    enqueueGameInput?: (request: string) => string;
    pollGameInput?: (requestId: string) => string;
    releaseGameInput?: (recreate?: string) => number;
  } = {},
) {
  const source = await Bun.file(new URL(
    "../../../examples/godot-gdscript/addons/gua/gua_webmcp_bridge.gd",
    import.meta.url,
  )).text();
  const match = source.match(/JavaScriptBridge\.eval\("""([\s\S]*?)"""/);
  if (!match) throw new Error("Godot WebMCP install script was not found.");
  godotGlobals.__guaGodotGetTree = () => JSON.stringify({ screen: "title", nodes: [] });
  godotGlobals.__guaGodotGetWorldTree = () => JSON.stringify({ schemaVersion: 1, sessionEpoch: 1, frameSequence: 1, revision: 1, scene: "level", objects: [] });
  godotGlobals.__guaGodotQueryWorld = () => JSON.stringify({ valid: true, matches: [] });
  godotGlobals.__guaGodotEnqueueAction = () => JSON.stringify({ requestId: 17 });
  godotGlobals.__guaGodotPollAction = options.pollAction ?? (() => "null");
  godotGlobals.__guaGodotCancelAction = (requestId) => { cancelled.push(requestId); return options.cancellationResult ?? 1; };
  godotGlobals.__guaGodotGetGameInputCapabilities = () => JSON.stringify(["raw_keyboard_input_v1"]);
  godotGlobals.__guaGodotGetGameInputActions = () => JSON.stringify({ schemaVersion: 1, sessionEpoch: 1, revision: 1, context: "", actions: [] });
  godotGlobals.__guaGodotGetGameInputState = () => JSON.stringify({ schemaVersion: 1, held: [] });
  godotGlobals.__guaGodotEnqueueGameInput = options.enqueueGameInput ?? (() => JSON.stringify({ requestId: 23 }));
  godotGlobals.__guaGodotPollGameInput = options.pollGameInput ?? (() => "null");
  godotGlobals.__guaGodotReleaseGameInput = options.releaseGameInput ?? (() => 1);
  new Function(match[1]!.replaceAll("%s", "test-owner"))();
  return godotGlobals.__guaGodotWebPort!;
}

describe("Godot Web same-page port", () => {
  test("routes World Object Tree reads and queries through Godot callbacks", async () => {
    const port = await installGodotWebPort([]);
    await expect(port.invoke({ type: "get_world_object_tree" })).resolves.toMatchObject({ scene: "level" });
    await expect(port.invoke({ type: "query_world_objects", worldId: "door" })).resolves.toEqual({ valid: true, matches: [] });
  });
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

  test("uses the per-call action timeout", async () => {
    const cancelled: string[] = [];
    const port = await installGodotWebPort(cancelled);

    await expect(port.invoke(
      { type: "perform_action", request: { action: "click", nodeId: "start" } },
      { timeoutMs: 0 },
    )).rejects.toMatchObject({ code: "timeout" });
    expect(cancelled).toEqual(["17"]);
  });

  test("aborts every correlated game-input call when one call releases the page owner", async () => {
    const released: Array<string | undefined> = [];
    let requestId = 20;
    const port = await installGodotWebPort([], {
      enqueueGameInput: () => JSON.stringify({ requestId: ++requestId }),
      releaseGameInput: (recreate) => { released.push(recreate); return 1; },
    });
    const controller = new AbortController();
    const first = port.invoke(
      { type: "perform_game_input", request: { type: "key_down", code: "KeyA" } },
      { signal: controller.signal },
    ).catch((error) => error);
    const second = port.invoke(
      { type: "perform_game_input", request: { type: "key_down", code: "KeyB" } },
    ).catch((error) => error);

    controller.abort();

    await expect(first).resolves.toMatchObject({ code: "aborted" });
    await expect(second).resolves.toMatchObject({ code: "aborted" });
    expect(released).toEqual(["1"]);
  });

  test("rejects every pending game-input call when the port is uninstalled", async () => {
    const released: Array<string | undefined> = [];
    let requestId = 30;
    const port = await installGodotWebPort([], {
      enqueueGameInput: () => JSON.stringify({ requestId: ++requestId }),
      releaseGameInput: (recreate) => { released.push(recreate); return 1; },
    });
    const first = port.invoke({ type: "perform_game_input", request: { type: "key_down", code: "KeyA" } }).catch((error) => error);
    const second = port.invoke({ type: "perform_game_input", request: { type: "key_down", code: "KeyB" } }).catch((error) => error);

    port.__guaUninstall();

    await expect(first).resolves.toMatchObject({ code: "engine_unsupported" });
    await expect(second).resolves.toMatchObject({ code: "engine_unsupported" });
    expect(released).toEqual([undefined]);
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

  test("drains an in-flight completion after uninstall rejects the call", async () => {
    const cancelled: string[] = [];
    let polls = 0;
    const port = await installGodotWebPort(cancelled, {
      cancellationResult: -1,
      pollAction: () => JSON.stringify(++polls >= 2
        ? { requestId: 17, action: "click", succeeded: true }
        : null),
    });
    const pending = port
      .invoke({ type: "perform_action", request: { action: "click", nodeId: "start" } })
      .catch((error) => error);

    port.__guaUninstall();

    await expect(pending).resolves.toMatchObject({ code: "engine_unsupported" });
    await waitFor(() => polls >= 2);
    expect(cancelled).toEqual(["17"]);
  });

  test("stops uninstall drain polling when the Godot producer disappears", async () => {
    const cancelled: string[] = [];
    let polls = 0;
    const port = await installGodotWebPort(cancelled, {
      cancellationResult: -1,
      pollAction: () => JSON.stringify(++polls >= 2
        ? { code: "engine_unsupported", message: "The Godot Gua adapter is no longer available." }
        : null),
    });
    const pending = port
      .invoke({ type: "perform_action", request: { action: "click", nodeId: "start" } })
      .catch((error) => error);

    port.__guaUninstall();

    await expect(pending).resolves.toMatchObject({ code: "engine_unsupported" });
    await waitFor(() => polls >= 2);
    await new Promise((resolve) => setTimeout(resolve, 5));
    expect(polls).toBe(2);
    expect(cancelled).toEqual(["17"]);
  });

  test("drains an already-emitted completion when uninstall cancellation reports not found", async () => {
    const cancelled: string[] = [];
    let polls = 0;
    const port = await installGodotWebPort(cancelled, {
      cancellationResult: 0,
      pollAction: () => JSON.stringify(++polls >= 2
        ? { requestId: 17, action: "click", succeeded: true }
        : null),
    });
    const pending = port
      .invoke({ type: "perform_action", request: { action: "click", nodeId: "start" } })
      .catch((error) => error);

    port.__guaUninstall();

    await expect(pending).resolves.toMatchObject({ code: "engine_unsupported" });
    expect(cancelled).toEqual(["17"]);
    expect(polls).toBe(2);
  });

  test("drains an in-flight completion after reporting the abort", async () => {
    const cancelled: string[] = [];
    let polls = 0;
    const port = await installGodotWebPort(cancelled, {
      cancellationResult: -1,
      pollAction: () => JSON.stringify(++polls >= 2
        ? { requestId: 17, action: "click", succeeded: true }
        : null),
    });
    const controller = new AbortController();
    const pending = port
      .invoke({ type: "perform_action", request: { action: "click", nodeId: "start" } }, { signal: controller.signal })
      .catch((error) => error);

    controller.abort();

    await expect(pending).resolves.toMatchObject({ code: "aborted" });
    await waitFor(() => polls >= 2);
    expect(cancelled).toEqual(["17"]);
    expect(polls).toBe(2);
  });

  test("drains an already-emitted completion when cancellation reports not found", async () => {
    const cancelled: string[] = [];
    let polls = 0;
    const port = await installGodotWebPort(cancelled, {
      cancellationResult: 0,
      pollAction: () => JSON.stringify(++polls >= 2
        ? { requestId: 17, action: "click", succeeded: true }
        : null),
    });
    const controller = new AbortController();
    const pending = port
      .invoke({ type: "perform_action", request: { action: "click", nodeId: "start" } }, { signal: controller.signal })
      .catch((error) => error);

    controller.abort();

    await expect(pending).resolves.toMatchObject({ code: "aborted" });
    expect(cancelled).toEqual(["17"]);
    expect(polls).toBe(2);
  });
});

async function waitFor(predicate: () => boolean): Promise<void> {
  const deadline = performance.now() + 100;
  while (!predicate()) {
    if (performance.now() >= deadline) throw new Error("Timed out waiting for the Godot port to drain its result.");
    await new Promise((resolve) => setTimeout(resolve, 1));
  }
}
