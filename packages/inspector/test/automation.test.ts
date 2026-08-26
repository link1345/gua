import { describe, expect, test } from "bun:test";

import { InspectorRecorder, validateRecording } from "../src/automation";
import { MockInspectorClient, createCoalescedAsyncRunner, worldObjectDepths, type GuaWorldObject } from "../src/core";

describe("InspectorRecorder", () => {
  test("computes every level of the world object hierarchy", () => {
    const object = (id: string, parentId?: string): GuaWorldObject => ({ id, parentId, kind: "object", label: id,
      space: "world2d", position: { x: 0, y: 0 }, visibleToPlayer: true, active: true,
      agentExposure: "auto", tags: [], state: {} });
    const depths = worldObjectDepths([object("root"), object("child", "root"), object("grandchild", "child"), object("leaf", "grandchild")]);
    expect([...depths.values()]).toEqual([0, 1, 2, 3]);
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
