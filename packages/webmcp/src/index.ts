export * from "./tool-definitions.js";
export * from "./ports.js";

export interface GuaBounds { x: number; y: number; w: number; h: number }
export interface GuaNodeState {
  focused?: boolean;
  hovered?: boolean;
  pressed?: boolean;
  checked?: boolean;
  selected?: boolean;
  caretPosition?: number;
  selectionStart?: number;
  selectionEnd?: number;
  scrollX?: number;
  scrollY?: number;
  scrollMaxX?: number;
  scrollMaxY?: number;
  rangeValue?: number;
  rangeMin?: number;
  rangeMax?: number;
  selectedIndex?: number;
  value?: number | string | boolean | null;
}
export interface GuaNode {
  id: string;
  parentId?: string;
  role: string;
  label?: string;
  text?: string;
  value?: number | string | boolean | null;
  visible: boolean;
  enabled: boolean;
  bounds: GuaBounds;
  state?: GuaNodeState;
  actions: string[];
}
export interface GuaUiTree {
  schemaVersion: 2;
  sessionEpoch?: number;
  frameSequence: number;
  revision: number;
  screen: string;
  nodes: GuaNode[];
}
export interface GuaScreenshot { dataUri: string; width: number; height: number }

export type GuaWebAction = "click" | "focus" | "set_value" | "set_checked" | "select" | "scroll" | "press_key";
export type GuaWebActionRequest =
  | { action: "click" | "focus"; nodeId: string }
  | { action: "set_value"; nodeId: string; value: string; sensitive?: boolean }
  | { action: "set_checked"; nodeId: string; checked: boolean }
  | { action: "select"; nodeId: string; value: string }
  | { action: "scroll"; nodeId: string; deltaX: number; deltaY: number; scrollUnit?: 0 | 1 }
  | { action: "press_key"; nodeId?: string; key: string; modifiers?: number };
export interface GuaWebActionCompletion {
  requestId: number;
  action: GuaWebAction | number;
  succeeded: boolean;
  error?: string | number;
  nodeId?: string;
  value?: string;
  sensitive?: boolean;
  sessionEpoch?: number;
  frameSequence?: number;
  revision?: number;
}

export interface GuaBridgeCallOptions { signal?: AbortSignal; timeoutMs?: number }

/** Implemented by the engine adapter in the same page. performAction resolves only after host completion. */
export interface GuaBrowserBridge {
  getUiTree(): Promise<GuaUiTree>;
  performAction(request: GuaWebActionRequest, options?: GuaBridgeCallOptions): Promise<GuaWebActionCompletion>;
  getScreenshot?(): Promise<GuaScreenshot>;
}

export type GuaWebErrorCode =
  | "webmcp_unsupported" | "engine_unsupported" | "invalid_request" | "node_not_found"
  | "hidden" | "disabled" | "unsupported_action" | "action_failed" | "timeout" | "aborted";

export class GuaWebError extends Error {
  constructor(public readonly code: GuaWebErrorCode, message: string, public readonly details?: Record<string, unknown>) {
    super(message);
    this.name = "GuaWebError";
  }
  toJSON() { return { code: this.code, message: this.message, ...(this.details ? { details: this.details } : {}) }; }
}

interface WebMcpExecutionOptions { signal?: AbortSignal }
interface WebMcpToolRegistration {
  name: string;
  description: string;
  inputSchema: Record<string, unknown>;
  execute(input: Record<string, unknown>, options?: WebMcpExecutionOptions): Promise<unknown>;
}
interface ModelContext {
  registerTool(tool: WebMcpToolRegistration, options?: { signal?: AbortSignal }): Promise<void> | void;
}

export interface RegisterGuaWebMcpOptions {
  document?: Document & { modelContext?: ModelContext };
  pollIntervalMs?: number;
  defaultTimeoutMs?: number;
}
export interface GuaWebMcpRegistration {
  supported: boolean;
  registeredTools: string[];
  unregister(): void;
  error?: GuaWebError;
}

import { guaWebMcpToolDefinitions, maxBrowserTimerDelayMs } from "./tool-definitions.js";

