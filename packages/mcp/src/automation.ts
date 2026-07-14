import { mkdir, readFile, readdir, writeFile } from "node:fs/promises";
import path from "node:path";

import { PNG } from "pngjs";

export type RecordedAction = "click" | "focus" | "set_value" | "set_checked" | "select" | "scroll" | "press_key";

export interface RecordingTarget {
  id?: string;
  role?: string;
  name?: string;
  scope?: string;
  currentFocus?: true;
}

export interface RecordingStep {
  action: RecordedAction;
  requestId?: number;
  eventId?: number;
  target?: RecordingTarget;
  coordinateFallback?: { x: number; y: number };
  relativeMilliseconds: number;
  preRevision: number;
  postRevision: number;
  waitCondition?: string;
  value?: string;
  secretKey?: string;
  sensitive: boolean;
  deltaX?: number;
  deltaY?: number;
  scrollUnit?: number;
  modifiers?: number;
}

export interface GuaRecording {
  schemaVersion: 1;
  steps: RecordingStep[];
}

export interface RecordActionInput {
  action: RecordedAction;
  requestId?: number;
  eventId?: number;
  nodeId?: string;
  relativeMilliseconds?: number;
  preRevision: number;
  postRevision: number;
  waitCondition?: string;
  value?: string;
  sensitive?: boolean;
  secretKey?: string;
  deltaX?: number;
  deltaY?: number;
  scrollUnit?: number;
  modifiers?: number;
}

export interface ScreenshotPayload {
  dataUri: string;
  width: number;
  height: number;
}

export interface MaskRectangle {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface VisualComparisonOptions {
  name: string;
  variant?: string;
  updateBaseline?: boolean;
  pixelThreshold?: number;
  maxDifferentPixelRatio?: number;
  masks?: MaskRectangle[];
}

export interface VisualComparisonResult {
  schemaVersion: 1;
  name: string;
  variant: string;
  matched: boolean;
  baselineCreated: boolean;
  reason?: "baseline_missing" | "dimension_mismatch" | "pixel_difference";
  width: number;
  height: number;
  comparedPixels: number;
  differentPixels: number;
  differentPixelRatio: number;
  baselinePath: string;
  artifactDirectory?: string;
  actualPath?: string;
  diffPath?: string;
  comparisonPath?: string;
}

export class GuaAutomationManager {
  private recordingStartedAt: number | null = null;
  private activeSteps: RecordingStep[] = [];
  private lastRecording: GuaRecording | null = null;
  private lastVisualResult: VisualComparisonResult | null = null;

  constructor(readonly artifactRoot: string) {}

  startRecording(): { recording: true; startedAt: string } {
    if (this.recordingStartedAt !== null) {
      throw new Error("A Gua recording is already active.");
    }
    this.recordingStartedAt = Date.now();
    this.activeSteps = [];
    return { recording: true, startedAt: new Date(this.recordingStartedAt).toISOString() };
  }

  isRecording(): boolean {
    return this.recordingStartedAt !== null;
  }

  recordAction(input: RecordActionInput): void {
    if (this.recordingStartedAt === null) return;
    const sensitive = input.sensitive === true;
    if (sensitive && (input.action !== "set_value" || input.secretKey === undefined)) {
      throw new Error("Sensitive set_value recording requires secretKey.");
    }
    const target: RecordingTarget = input.nodeId === undefined || input.nodeId.length === 0
      ? { currentFocus: true }
      : { id: input.nodeId };
    this.activeSteps.push(compact({
      action: input.action,
      requestId: input.requestId,
      eventId: input.eventId,
      target,
      relativeMilliseconds: input.relativeMilliseconds ?? Date.now() - this.recordingStartedAt,
      preRevision: input.preRevision,
      postRevision: input.postRevision,
      waitCondition: input.waitCondition,
      value: sensitive ? undefined : input.value,
      secretKey: sensitive ? input.secretKey : undefined,
      sensitive,
      deltaX: input.deltaX === 0 ? undefined : input.deltaX,
      deltaY: input.deltaY === 0 ? undefined : input.deltaY,
      scrollUnit: input.scrollUnit === 0 ? undefined : input.scrollUnit,
      modifiers: input.modifiers === 0 ? undefined : input.modifiers,
    }) as unknown as RecordingStep);
  }

