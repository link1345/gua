import { describe, expect, test } from "bun:test";
import { DemoRuntime } from "../src/index";

describe("DemoRuntime virtual clock", () => {
  test("rejects installation until the current timeline is reset", () => {
    const runtime = new DemoRuntime(() => 0);
    expect(runtime.installClock(100, 10).nowMs).toBe(100);

    expect(() => runtime.installClock(0, 5)).toThrow("invalid_state");
    expect(runtime.getClock().nowMs).toBe(100);
    expect(runtime.getClock().defaultStepMs).toBe(10);
  });

  test("rejects durations that would rewind or overflow the timeline", () => {
    const runtime = new DemoRuntime(() => 0);
    runtime.installClock(100, 10);
    runtime.pauseClock();

    expect(() => runtime.runClockFor(-25)).toThrow("invalid_duration");
    expect(() => runtime.runClockFor(Number.POSITIVE_INFINITY)).toThrow("invalid_duration");
    expect(runtime.getClock().nowMs).toBe(100);
  });

  test("rejects steps and runs that cannot advance a large timeline", () => {
    const runtime = new DemoRuntime(() => 0);
    expect(() => runtime.installClock(1e16, 1)).toThrow("invalid_duration");

    runtime.installClock(1e16, 2);
    runtime.pauseClock();
    expect(() => runtime.runClockFor(1)).toThrow("invalid_duration");
    expect(() => runtime.runClockFor(100, 1)).toThrow("invalid_duration");
    expect(runtime.getClock().nowMs).toBe(1e16);
  });

  test("advances the completion frame without inventing a semantic revision", async () => {
    const runtime = new DemoRuntime(() => 0);
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

  test("advances running time from a monotonic source and freezes while paused", () => {
    let monotonicNow = 1_000;
    const runtime = new DemoRuntime(() => monotonicNow);
    runtime.installClock(100, 10);

    monotonicNow += 25;
    expect(runtime.getClock().nowMs).toBe(125);
    runtime.pauseClock();
    monotonicNow += 50;
    expect(runtime.getClock().nowMs).toBe(125);

    runtime.resumeClock();
    monotonicNow += 10;
    expect(runtime.getClock().nowMs).toBe(135);
  });
});
