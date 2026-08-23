import { describe, expect, test } from "bun:test";
import { DemoRuntime } from "../src/index";

describe("DemoRuntime virtual clock", () => {
  test("rejects installation until the current timeline is reset", () => {
    const runtime = new DemoRuntime();
    expect(runtime.installClock(100, 10).nowMs).toBe(100);

    expect(() => runtime.installClock(0, 5)).toThrow("invalid_state");
    expect(runtime.getClock().nowMs).toBe(100);
    expect(runtime.getClock().defaultStepMs).toBe(10);
  });

  test("rejects durations that would rewind or overflow the timeline", () => {
    const runtime = new DemoRuntime();
    runtime.installClock(100, 10);
    runtime.pauseClock();

    expect(() => runtime.runClockFor(-25)).toThrow("invalid_duration");
    expect(() => runtime.runClockFor(Number.POSITIVE_INFINITY)).toThrow("invalid_duration");
    expect(runtime.getClock().nowMs).toBe(100);
  });

  test("advances the completion frame without inventing a semantic revision", async () => {
    const runtime = new DemoRuntime();
    runtime.installClock(0, 10);
    runtime.pauseClock();
    const before = runtime.getUiTree();
    const operation = runtime.runClockFor(10);

    await new Promise((resolve) => setTimeout(resolve, 0));

    const after = runtime.getUiTree();
    expect(after.frameSequence).toBe(before.frameSequence + 1);
    expect(after.revision).toBe(before.revision);
    expect(runtime.getClock().completedOperationSequence).toBe(operation.operationSequence);
  });
});