  stopRecording(): GuaRecording {
    if (this.recordingStartedAt === null) {
      throw new Error("No Gua recording is active.");
    }
    this.recordingStartedAt = null;
    const recording: GuaRecording = { schemaVersion: 1, steps: [...this.activeSteps] };
    validateRecording(recording);
    this.lastRecording = recording;
    return recording;
  }

  getLastRecording(): GuaRecording {
    if (this.lastRecording === null) throw new Error("No completed Gua recording is available.");
    return this.lastRecording;
  }

  async saveRecording(name: string, recording = this.getLastRecording()): Promise<{ path: string; stepCount: number }> {
    validateRecording(recording);
    const fileName = `${safeSegment(name)}.json`;
    const directory = path.join(this.artifactRoot, "recordings");
    await mkdir(directory, { recursive: true });
    const outputPath = path.join(directory, fileName);
    await writeFile(outputPath, `${JSON.stringify(recording, null, 2)}\n`, "utf8");
    return { path: outputPath, stepCount: recording.steps.length };
  }

  async loadRecording(name: string): Promise<GuaRecording> {
    const inputPath = path.join(this.artifactRoot, "recordings", `${safeSegment(name)}.json`);
    const recording = JSON.parse(await readFile(inputPath, "utf8")) as unknown;
    validateRecording(recording);
    this.lastRecording = recording;
    return recording;
  }

  setLastRecording(recording: GuaRecording): void {
    validateRecording(recording);
    this.lastRecording = recording;
  }

  async compareScreenshot(
    screenshot: ScreenshotPayload,
    options: VisualComparisonOptions,
  ): Promise<VisualComparisonResult> {
    const name = safeSegment(options.name);
    const variant = safeSegment(options.variant ?? "default");
    const threshold = boundedNumber(options.pixelThreshold ?? 0, 0, 255, "pixelThreshold");
    const maxRatio = boundedNumber(options.maxDifferentPixelRatio ?? 0, 0, 1, "maxDifferentPixelRatio");
    const actualBytes = decodePngDataUri(screenshot.dataUri);
    const actual = PNG.sync.read(actualBytes);
    const baselineDirectory = path.join(this.artifactRoot, "baselines", name);
    const baselinePath = path.join(baselineDirectory, `${variant}.png`);

    if (options.updateBaseline === true) {
      await mkdir(baselineDirectory, { recursive: true });
      await writeFile(baselinePath, actualBytes);
      const updated: VisualComparisonResult = {
        schemaVersion: 1, name, variant, matched: true, baselineCreated: true,
        width: actual.width, height: actual.height, comparedPixels: 0,
        differentPixels: 0, differentPixelRatio: 0, baselinePath,
      };
      this.lastVisualResult = updated;
      return updated;
    }

    let expectedBytes: Buffer;
    try {
      expectedBytes = await readFile(baselinePath);
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code !== "ENOENT") throw error;
      return this.writeVisualFailure(actualBytes, undefined, actual, {
        schemaVersion: 1, name, variant, matched: false, baselineCreated: false,
        reason: "baseline_missing", width: actual.width, height: actual.height,
        comparedPixels: 0, differentPixels: 0, differentPixelRatio: 0, baselinePath,
      });
    }

    const expected = PNG.sync.read(expectedBytes);
    if (expected.width !== actual.width || expected.height !== actual.height) {
      return this.writeVisualFailure(actualBytes, expectedBytes, actual, {
        schemaVersion: 1, name, variant, matched: false, baselineCreated: false,
        reason: "dimension_mismatch", width: actual.width, height: actual.height,
        comparedPixels: 0, differentPixels: 0, differentPixelRatio: 0, baselinePath,
      });
    }

