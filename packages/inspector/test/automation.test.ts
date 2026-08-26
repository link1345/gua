import { describe, expect, test } from "bun:test";

import { InspectorRecorder, validateRecording } from "../src/automation";
import { MockInspectorClient, createCoalescedAsyncRunner, readSnapshot, worldObjectDepths, type GuaWorldObject } from "../src/core";

describe("InspectorRecorder", () => {
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
  test("exports schema v1 and redacts sensitive values", () => {
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
    expect(recording.schemaVersion).toBe(1);
    expect(recording.steps[0]?.value).toBeUndefined();
    expect(recording.steps[0]?.secretKey).toBe("login-password");
    expect(JSON.stringify(recording)).not.toContain("not-written");
  });
});

async function waitUntil(predicate: () => boolean): Promise<void> {
  for (let index = 0; index < 20; index += 1) {
    if (predicate()) return;
    await Promise.resolve();
  }
  throw new Error("Timed out waiting for condition.");
}
