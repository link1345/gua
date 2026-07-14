import { afterEach, describe, expect, test } from "bun:test";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import path from "node:path";
import { tmpdir } from "node:os";

import { PNG } from "pngjs";

import { GuaAutomationManager } from "../src/automation";
import { guaMcpTools } from "../src/index";

const roots: string[] = [];

afterEach(async () => {
  await Promise.all(roots.splice(0).map((root) => rm(root, { recursive: true, force: true })));
});

describe("GuaAutomationManager", () => {
  test("publishes the AI recording and visual tool surface", () => {
    expect(guaMcpTools).toContain("start_recording");
    expect(guaMcpTools).toContain("stop_recording");
    expect(guaMcpTools).toContain("save_recording");
    expect(guaMcpTools).toContain("replay_recording");
    expect(guaMcpTools).toContain("compare_screenshot");
    expect(guaMcpTools).toContain("get_visual_artifacts");
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
