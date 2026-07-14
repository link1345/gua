import type { GuaNode, GuaUiTree } from "./core";

export type RecordedAction = "click" | "focus" | "set_value" | "set_checked" | "select" | "scroll" | "press_key";

export interface SemanticActionInput {
  action: RecordedAction;
  nodeId?: string;
  value?: string;
  checked?: boolean;
  key?: string;
  modifiers?: number;
  deltaX?: number;
  deltaY?: number;
  scrollUnit?: number;
  sensitive?: boolean;
  secretKey?: string;
}

export interface RecordingStep {
  action: RecordedAction;
  requestId?: number;
  eventId?: number;
  target?: { id?: string; role?: string; name?: string; scope?: string; currentFocus?: true };
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

export interface GuaRecording { schemaVersion: 1; steps: RecordingStep[] }

export interface ActionOutcome {
  requestId?: number;
  completion?: { requestId: number; succeeded: boolean; error: number; revision: number };
}

export class InspectorRecorder {
  private startedAt: number | null = null;
  private steps: RecordingStep[] = [];

  start(): void {
    if (this.startedAt !== null) throw new Error("A recording is already active.");
    this.startedAt = Date.now();
    this.steps = [];
  }

  get active(): boolean { return this.startedAt !== null; }

  record(input: SemanticActionInput, outcome: ActionOutcome, preRevision: number, postRevision: number): void {
    if (this.startedAt === null) return;
    if (input.sensitive === true && (input.action !== "set_value" || input.secretKey === undefined)) {
      throw new Error("Sensitive set_value requires secretKey.");
    }
    this.steps.push(compact({
      action: input.action,
      requestId: outcome.requestId,
      target: input.nodeId === undefined ? { currentFocus: true as const } : { id: input.nodeId },
      relativeMilliseconds: Date.now() - this.startedAt,
      preRevision,
      postRevision,
      value: input.sensitive === true ? undefined : actionValue(input),
      secretKey: input.sensitive === true ? input.secretKey : undefined,
      sensitive: input.sensitive === true,
      deltaX: input.deltaX === 0 ? undefined : input.deltaX,
      deltaY: input.deltaY === 0 ? undefined : input.deltaY,
      scrollUnit: input.scrollUnit === 0 ? undefined : input.scrollUnit,
      modifiers: input.modifiers === 0 ? undefined : input.modifiers,
    }) as unknown as RecordingStep);
  }