export async function registerGuaWebMcp(
  bridge: GuaBrowserBridge,
  options: RegisterGuaWebMcpOptions = {},
): Promise<GuaWebMcpRegistration> {
  const documentValue = options.document ?? (globalThis as { document?: Document }).document as RegisterGuaWebMcpOptions["document"];
  const modelContext = documentValue?.modelContext;
  const controller = new AbortController();
  const registeredTools: string[] = [];
  if (!modelContext || typeof modelContext.registerTool !== "function") {
    return {
      supported: false,
      registeredTools,
      unregister: () => controller.abort(),
      error: new GuaWebError("webmcp_unsupported", "This browser does not expose document.modelContext.registerTool()."),
    };
  }

  const definitions = guaWebMcpToolDefinitions.filter((definition) => definition.name !== "get_screenshot" || bridge.getScreenshot);
  try {
    const pollIntervalMs = timerDelay(options.pollIntervalMs ?? 25, "pollIntervalMs");
    const defaultTimeoutMs = timerDelay(options.defaultTimeoutMs ?? 5000, "defaultTimeoutMs");
    for (const definition of definitions) {
      await modelContext.registerTool({
        ...definition,
        execute: async (input, executionOptions) => executeTool(
          definition.name,
          input ?? {},
          bridge,
          executionOptions?.signal,
          pollIntervalMs,
          defaultTimeoutMs,
        ),
      }, { signal: controller.signal });
      registeredTools.push(definition.name);
    }
  } catch (error) {
    controller.abort();
    const message = error instanceof Error ? error.message : "The browser rejected WebMCP tool registration.";
    return {
      supported: false,
      registeredTools: [],
      unregister: () => controller.abort(),
      error: error instanceof GuaWebError ? error : new GuaWebError("webmcp_unsupported", message),
    };
  }
  return { supported: true, registeredTools, unregister: () => controller.abort() };
}

async function executeTool(
  name: string,
  input: Record<string, unknown>,
  bridge: GuaBrowserBridge,
  signal: AbortSignal | undefined,
  pollIntervalMs: number,
  defaultTimeoutMs: number,
): Promise<unknown> {
  try {
    throwIfAborted(signal);
    if (name === "get_ui_tree") {
      return toolResult(await withTimeout(
        bridge.getUiTree(),
        defaultTimeoutMs,
        signal,
        "Timed out reading the current Gua semantic UI tree.",
      ));
    }
    if (name === "get_screenshot") {
      if (!bridge.getScreenshot) throw new GuaWebError("engine_unsupported", "The engine bridge does not support screenshots.");
      return toolResult(await withTimeout(
        bridge.getScreenshot(),
        defaultTimeoutMs,
        signal,
        "Timed out reading the latest Gua screenshot.",
      ));
    }
    if (name === "wait_for_node") {
      const nodeId = requiredString(input, "nodeId");
      const timeoutMs = optionalInteger(input, "timeoutMs", defaultTimeoutMs, 0, maxBrowserTimerDelayMs);
      return toolResult(await waitForNode(bridge, nodeId, timeoutMs, pollIntervalMs, signal));
    }
    const request = actionRequest(name, input);
    await validateAction(bridge, request, defaultTimeoutMs, signal);
    throwIfAborted(signal);
    const completion = await performActionWithCancellation(
      bridge,
      request,
      defaultTimeoutMs,
      signal,
      `Timed out waiting for ${request.action} host completion.`,
    );
    if (!completion || typeof completion.requestId !== "number") {
      throw new GuaWebError("invalid_request", "The engine bridge returned no request-correlated completion.");
    }
    if (!completion.succeeded) {
      throw new GuaWebError(completionErrorCode(completion.error), `The host rejected ${request.action} for ${request.nodeId ?? "current focus"}.`, {
        requestId: completion.requestId,
        hostError: completion.error ?? "unknown",
      });
    }
    const safeCompletion = (request.action === "set_value" && request.sensitive) || completion.sensitive
      ? { ...completion, value: "", sensitive: true }
      : completion;
    return toolResult(safeCompletion);
  } catch (error) {
    const normalized = normalizeError(error, input.sensitive === undefined || input.sensitive === false ? undefined : String(input.value ?? ""));
    return { content: [{ type: "text", text: JSON.stringify({ error: normalized.toJSON() }) }], isError: true };
  }
}

