import { describe, expect, test } from "bun:test";

import { InspectorRecorder, validateRecording } from "../src/automation";
import { MockInspectorClient } from "../src/core";

describe("InspectorRecorder", () => {
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