  stop(): GuaRecording {
    if (this.startedAt === null) throw new Error("No recording is active.");
    this.startedAt = null;
    const recording: GuaRecording = { schemaVersion: 1, steps: [...this.steps] };
    validateRecording(recording);
    return recording;
  }
}

export async function replayRecording(
  recording: GuaRecording,
  getTree: () => Promise<GuaUiTree>,
  perform: (action: SemanticActionInput) => Promise<ActionOutcome>,
  secrets: Record<string, string> = {},
): Promise<void> {
  validateRecording(recording);
  let previous = 0;
  for (const step of recording.steps) {
    if (step.waitCondition !== undefined) await waitForCondition(getTree, step.waitCondition, 10000);
    else if (step.relativeMilliseconds > previous) await delay(step.relativeMilliseconds - previous);
    previous = step.relativeMilliseconds;
    const nodeId = await resolveTarget(step, await getTree());
    const value = step.sensitive ? secrets[step.secretKey as string] : step.value;
    if (step.sensitive && value === undefined) throw new Error(`Missing secret '${step.secretKey}'.`);
    await perform({
      action: step.action, nodeId, value,
      checked: step.action === "set_checked" ? value === "true" : undefined,
      key: step.action === "press_key" ? value : undefined,
      modifiers: step.modifiers, deltaX: step.deltaX, deltaY: step.deltaY,
      scrollUnit: step.scrollUnit, sensitive: step.sensitive, secretKey: step.secretKey,
    });
  }
}

export interface BrowserVisualResult {
  matched: boolean;
  reason?: "dimension_mismatch" | "pixel_difference";
  width: number;
  height: number;
  comparedPixels: number;
  differentPixels: number;
  differentPixelRatio: number;
  actualDataUri: string;
  expectedDataUri: string;
  diffDataUri: string;
  comparisonJson: string;
}

export async function compareImages(
  expectedDataUri: string,
  actualDataUri: string,
  pixelThreshold = 0,
  maxDifferentPixelRatio = 0,
): Promise<BrowserVisualResult> {
  const [expected, actual] = await Promise.all([loadImage(expectedDataUri), loadImage(actualDataUri)]);
  const width = actual.naturalWidth;
  const height = actual.naturalHeight;
  if (expected.naturalWidth !== width || expected.naturalHeight !== height) {
    return visualResult(false, "dimension_mismatch", width, height, 0, 0, actualDataUri, expectedDataUri, actualDataUri);
  }
  const expectedPixels = imageData(expected, width, height);
  const actualPixels = imageData(actual, width, height);
  const diffCanvas = document.createElement("canvas");
  diffCanvas.width = width;
  diffCanvas.height = height;
  const context = requiredContext(diffCanvas);
  const diff = context.createImageData(width, height);
  let differentPixels = 0;
  for (let offset = 0; offset < actualPixels.data.length; offset += 4) {
    const different = Math.max(
      Math.abs(expectedPixels.data[offset] - actualPixels.data[offset]),
      Math.abs(expectedPixels.data[offset + 1] - actualPixels.data[offset + 1]),
      Math.abs(expectedPixels.data[offset + 2] - actualPixels.data[offset + 2]),
      Math.abs(expectedPixels.data[offset + 3] - actualPixels.data[offset + 3]),
    ) > pixelThreshold;
    if (different) differentPixels += 1;
    diff.data[offset] = different ? 255 : actualPixels.data[offset];
    diff.data[offset + 1] = different ? 0 : actualPixels.data[offset + 1];
    diff.data[offset + 2] = different ? 80 : actualPixels.data[offset + 2];
    diff.data[offset + 3] = different ? 255 : 72;
  }
  context.putImageData(diff, 0, 0);
  const comparedPixels = width * height;
  const ratio = comparedPixels === 0 ? 0 : differentPixels / comparedPixels;
  return visualResult(
    ratio <= maxDifferentPixelRatio,
    ratio <= maxDifferentPixelRatio ? undefined : "pixel_difference",
    width, height, comparedPixels, differentPixels, actualDataUri, expectedDataUri, diffCanvas.toDataURL("image/png"),
  );
}

export function validateRecording(value: unknown): asserts value is GuaRecording {
  if (!isRecord(value) || value.schemaVersion !== 1 || !Array.isArray(value.steps)) {
    throw new Error("Recording must use schemaVersion 1 and contain steps.");
  }
  let previous = -1;
  value.steps.forEach((raw, index) => {
    if (!isRecord(raw) || typeof raw.action !== "string" || !actions.includes(raw.action as RecordedAction)) {
      throw new Error(`Recording step ${index} has an unsupported action.`);
    }
    if (!Number.isInteger(raw.relativeMilliseconds) || (raw.relativeMilliseconds as number) < previous) {
      throw new Error(`Recording step ${index} has invalid timing.`);
    }
    previous = raw.relativeMilliseconds as number;
    const hasTarget = isRecord(raw.target);
    const hasCoordinate = isRecord(raw.coordinateFallback);
    if (hasTarget === hasCoordinate) throw new Error(`Recording step ${index} requires exactly one target or coordinate fallback.`);
    if (!Number.isInteger(raw.preRevision) || !Number.isInteger(raw.postRevision)) throw new Error(`Recording step ${index} has invalid revisions.`);
  });
}

function visualResult(
  matched: boolean,
  reason: BrowserVisualResult["reason"],
  width: number,
  height: number,
  comparedPixels: number,
  differentPixels: number,
  actualDataUri: string,
  expectedDataUri: string,
  diffDataUri: string,
): BrowserVisualResult {
  const result = compact({
    matched, reason, width, height, comparedPixels, differentPixels,
    differentPixelRatio: comparedPixels === 0 ? 0 : differentPixels / comparedPixels,
    actualDataUri, expectedDataUri, diffDataUri,
  }) as unknown as Omit<BrowserVisualResult, "comparisonJson">;
  return { ...result, comparisonJson: JSON.stringify({ schemaVersion: 1, ...result, actualDataUri: undefined, expectedDataUri: undefined, diffDataUri: undefined }, null, 2) };
}

function imageData(image: HTMLImageElement, width: number, height: number): ImageData {
  const canvas = document.createElement("canvas");
  canvas.width = width;
  canvas.height = height;
  const context = requiredContext(canvas);
  context.drawImage(image, 0, 0);
  return context.getImageData(0, 0, width, height);
}

function requiredContext(canvas: HTMLCanvasElement): CanvasRenderingContext2D {
  const context = canvas.getContext("2d", { willReadFrequently: true });
  if (context === null) throw new Error("Canvas 2D is unavailable.");
  return context;
}

function loadImage(source: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => resolve(image);
    image.onerror = () => reject(new Error("Screenshot image could not be decoded."));
    image.src = source;
  });
}

