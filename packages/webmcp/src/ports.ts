import { GuaWebError, type GuaBridgeCallOptions, type GuaBrowserBridge, type GuaScreenshot, type GuaUiTree, type GuaWebActionCompletion, type GuaWebActionRequest } from "./index.js";

export interface GuaInPagePort {
  /** A same-page engine call. No network transport or session routing is permitted here. */
  invoke(command: GuaInPageCommand, options?: GuaBridgeCallOptions): Promise<unknown>;
}

export type GuaInPageCommand =
  | { type: "get_ui_tree" }
  | { type: "perform_action"; request: GuaWebActionRequest }
  | { type: "get_screenshot" };

export interface GuaInPageBridgeOptions { screenshot?: boolean }

export function createGuaInPageBridge(port: GuaInPagePort, options: GuaInPageBridgeOptions = {}): GuaBrowserBridge {
  const bridge: GuaBrowserBridge = {
    getUiTree: async () => parseTree(await invoke(port, { type: "get_ui_tree" })),
    performAction: async (request, callOptions) => parseCompletion(await invoke(port, { type: "perform_action", request }, callOptions)),
  };
  if (options.screenshot) bridge.getScreenshot = async () => parseScreenshot(await invoke(port, { type: "get_screenshot" }));
  return bridge;
}

export function createGodotWebBridge(portName = "__guaGodotWebPort", options: GuaInPageBridgeOptions = {}): GuaBrowserBridge {
  return createGuaInPageBridge(globalPort(portName, "Godot Web Export"), options);
}

export function createUnityWebGlBridge(portName = "__guaUnityWebPort", options: GuaInPageBridgeOptions = {}): GuaBrowserBridge {
  return createGuaInPageBridge(globalPort(portName, "Unity WebGL"), options);
}

function globalPort(name: string, engine: string): GuaInPagePort {
  const port = (globalThis as Record<string, unknown>)[name] as GuaInPagePort | undefined;
  if (!port || typeof port.invoke !== "function") {
    throw new GuaWebError("engine_unsupported", `${engine} did not install the same-page Gua port '${name}'.`);
  }
  return port;
}

async function invoke(port: GuaInPagePort, command: GuaInPageCommand, options?: GuaBridgeCallOptions): Promise<unknown> {
  try { return await port.invoke(command, options); }
  catch (error) {
    if (error instanceof GuaWebError) throw error;
    const record = asRecord(error);
    if (typeof record?.code === "string" && typeof record.message === "string") {
      const code = isErrorCode(record.code) ? record.code : "action_failed";
      throw new GuaWebError(code, record.message, { engineCode: record.code });
    }
    throw error;
  }
}

function parseTree(value: unknown): GuaUiTree {
  const parsed = parseJson(value);
  const record = asRecord(parsed);
  if (!record || typeof record.screen !== "string" || !Array.isArray(record.nodes)) {
    throw new GuaWebError("invalid_request", "The engine returned an invalid protocol UI tree.");
  }
  return parsed as GuaUiTree;
}

function parseCompletion(value: unknown): GuaWebActionCompletion {
  const parsed = parseJson(value);
  const record = asRecord(parsed);
  if (!record || typeof record.requestId !== "number" || typeof record.succeeded !== "boolean") {
    throw new GuaWebError("invalid_request", "The engine returned an invalid action completion.");
  }
  return parsed as GuaWebActionCompletion;
}

function parseScreenshot(value: unknown): GuaScreenshot {
  const parsed = parseJson(value);
  const record = asRecord(parsed);
  if (!record || typeof record.dataUri !== "string" || typeof record.width !== "number" || typeof record.height !== "number") {
    throw new GuaWebError("engine_unsupported", "The engine returned no supported screenshot payload.");
  }
  return parsed as GuaScreenshot;
}

function parseJson(value: unknown): unknown {
  if (typeof value !== "string") return value;
  try { return JSON.parse(value); }
  catch { throw new GuaWebError("invalid_request", "The engine returned malformed JSON."); }
}

function asRecord(value: unknown): Record<string, unknown> | undefined {
  return value !== null && typeof value === "object" ? value as Record<string, unknown> : undefined;
}

function isErrorCode(value: string): value is GuaWebError["code"] {
  return [
    "webmcp_unsupported", "engine_unsupported", "invalid_request", "node_not_found", "hidden", "disabled",
    "unsupported_action", "action_failed", "timeout", "aborted",
  ].includes(value);
}
