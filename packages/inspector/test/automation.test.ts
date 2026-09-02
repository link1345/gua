import { describe, expect, test } from "bun:test";

import { InspectorRecorder, prepareManualGameInput, replayRecording, validateRecording } from "../src/automation";
import { MockInspectorClient, createCoalescedAsyncRunner, findGameInputActionsCompatible, formatBounds, hasCompleteBounds, readSnapshot, worldObjectDepths, type GuaWorldObject } from "../src/core";

describe("InspectorRecorder", () => {
  test("falls back to a bounded legacy action map when search is unsupported", async () => {
    const client = new MockInspectorClient();
    client.findGameInputActions = async () => { throw new Error("unknown command"); };
    const result = await findGameInputActionsCompatible(client, { id: "move", limit: 1 });
    expect(result).toMatchObject({ count: 1, truncated: false, actions: [{ id: "move" }] });
  });

  test("re-resolves confirmation immediately before manual game input", async () => {
    const prompts: string[] = [];
    const resolvedIds: string[] = [];
    const command = await prepareManualGameInput(
      { type: "press_game_input_action", actionId: "launch" },
      async (actionId) => { resolvedIds.push(actionId); return { schemaVersion: 1, sessionEpoch: 1, revision: 2, context: "combat", actions: [{
        id: "launch", valueType: "button", holdable: false, active: true, bindings: [], risk: "dangerous", requiresConfirmation: true,
      }] }; },
      (action) => { prompts.push(action.id); return true; },
    );
    expect(command).toEqual({ type: "press_game_input_action", actionId: "launch", confirmed: true });
    expect(resolvedIds).toEqual(["launch"]);
    expect(prompts).toEqual(["launch"]);
  });

  test("represents omitted Player bounds without producing overlay coordinates", () => {
    const projected = { y: 20, h: 40 };
    expect(formatBounds(projected)).toBe("unknown, 20, unknown, 40");
    expect(hasCompleteBounds(projected)).toBe(false);
    expect(hasCompleteBounds({ x: 10, y: 20, w: 30, h: 40 })).toBe(true);
  });
  test("computes every level of the world object hierarchy", () => {
    const object = (id: string, parentId?: string): GuaWorldObject => ({ id, parentId, kind: "object", label: id,
      space: "world2d", position: { x: 0, y: 0 }, visibleToPlayer: true, active: true,
      agentExposure: "auto", tags: [], state: {} });
    const depths = worldObjectDepths([object("root"), object("child", "root"), object("grandchild", "child"), object("leaf", "grandchild")]);
    expect([...depths.values()]).toEqual([0, 1, 2, 3]);
  });

  test("surfaces world tree fetch failures instead of replacing displayed state", async () => {
    const client = new MockInspectorClient();
    client.getWorldObjectTree = async () => { throw new Error("temporary world failure"); };
    await expect(readSnapshot(client)).rejects.toThrow("temporary world failure");
  });

  test("retries polled trees until their session epochs match", async () => {
    const client = new MockInspectorClient();
    let uiCalls = 0;
    let worldCalls = 0;
    const originalUi = client.getUiTree.bind(client);
    const originalWorld = client.getWorldObjectTree.bind(client);
    client.getUiTree = async () => ({ ...(await originalUi()), sessionEpoch: ++uiCalls === 1 ? 1 : 2 });
    client.getWorldObjectTree = async () => {
      worldCalls += 1;
      return { ...(await originalWorld()), sessionEpoch: 2 };
    };
    const snapshot = await readSnapshot(client);
    expect(snapshot.uiTree.sessionEpoch).toBe(2);
    expect(snapshot.worldObjectTree.sessionEpoch).toBe(2);
    expect([uiCalls, worldCalls]).toEqual([2, 2]);
  });

  test("never publishes a persistently mixed-epoch polled snapshot", async () => {
    const client = new MockInspectorClient();
    const originalUi = client.getUiTree.bind(client);
    const originalWorld = client.getWorldObjectTree.bind(client);
    client.getUiTree = async () => ({ ...(await originalUi()), sessionEpoch: 1 });
    client.getWorldObjectTree = async () => ({ ...(await originalWorld()), sessionEpoch: 2 });
    await expect(readSnapshot(client)).rejects.toThrow("same session epoch");
  });

  test("coalesces snapshot-driven clock refreshes without starving updates", async () => {
    const releases: Array<() => void> = [];
    let calls = 0;
    const refresh = createCoalescedAsyncRunner(async () => {
      calls += 1;
      await new Promise<void>((resolve) => releases.push(resolve));
    });

    const first = refresh();
    const second = refresh();
    refresh();
    expect(calls).toBe(1);

    releases.shift()?.();
    await waitUntil(() => calls === 2);
    releases.shift()?.();
    await Promise.all([first, second]);
    expect(calls).toBe(2);
  });

  test("controls the mock virtual clock deterministically", async () => {
    const client = new MockInspectorClient();
    await client.installClock(0, 10);
    await client.pauseClock();
    const overridden = await client.runClockFor(4, 4);
    expect(overridden.defaultStepMs).toBe(10);
    const status = await client.runClockFor(21);
    expect(status.nowMs).toBe(25);
    expect(status.defaultStepMs).toBe(10);
    expect(status.state).toBe("paused");
  });
  test("exports schema v2 and redacts sensitive values", () => {
    const recorder = new InspectorRecorder();
    recorder.start();
    recorder.record(
      { action: "set_value", nodeId: "password", value: "not-written", sensitive: true, secretKey: "login-password" },
      { requestId: 4, completion: { requestId: 4, succeeded: true, error: 0, revision: 9 } },
      8,
      9,
    );
    const recording = recorder.stop();

    validateRecording(recording);
    expect(recording.schemaVersion).toBe(2);
    expect(recording.steps[0]?.value).toBeUndefined();
    expect(recording.steps[0]?.secretKey).toBe("login-password");
    expect(JSON.stringify(recording)).not.toContain("not-written");
  });
  test("records semantic game input and explicit release", () => {
    const recorder = new InspectorRecorder();
    recorder.start();
    recorder.recordGameInput({ type: "set_game_input_action", actionId: "move", value: { x: 1, y: 0 }, leaseMs: 5000 }, 20);
    recorder.recordGameInput({ type: "release_game_input_action", actionId: "move" }, 21);
    const recording = recorder.stop();
    validateRecording(recording);
    expect(recording.steps.map((step) => step.operation)).toEqual(["set_game_input_action", "release_game_input_action"]);
    expect(recording.steps.every((step) => step.target === undefined && step.coordinateFallback === undefined)).toBe(true);
    expect(() => validateRecording({ ...recording, steps: [{ ...recording.steps[0], target: { currentFocus: true } }] }))
      .toThrow("game input cannot have a UI target");
  });

  test("redacts and restores sensitive game input", async () => {
    const recorder = new InspectorRecorder();
    recorder.start();
    recorder.recordGameInput({ type: "set_game_input_action", actionId: "chat", value: "not-written",
      sensitive: true, secretKey: "chat-secret" }, 22);
    const recording = recorder.stop();
    expect(recording.steps[0]?.secretKey).toBe("chat-secret");
    expect(JSON.stringify(recording)).not.toContain("not-written");

    const commands: unknown[] = [];
    await replayRecording(recording, async () => ({ nodes: [] }) as never, async () => ({}) as never,
      { "chat-secret": "restored" }, async (command) => { commands.push(command); }, async () => ({
        schemaVersion: 1, sessionEpoch: 1, revision: 1, context: "gameplay", actions: [{ id: "chat", valueType: "text",
          holdable: false, active: true, bindings: [], risk: "safe", requiresConfirmation: false }],
      }));
    expect(commands).toEqual([
      { type: "set_game_input_action", actionId: "chat", sensitive: true, value: "restored" },
      { type: "release_all_game_inputs" },
    ]);
    expect(() => validateRecording({ ...recording, steps: [{ ...recording.steps[0],
      arguments: { ...recording.steps[0]?.arguments, value: "plaintext" } }] }))
      .toThrow("sensitive game input plaintext");
    expect(() => validateRecording({ ...recording, steps: [{ ...recording.steps[0], secretKey: undefined }] }))
      .toThrow("requires a secretKey");
  });

  test("preserves replay failures but surfaces cleanup-only failures", async () => {
    const recording = { schemaVersion: 2 as const, steps: [{ action: "game_input" as const,
      operation: "press_physical_key" as const, arguments: { code: "Space" }, sensitive: false,
      relativeMilliseconds: 0, preRevision: 0, postRevision: 0 }] };
    let calls = 0;
    await expect(replayRecording(recording, async () => ({ nodes: [] }) as never, async () => ({}) as never, {}, async () => {
      calls += 1;
      throw new Error(calls === 1 ? "replay failed" : "cleanup failed");
    })).rejects.toThrow("replay failed");
    calls = 0;
    await expect(replayRecording(recording, async () => ({ nodes: [] }) as never, async () => ({}) as never, {}, async () => {
      calls += 1;
      if (calls === 2) throw new Error("cleanup failed");
    })).rejects.toThrow("cleanup failed");
  });

  test("re-resolves and reconfirms protected semantic actions during replay", async () => {
    const recording = { schemaVersion: 2 as const, steps: [{ action: "game_input" as const,
      operation: "press_game_input_action" as const, arguments: { actionId: "launch", confirmed: true }, sensitive: false,
      relativeMilliseconds: 0, preRevision: 0, postRevision: 0 }] };
    const actions = async () => ({ schemaVersion: 1 as const, sessionEpoch: 1, revision: 2, context: "launch",
      actions: [{ id: "launch", valueType: "button" as const, holdable: false, active: true, bindings: [],
        risk: "dangerous", requiresConfirmation: true }] });
    const declined: unknown[] = [];
    await expect(replayRecording(recording, async () => ({ nodes: [] }) as never, async () => ({}) as never, {},
      async (command) => { declined.push(command); }, actions, () => false)).rejects.toThrow("Confirmation was declined");
    expect(declined).toEqual([{ type: "release_all_game_inputs" }]);

    const confirmed: unknown[] = [];
    await replayRecording(recording, async () => ({ nodes: [] }) as never, async () => ({}) as never, {},
      async (command) => { confirmed.push(command); }, actions, () => true);
    expect(confirmed).toEqual([
      { type: "press_game_input_action", actionId: "launch", confirmed: true },
      { type: "release_all_game_inputs" },
    ]);
  });

  test("rejects v1 and non-game-input operations before replay", async () => {
    const step = { action: "game_input" as const, operation: "press_physical_key", arguments: { code: "Space" },
      sensitive: false, relativeMilliseconds: 0, preRevision: 0, postRevision: 0 };
    expect(() => validateRecording({ schemaVersion: 1, steps: [step] })).toThrow("invalid game input");
    expect(() => validateRecording({ schemaVersion: 2, steps: [{ ...step, operation: "reset_context",
      arguments: { expectedSessionEpoch: 1 } }] })).toThrow("invalid game input arguments");
    expect(() => validateRecording({ schemaVersion: 2, steps: [{ ...step, operation: "press_game_input_action",
      arguments: { type: "reset_context", actionId: "jump" } }] })).toThrow("invalid game input arguments");
  });
});

async function waitUntil(predicate: () => boolean): Promise<void> {
  for (let index = 0; index < 20; index += 1) {
    if (predicate()) return;
    await Promise.resolve();
  }
  throw new Error("Timed out waiting for condition.");
}