    const diff = new PNG({ width: actual.width, height: actual.height });
    let comparedPixels = 0;
    let differentPixels = 0;
    for (let y = 0; y < actual.height; y += 1) {
      for (let x = 0; x < actual.width; x += 1) {
        const offset = (y * actual.width + x) * 4;
        if (isMasked(x, y, options.masks ?? [])) {
          copyPixel(actual.data, diff.data, offset, 80);
          continue;
        }
        comparedPixels += 1;
        const different = maxChannelDifference(expected.data, actual.data, offset) > threshold;
        if (different) differentPixels += 1;
        if (different) {
          diff.data[offset] = 255;
          diff.data[offset + 1] = 0;
          diff.data[offset + 2] = 80;
          diff.data[offset + 3] = 255;
        } else {
          copyPixel(actual.data, diff.data, offset, 72);
        }
      }
    }
    const differentPixelRatio = comparedPixels === 0 ? 0 : differentPixels / comparedPixels;
    const result: VisualComparisonResult = {
      schemaVersion: 1, name, variant, matched: differentPixelRatio <= maxRatio,
      baselineCreated: false, reason: differentPixelRatio <= maxRatio ? undefined : "pixel_difference",
      width: actual.width, height: actual.height, comparedPixels, differentPixels,
      differentPixelRatio, baselinePath,
    };
    if (result.matched) {
      this.lastVisualResult = result;
      return result;
    }
    return this.writeVisualFailure(actualBytes, expectedBytes, actual, result, PNG.sync.write(diff));
  }

  async getVisualArtifacts(name?: string): Promise<{ latest: VisualComparisonResult | null; files: string[] }> {
    const root = name === undefined
      ? path.join(this.artifactRoot, "visual")
      : path.join(this.artifactRoot, "visual", safeSegment(name));
    const files = await listFiles(root);
    let latest = name === undefined || this.lastVisualResult?.name === safeSegment(name)
      ? this.lastVisualResult
      : null;
    if (latest === null) {
      const manifests = files.filter((file) => path.basename(file) === "comparison.json");
      const latestPath = manifests.at(-1);
      if (latestPath !== undefined) latest = JSON.parse(await readFile(latestPath, "utf8")) as VisualComparisonResult;
    }
    return { latest, files };
  }

  private async writeVisualFailure(
    actualBytes: Buffer,
    expectedBytes: Buffer | undefined,
    actual: PNG,
    result: VisualComparisonResult,
    diffBytes?: Buffer,
  ): Promise<VisualComparisonResult> {
    const stamp = new Date().toISOString().replace(/[:.]/g, "-");
    const directory = path.join(this.artifactRoot, "visual", result.name, result.variant, stamp);
    await mkdir(directory, { recursive: true });
    const actualPath = path.join(directory, "actual.png");
    const diffPath = path.join(directory, "diff.png");
    const comparisonPath = path.join(directory, "comparison.json");
    await writeFile(actualPath, actualBytes);
    if (expectedBytes !== undefined) await writeFile(path.join(directory, "expected.png"), expectedBytes);
    const fallbackDiff = new PNG({ width: actual.width, height: actual.height });
    if (diffBytes === undefined) {
      for (let offset = 0; offset < actual.data.length; offset += 4) copyPixel(actual.data, fallbackDiff.data, offset, 72);
    }
    await writeFile(diffPath, diffBytes ?? PNG.sync.write(fallbackDiff));
    const completed = compact({ ...result, artifactDirectory: directory, actualPath, diffPath, comparisonPath }) as VisualComparisonResult;
    await writeFile(comparisonPath, `${JSON.stringify(completed, null, 2)}\n`, "utf8");
    this.lastVisualResult = completed;
    return completed;
  }
}