async function resolveTarget(step: RecordingStep, tree: GuaUiTree): Promise<string | undefined> {
  if (step.coordinateFallback !== undefined) throw new Error("Coordinate fallback replay is disabled; use a semantic target.");
  const target = step.target as NonNullable<RecordingStep["target"]>;
  if (target.currentFocus === true) return undefined;
  if (target.id !== undefined) return target.id;
  const matches = tree.nodes.filter((node) => node.role === target.role &&
    (target.name === undefined || node.label === target.name) &&
    (target.scope === undefined || descendant(node, target.scope, tree.nodes)));
  if (matches.length !== 1) throw new Error(`Recording target matched ${matches.length} nodes.`);
  return matches[0]?.id;
}

function descendant(node: GuaNode, scope: string, nodes: GuaNode[]): boolean {
  let parentId = node.parentId;
  while (parentId !== undefined) {
    if (parentId === scope) return true;
    parentId = nodes.find((candidate) => candidate.id === parentId)?.parentId;
  }
  return false;
}

async function waitForCondition(getTree: () => Promise<GuaUiTree>, condition: string, timeoutMs: number): Promise<void> {
  const [kind, encodedId, encodedValue] = condition.split(":");
  if (encodedId === undefined) throw new Error(`Unsupported wait condition: ${condition}`);
  const id = decodeURIComponent(encodedId);
  const value = encodedValue === undefined ? undefined : decodeURIComponent(encodedValue);
  const started = Date.now();
  while (Date.now() - started <= timeoutMs) {
    const node = (await getTree()).nodes.find((candidate) => candidate.id === id);
    const match = kind === "visible" ? node?.visible === true
      : kind === "hidden" ? node === undefined || node.visible === false
      : kind === "enabled" ? node?.enabled === true
      : kind === "disabled" ? node !== undefined && !node.enabled
      : kind === "focused" ? node?.state?.focused === true
      : kind === "unfocused" ? node !== undefined && node.state?.focused !== true
      : kind === "checked" ? node?.state?.checked === true
      : kind === "unchecked" ? node !== undefined && node.state?.checked !== true
      : kind === "text" ? node?.label === value
      : kind === "value" ? String(node?.state?.value ?? "") === value
      : false;
    if (match) return;
    await delay(50);
  }
  throw new Error(`Timed out waiting for ${condition}.`);
}

function actionValue(input: SemanticActionInput): string | undefined {
  if (input.action === "set_checked") return String(input.checked === true);
  if (input.action === "press_key") return input.key;
  if (input.action === "set_value" || input.action === "select") return input.value;
  return undefined;
}

function compact<T extends Record<string, unknown>>(value: T): Partial<T> {
  return Object.fromEntries(Object.entries(value).filter(([, item]) => item !== undefined)) as Partial<T>;
}

function delay(milliseconds: number): Promise<void> { return new Promise((resolve) => window.setTimeout(resolve, milliseconds)); }
function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === "object" && value !== null && !Array.isArray(value); }
const actions: RecordedAction[] = ["click", "focus", "set_value", "set_checked", "select", "scroll", "press_key"];