async function performActionWithCancellation(
  bridge: GuaBrowserBridge,
  request: GuaWebActionRequest,
  timeoutMs: number,
  signal: AbortSignal | undefined,
  timeoutMessage: string,
): Promise<GuaWebActionCompletion> {
  throwIfAborted(signal);
  const controller = new AbortController();
  const forwardAbort = () => controller.abort();
  signal?.addEventListener("abort", forwardAbort, { once: true });
  if (signal?.aborted) {
    signal.removeEventListener("abort", forwardAbort);
    throw new GuaWebError("aborted", "The Gua WebMCP tool call was aborted.");
  }
  try {
    return await withTimeout(
      bridge.performAction(request, { signal: controller.signal, timeoutMs }),
      timeoutMs,
      signal,
      timeoutMessage,
    );
  } catch (error) {
    controller.abort();
    throw error;
  } finally {
    signal?.removeEventListener("abort", forwardAbort);
  }
}

async function validateAction(
  bridge: GuaBrowserBridge,
  request: GuaWebActionRequest,
  timeoutMs: number,
  signal?: AbortSignal,
): Promise<void> {
  if (request.action === "press_key" && !request.nodeId) return;
  const tree = await withTimeout(
    bridge.getUiTree(),
    timeoutMs,
    signal,
    "Timed out reading the current Gua semantic UI tree before dispatch.",
  );
  const node = tree.nodes.find((candidate) => candidate.id === request.nodeId);
  if (!node) throw new GuaWebError("node_not_found", `Gua node not found: ${request.nodeId}`);
  if (!node.visible) throw new GuaWebError("hidden", `Gua node is hidden: ${request.nodeId}`);
  if (!node.enabled) throw new GuaWebError("disabled", `Gua node is disabled: ${request.nodeId}`);
  if (!node.actions.includes(request.action)) {
    throw new GuaWebError("unsupported_action", `Gua node does not support ${request.action}: ${request.nodeId}`);
  }
}

async function waitForNode(bridge: GuaBrowserBridge, nodeId: string, timeoutMs: number, pollIntervalMs: number, signal?: AbortSignal): Promise<GuaNode> {
  const deadline = performance.now() + timeoutMs;
  do {
    throwIfAborted(signal);
    const remainingMs = Math.max(0, deadline - performance.now());
    const tree = await withTimeout(
      bridge.getUiTree(),
      remainingMs,
      signal,
      `Timed out waiting for Gua node: ${nodeId}`,
    );
    const node = tree.nodes.find((candidate) => candidate.id === nodeId);
    if (node) return node;
    if (performance.now() >= deadline) break;
    await delay(Math.min(pollIntervalMs, Math.max(0, deadline - performance.now())), signal);
  } while (true);
  throw new GuaWebError("timeout", `Timed out waiting for Gua node: ${nodeId}`, { timeoutMs });
}

function actionRequest(name: string, input: Record<string, unknown>): GuaWebActionRequest {
  const action = name.replace(/_node$/, "") as GuaWebAction;
  switch (action) {
    case "click":
    case "focus":
      return { action, nodeId: requiredString(input, "nodeId") };
    case "set_value":
      return {
        action,
        nodeId: requiredString(input, "nodeId"),
        value: requiredString(input, "value", true),
        sensitive: input.sensitive === undefined ? false : requiredBoolean(input, "sensitive"),
      };
    case "set_checked":
      return { action, nodeId: requiredString(input, "nodeId"), checked: requiredBoolean(input, "checked") };
    case "select":
      return { action, nodeId: requiredString(input, "nodeId"), value: requiredString(input, "value") };
    case "scroll":
      return {
        action,
        nodeId: requiredString(input, "nodeId"),
        deltaX: requiredNumber(input, "deltaX"),
        deltaY: requiredNumber(input, "deltaY"),
        scrollUnit: optionalInteger(input, "scrollUnit", 0, 0, 1) as 0 | 1,
      };
    case "press_key":
      return {
        action,
        ...(input.nodeId === undefined ? {} : { nodeId: requiredString(input, "nodeId") }),
        key: requiredString(input, "key"),
        modifiers: optionalInteger(input, "modifiers", 0, 0, 15),
      };
    default:
      throw new GuaWebError("invalid_request", `Unknown Gua WebMCP tool: ${name}`);
  }
}

