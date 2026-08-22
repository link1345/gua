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
});