export function validateRecording(value: unknown): asserts value is GuaRecording {
  if (!isRecord(value) || value.schemaVersion !== 1 || !Array.isArray(value.steps)) {
    throw new Error("Recording must use schemaVersion 1 and contain steps.");
  }
  let previous = -1;
  value.steps.forEach((raw, index) => {
    if (!isRecord(raw) || !recordedActions.includes(raw.action as RecordedAction)) {
      throw new Error(`Recording step ${index} has an unsupported action.`);
    }
    if (!Number.isInteger(raw.relativeMilliseconds) || (raw.relativeMilliseconds as number) < previous) {
      throw new Error(`Recording step ${index} has non-monotonic relativeMilliseconds.`);
    }
    previous = raw.relativeMilliseconds as number;
    if (!Number.isInteger(raw.preRevision) || !Number.isInteger(raw.postRevision) ||
        (raw.postRevision as number) < (raw.preRevision as number)) {
      throw new Error(`Recording step ${index} has invalid revisions.`);
    }
    const hasTarget = isRecord(raw.target);
    const hasCoordinate = isRecord(raw.coordinateFallback);
    if (hasTarget === hasCoordinate) throw new Error(`Recording step ${index} requires exactly one target or coordinate fallback.`);
    if (hasTarget) {
      const target = raw.target as Record<string, unknown>;
      const targetChoices = (nonEmpty(target.id) ? 1 : 0) + (nonEmpty(target.role) ? 1 : 0) + (target.currentFocus === true ? 1 : 0);
      if (targetChoices !== 1) throw new Error(`Recording step ${index} target is ambiguous.`);
    } else {
      const coordinate = raw.coordinateFallback as Record<string, unknown>;
      if (typeof coordinate.x !== "number" || !Number.isFinite(coordinate.x) ||
          typeof coordinate.y !== "number" || !Number.isFinite(coordinate.y)) {
        throw new Error(`Recording step ${index} has an invalid coordinate fallback.`);
      }
    }
    if (raw.sensitive === true && (raw.action !== "set_value" || !nonEmpty(raw.secretKey) || raw.value !== undefined)) {
      throw new Error(`Sensitive recording step ${index} is invalid.`);
    }
    if (raw.sensitive !== true && raw.secretKey !== undefined) throw new Error(`Recording step ${index} has an unexpected secretKey.`);
    if ((raw.action === "select" || raw.action === "press_key") && !nonEmpty(raw.value)) {
      throw new Error(`Recording step ${index} requires a value.`);
    }
    if (raw.action === "set_value" && raw.sensitive !== true && typeof raw.value !== "string") {
      throw new Error(`Recording step ${index} requires a value.`);
    }
    if (raw.action === "set_checked" && raw.value !== "true" && raw.value !== "false") {
      throw new Error(`Recording step ${index} requires a boolean value.`);
    }
  });
}

const recordedActions: RecordedAction[] = ["click", "focus", "set_value", "set_checked", "select", "scroll", "press_key"];

function decodePngDataUri(dataUri: string): Buffer {
  const match = /^data:image\/png;base64,([A-Za-z0-9+/=]+)$/.exec(dataUri);
  if (match === null) throw new Error("Visual comparison requires a data:image/png;base64 screenshot.");
  return Buffer.from(match[1], "base64");
}

function safeSegment(value: string): string {
  const safe = value.trim().replace(/[^A-Za-z0-9._-]+/g, "-").replace(/^-+|-+$/g, "");
  if (safe.length === 0 || safe === "." || safe === "..") throw new Error("Artifact name must contain a safe character.");
  return safe;
}

function boundedNumber(value: number, minimum: number, maximum: number, name: string): number {
  if (!Number.isFinite(value) || value < minimum || value > maximum) {
    throw new Error(`${name} must be between ${minimum} and ${maximum}.`);
  }
  return value;
}

function maxChannelDifference(expected: Buffer, actual: Buffer, offset: number): number {
  return Math.max(
    Math.abs(expected[offset] - actual[offset]),
    Math.abs(expected[offset + 1] - actual[offset + 1]),
    Math.abs(expected[offset + 2] - actual[offset + 2]),
    Math.abs(expected[offset + 3] - actual[offset + 3]),
  );
}

function copyPixel(source: Buffer, target: Buffer, offset: number, alpha: number): void {
  target[offset] = source[offset];
  target[offset + 1] = source[offset + 1];
  target[offset + 2] = source[offset + 2];
  target[offset + 3] = alpha;
}

function isMasked(x: number, y: number, masks: MaskRectangle[]): boolean {
  return masks.some((mask) => x >= mask.x && y >= mask.y && x < mask.x + mask.width && y < mask.y + mask.height);
}

async function listFiles(root: string): Promise<string[]> {
  let entries;
  try {
    entries = await readdir(root, { withFileTypes: true });
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code === "ENOENT") return [];
    throw error;
  }
  const nested = await Promise.all(entries.map(async (entry) => {
    const candidate = path.join(root, entry.name);
    return entry.isDirectory() ? listFiles(candidate) : [candidate];
  }));
  return nested.flat().sort();
}

function compact<T extends Record<string, unknown>>(value: T): Partial<T> {
  return Object.fromEntries(Object.entries(value).filter(([, item]) => item !== undefined)) as Partial<T>;
}

function nonEmpty(value: unknown): value is string {
  return typeof value === "string" && value.length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