function toolResult(value: unknown) { return { content: [{ type: "text", text: JSON.stringify(value) }] }; }
function requiredString(input: Record<string, unknown>, key: string, allowEmpty = false): string {
  if (typeof input[key] !== "string" || (!allowEmpty && (input[key] as string).length === 0)) {
    throw new GuaWebError("invalid_request", `${key} must be ${allowEmpty ? "a string" : "a non-empty string"}.`);
  }
  return input[key] as string;
}
function requiredBoolean(input: Record<string, unknown>, key: string): boolean {
  if (typeof input[key] !== "boolean") throw new GuaWebError("invalid_request", `${key} must be a boolean.`);
  return input[key] as boolean;
}
function requiredNumber(input: Record<string, unknown>, key: string): number {
  if (typeof input[key] !== "number" || !Number.isFinite(input[key])) throw new GuaWebError("invalid_request", `${key} must be a finite number.`);
  return input[key] as number;
}
function optionalInteger(input: Record<string, unknown>, key: string, fallback: number, minimum: number, maximum = Number.MAX_SAFE_INTEGER): number {
  const value = input[key] ?? fallback;
  if (!Number.isInteger(value) || (value as number) < minimum || (value as number) > maximum) {
    throw new GuaWebError("invalid_request", `${key} must be an integer from ${minimum} to ${maximum}.`);
  }
  return value as number;
}
function timerDelay(value: number, key: string): number {
  if (!Number.isInteger(value) || value < 0 || value > maxBrowserTimerDelayMs) {
    throw new GuaWebError("invalid_request", `${key} must be an integer from 0 to ${maxBrowserTimerDelayMs}.`);
  }
  return value;
}
function throwIfAborted(signal?: AbortSignal): void {
  if (signal?.aborted) throw new GuaWebError("aborted", "The Gua WebMCP tool call was aborted.");
}
function delay(milliseconds: number, signal?: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    let settled = false;
    const finish = (settle: () => void) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      signal?.removeEventListener("abort", aborted);
      settle();
    };
    const aborted = () => finish(() => reject(new GuaWebError("aborted", "The Gua WebMCP tool call was aborted.")));
    const timer = setTimeout(() => finish(resolve), milliseconds);
    signal?.addEventListener("abort", aborted, { once: true });
    if (signal?.aborted) aborted();
  });
}
function withTimeout<T>(operation: Promise<T>, timeoutMs: number, signal: AbortSignal | undefined, message: string): Promise<T> {
  throwIfAborted(signal);
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => finish(() => reject(new GuaWebError("timeout", message, { timeoutMs }))), Math.max(0, timeoutMs));
    const aborted = () => finish(() => reject(new GuaWebError("aborted", "The Gua WebMCP tool call was aborted.")));
    const finish = (settle: () => void) => {
      clearTimeout(timer);
      signal?.removeEventListener("abort", aborted);
      settle();
    };
    signal?.addEventListener("abort", aborted, { once: true });
    operation.then((value) => finish(() => resolve(value)), (error) => finish(() => reject(error)));
  });
}
function normalizeError(error: unknown, secret?: string): GuaWebError {
  let message = error instanceof Error ? error.message : "The engine bridge failed.";
  if (secret) message = message.split(secret).join("[REDACTED]");
  if (error instanceof GuaWebError) {
    const details = secret && error.details ? redactStrings(error.details, secret) as Record<string, unknown> : error.details;
    return new GuaWebError(error.code, message, details);
  }
  return new GuaWebError("action_failed", message);
}

function completionErrorCode(error: GuaWebActionCompletion["error"]): GuaWebErrorCode {
  switch (error) {
    case -2:
    case "-2":
    case "node_not_found":
      return "node_not_found";
    case -3:
    case "-3":
    case "hidden":
      return "hidden";
    case -4:
    case "-4":
    case "disabled":
      return "disabled";
    case -5:
    case "-5":
    case "unsupported":
    case "unsupported_action":
      return "unsupported_action";
    case -6:
    case "-6":
    case "invalid_value":
      return "invalid_request";
    default:
      return "action_failed";
  }
}

function redactStrings(value: unknown, secret: string): unknown {
  if (typeof value === "string") return value.split(secret).join("[REDACTED]");
  if (Array.isArray(value)) return value.map((item) => redactStrings(item, secret));
  if (value && typeof value === "object") {
    return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, redactStrings(item, secret)]));
  }
  return value;
}
