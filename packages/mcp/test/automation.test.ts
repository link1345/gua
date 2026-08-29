import { afterEach, describe, expect, test } from "bun:test";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import path from "node:path";
import { tmpdir } from "node:os";

import { PNG } from "pngjs";
import { guaPhysicalKeyboardCodes } from "gua-webmcp";

import { GuaAutomationManager, validateRecording } from "../src/automation";
import { GuaBridgeClient, guaMcpToolDefinitions, guaMcpTools, parseClockRunForArguments, replayRecording } from "../src/index";
import { validateRecording as validateInspectorRecording } from "../../inspector/src/automation";

const roots: string[] = [];

afterEach(async () => {
  await Promise.all(roots.splice(0).map((root) => rm(root, { recursive: true, force: true })));
});

describe("GuaAutomationManager", () => {
  test("bounds world waits by their requested deadline", async () => {
    const server = Bun.serve({
      port: 0,
      fetch(request, server) {
        if (server.upgrade(request)) return undefined;
        return new Response("upgrade required", { status: 426 });
      },
      websocket: {
        message() {
          // Intentionally withhold the query response.
        },
      },
    });
    const bridge = new GuaBridgeClient(`ws://127.0.0.1:${server.port}`, 5000);
    const startedAt = performance.now();
    try {
      await expect(bridge.waitForWorldObject({ kind: "door" }, 50)).rejects.toThrow("Timed out waiting for a Gua world object");
      expect(performance.now() - startedAt).toBeLessThan(500);
    } finally {
      bridge.close();
      server.stop(true);
    }
  });

  test("keeps bundled workspace packages out of published dependencies", async () => {
    const manifest = JSON.parse(await readFile(new URL("../package.json", import.meta.url), "utf8"));
    expect(Object.values(manifest.dependencies ?? {}).some((version) => String(version).startsWith("workspace:"))).toBe(false);
    expect(manifest.devDependencies["gua-world-tools"]).toBe("^0.19.2");
  });

  test("rejects clock_run_for without its required duration", () => {
    expect(() => parseClockRunForArguments({})).toThrow("durationMs");
    expect(parseClockRunForArguments({ durationMs: 25 })).toEqual({
      type: "clock_run_for",
      durationMs: 25,
      stepMs: undefined,
    });
  });

  test("publishes the AI recording and visual tool surface", () => {
    expect(guaMcpTools).toContain("start_recording");
    expect(guaMcpTools).toContain("stop_recording");
    expect(guaMcpTools).toContain("save_recording");
    expect(guaMcpTools).toContain("replay_recording");
    expect(guaMcpTools).toContain("compare_screenshot");
    expect(guaMcpTools).toContain("get_visual_artifacts");
    expect(guaMcpTools).toContain("clock_pause");
    expect(guaMcpTools).toContain("clock_run_for");
    expect(guaMcpTools).toContain("get_world_object_tree");
    expect(guaMcpTools).toContain("find_world_objects");
    expect(guaMcpTools).toContain("wait_for_world_object");
    expect(guaMcpTools).not.toContain("interact_world_object" as never);
  });

  test("publishes MCP-specific sensitive value and screenshot contracts", () => {
    const setValue = guaMcpToolDefinitions.find((tool) => tool.name === "set_value")!;
    const setValueSchema = setValue.inputSchema as {
      properties: Record<string, unknown>;
      allOf: Array<{ then: { required: string[] } }>;
    };
    expect(setValueSchema.properties.secretKey).toBeDefined();
    expect(setValueSchema.allOf[0]?.then.required).toContain("secretKey");

    const screenshot = guaMcpToolDefinitions.find((tool) => tool.name === "get_screenshot")!;
    expect(screenshot.description).toContain("latest screenshot published");
    expect(screenshot.description).toContain("does not request a fresh capture");
  });

  test("records, redacts, saves, and reloads semantic operations", async () => {
    const manager = await createManager();
    manager.startRecording();
    manager.recordAction({
      action: "click", nodeId: "open-login", requestId: 10, eventId: 10,
      preRevision: 2, postRevision: 3, relativeMilliseconds: 0,
    });
    manager.recordAction({
      action: "set_value", nodeId: "password", requestId: 11, eventId: 11,
      preRevision: 3, postRevision: 4, relativeMilliseconds: 20,
      value: "not-written", sensitive: true, secretKey: "login-password",
    });

    const recording = manager.stopRecording();
    expect(recording.steps).toHaveLength(2);
    expect(recording.steps[1]?.value).toBeUndefined();
    expect(recording.steps[1]?.secretKey).toBe("login-password");

    const saved = await manager.saveRecording("login-flow");
    expect(await readFile(saved.path, "utf8")).not.toContain("not-written");
    expect((await manager.loadRecording("login-flow")).steps).toEqual(recording.steps);
  });

  test("keeps game-input command metadata out of recorded arguments", async () => {
    const manager = await createManager();
    manager.startRecording();
    manager.recordGameInput({
      operation: "set_game_input_action", requestId: 12, sensitive: true, secretKey: "chat-secret",
      arguments: { type: "set_game_input_action", actionId: "chat", sensitive: true, secretKey: "chat-secret" },
    });

    const recording = manager.stopRecording();
    validateInspectorRecording(recording);
    const step = recording.steps[0];
    expect(step?.operation).toBe("set_game_input_action");
    expect(step?.secretKey).toBe("chat-secret");
    expect(step?.arguments).toEqual({ actionId: "chat", sensitive: true });
  });

  test("keeps advertised physical keyboard codes aligned with the protocol schema", async () => {
    const schema = JSON.parse(await readFile(new URL("../../../protocol/schema/commands.schema.json", import.meta.url), "utf8"));
    expect(schema.$defs.keyboardCode.enum).toEqual(guaPhysicalKeyboardCodes);
    for (const name of ["key_down", "key_up", "press_physical_key"]) {
      const tool = guaMcpToolDefinitions.find((candidate) => candidate.name === name)!;
      const code = (tool.inputSchema as { properties: { code: { enum: string[] } } }).properties.code;
      expect(code.enum).toEqual(guaPhysicalKeyboardCodes);
    }
  });

  test("validates common metadata on imported game-input steps", () => {
    const step = {
      action: "game_input", operation: "key_down", arguments: { code: "KeyW" },
      relativeMilliseconds: 10, preRevision: 0, postRevision: 0, sensitive: false,
    };
    expect(() => validateRecording({ schemaVersion: 2, steps: [step] })).not.toThrow();
    const validate = (candidate: Record<string, unknown>) => {
      const recording = { schemaVersion: 2 as const, steps: [{ ...step, ...candidate }] };
      return () => validateRecording(recording);
    };
    expect(validate({ relativeMilliseconds: undefined })).toThrow("relativeMilliseconds");
    expect(validate({ relativeMilliseconds: -1 })).toThrow("relativeMilliseconds");
    expect(validate({ preRevision: -1 })).toThrow("revisions");
    expect(validate({ postRevision: -1 })).toThrow("revisions");
    expect(validate({ sensitive: undefined })).toThrow("sensitive metadata");
    expect(() => validateRecording({ schemaVersion: 2, steps: [step, { ...step, relativeMilliseconds: 9 }] }))
      .toThrow("non-monotonic relativeMilliseconds");
    expect(() => validateRecording({ schemaVersion: 2, steps: [{ ...step,
      operation: "reset_context", arguments: { expectedSessionEpoch: 1 } }] }))
      .toThrow("invalid game input arguments");
    expect(() => validateRecording({ schemaVersion: 2, steps: [{ ...step,
      arguments: { type: "reset_context", code: "KeyW" } }] }))
      .toThrow("invalid game input arguments");
    expect(() => validateRecording({ schemaVersion: 2, steps: [{ ...step,
      arguments: { code: "IntlBackslash" } }] }))
      .toThrow("invalid game input arguments");
  });

  test("cancels replay before a delayed game-input dispatch and still cleans up", async () => {
    const commands: string[] = [];
    const bridge = {
      async performGameInput(input: { type: string }) { commands.push(input.type); return { requestId: 1 }; },
      async waitForGameInput() { return { completed: true, requestId: 1, succeeded: true, errorCode: 0 }; },
    } as unknown as GuaBridgeClient;
    const controller = new AbortController();
    const replay = replayRecording({ schemaVersion: 2, steps: [{
      action: "game_input", operation: "key_down", arguments: { code: "KeyW" },
      relativeMilliseconds: 100, preRevision: 0, postRevision: 0, sensitive: false,
    }] }, bridge, {}, "preserve_delays", 1000, controller.signal);
    setTimeout(() => controller.abort(), 10);
    await expect(replay).rejects.toThrow("cancelled");
    expect(commands).toEqual(["release_all_game_inputs"]);
  });

  test("cancels replay during game-input preflight before dispatch", async () => {
    const commands: string[] = [];
    let markPreflightStarted!: () => void;
    let resolveActions!: (value: unknown) => void;
    const preflightStarted = new Promise<void>((resolve) => { markPreflightStarted = resolve; });
    const actions = new Promise<unknown>((resolve) => { resolveActions = resolve; });
    const bridge = {
      getGameInputActions() { markPreflightStarted(); return actions; },
      async performGameInput(input: { type: string }) { commands.push(input.type); return { requestId: 1 }; },
      async waitForGameInput() { return { completed: true, requestId: 1, succeeded: true, errorCode: 0 }; },
    } as unknown as GuaBridgeClient;
    const controller = new AbortController();
    const replay = replayRecording({ schemaVersion: 2, steps: [{
      action: "game_input", operation: "press_game_input_action", arguments: { actionId: "danger" },
      relativeMilliseconds: 0, preRevision: 0, postRevision: 0, sensitive: false,
    }] }, bridge, {}, "prefer_conditions", 1000, controller.signal);
    await preflightStarted;
    controller.abort();
    resolveActions({ actions: [{ id: "danger", requiresConfirmation: false }] });
    await expect(replay).rejects.toThrow("cancelled");
    expect(commands).toEqual(["release_all_game_inputs"]);
  });

  test("cancels replay during UI preflight before dispatch", async () => {
    const commands: string[] = [];
    let markPreflightStarted!: () => void;
    let resolveTree!: (value: unknown) => void;
    const preflightStarted = new Promise<void>((resolve) => { markPreflightStarted = resolve; });
    const tree = new Promise<unknown>((resolve) => { resolveTree = resolve; });
    const bridge = {
      getUiTree() { markPreflightStarted(); return tree; },
      async performAction() { commands.push("click"); return { requestId: 1 }; },
      async performGameInput(input: { type: string }) { commands.push(input.type); return { requestId: 2 }; },
      async waitForGameInput() { return { completed: true, requestId: 2, succeeded: true, errorCode: 0 }; },
    } as unknown as GuaBridgeClient;
    const controller = new AbortController();
    const replay = replayRecording({ schemaVersion: 2, steps: [{
      action: "click", target: { id: "button" }, relativeMilliseconds: 0,
      preRevision: 0, postRevision: 0, sensitive: false,
    }] }, bridge, {}, "prefer_conditions", 1000, controller.signal);
    await preflightStarted;
    controller.abort();
    resolveTree({ revision: 1, nodes: [] });
    await expect(replay).rejects.toThrow("cancelled");
    expect(commands).toEqual(["release_all_game_inputs"]);
  });

  test("restores sensitive semantic game-input secrets with their published type", async () => {
    const values: unknown[] = [];
    const bridge = {
      async getGameInputActions() { return { actions: [{ id: "move", valueType: "vector2", requiresConfirmation: false }] }; },
      async performGameInput(input: { type: string; value?: unknown }) { values.push(input.value); return { requestId: 8 }; },
      async waitForGameInput() { return { completed: true, requestId: 8, succeeded: true, errorCode: 0 }; },
    } as unknown as GuaBridgeClient;
    await replayRecording({ schemaVersion: 2, steps: [{
      action: "game_input", operation: "set_game_input_action", arguments: { actionId: "move", sensitive: true },
      relativeMilliseconds: 0, preRevision: 0, postRevision: 0, sensitive: true, secretKey: "move-secret",
    }] }, bridge, { "move-secret": "{\"x\":1,\"y\":0}" }, "prefer_conditions", 1000);
    expect(values[0]).toEqual({ x: 1, y: 0 });
  });

  test("does not expose malformed sensitive vector secrets in replay errors", async () => {
    const bridge = {
      async getGameInputActions() { return { actions: [{ id: "move", valueType: "vector2", requiresConfirmation: false }] }; },
      async performGameInput() { return { requestId: 9 }; },
      async waitForGameInput() { return { completed: true, requestId: 9, succeeded: true, errorCode: 0 }; },
    } as unknown as GuaBridgeClient;
    const marker = "topsecret-value";
    let message = "";
    try {
      await replayRecording({ schemaVersion: 2, steps: [{
        action: "game_input", operation: "set_game_input_action", arguments: { actionId: "move", sensitive: true },
        relativeMilliseconds: 0, preRevision: 0, postRevision: 0, sensitive: true, secretKey: "move-secret",
      }] }, bridge, { "move-secret": marker }, "prefer_conditions", 1000);
    } catch (error) {
      message = error instanceof Error ? error.message : String(error);
    }
    expect(message).toContain("finite vector2 JSON object");
    expect(message).not.toContain(marker);
    expect(message).not.toContain("topsecret");
  });

  test("creates an explicit baseline and emits diff artifacts on mismatch", async () => {
    const manager = await createManager();
    const baseline = screenshot(2, 1, [10, 20, 30, 255, 40, 50, 60, 255]);
    const actual = screenshot(2, 1, [10, 20, 30, 255, 255, 0, 0, 255]);

    const created = await manager.compareScreenshot(baseline, {
      name: "title", variant: "windows-vulkan", updateBaseline: true,
    });
    expect(created.matched).toBe(true);
    expect(created.baselineCreated).toBe(true);

    const compared = await manager.compareScreenshot(actual, {
      name: "title", variant: "windows-vulkan", pixelThreshold: 0,
    });
    expect(compared.matched).toBe(false);
    expect(compared.differentPixels).toBe(1);
    expect(compared.differentPixelRatio).toBe(0.5);
    expect(await readFile(compared.comparisonPath as string, "utf8")).toContain('"reason": "pixel_difference"');
    expect((await manager.getVisualArtifacts("title")).files).toContain(compared.diffPath as string);
  });
});

async function createManager(): Promise<GuaAutomationManager> {
  const root = await mkdtemp(path.join(tmpdir(), "gua-mcp-automation-"));
  roots.push(root);
  return new GuaAutomationManager(root);
}

function screenshot(width: number, height: number, rgba: number[]) {
  const png = new PNG({ width, height });
  png.data.set(rgba);
  return {
    dataUri: `data:image/png;base64,${PNG.sync.write(png).toString("base64")}`,
    width,
    height,
  };
}
