import { GuaWebError, type GuaBridgeCallOptions, type GuaBrowserBridge, type GuaScreenshot, type GuaUiTree, type GuaWebActionCompletion, type GuaWebActionRequest } from "./index.js";
import {
  parseWorldObjectTree,
  parseWorldQueryResult,
  type GuaWorldSelector,
} from "gua-world-tools";

export interface GuaInPagePort {
  /** A same-page engine call. No network transport or session routing is permitted here. */
  invoke(command: GuaInPageCommand, options?: GuaBridgeCallOptions): Promise<unknown>;
}

export type GuaInPageCommand =
  | { type: "get_ui_tree" }
  | { type: "get_world_object_tree" }
  | ({ type: "query_world_objects" } & GuaWorldWireSelector)
  | { type: "perform_action"; request: GuaWebActionRequest }
  | { type: "get_screenshot" };

export interface GuaInPageBridgeOptions { screenshot?: boolean; world?: boolean }

interface GuaWorldWireSelector {
  worldId?: string;
  kind?: string;
  label?: string;
  tag?: string;
  parentId?: string;
  directChild?: 0 | 1;
  visibleToPlayer?: 0 | 1 | 2;
  active?: 0 | 1 | 2;
  stateKey?: string;
  stateType?: 0 | 1 | 2 | 3;
  stateString?: string;
  stateNumber?: number;
  stateBool?: boolean;
}

export function createGuaInPageBridge(port: GuaInPagePort, options: GuaInPageBridgeOptions = {}): GuaBrowserBridge {
  const bridge: GuaBrowserBridge = {
    getUiTree: async () => parseTree(await invoke(port, { type: "get_ui_tree" })),
    performAction: async (request, callOptions) => parseCompletion(await invoke(port, { type: "perform_action", request }, callOptions)),
  };
  if (options.screenshot) bridge.getScreenshot = async () => parseScreenshot(await invoke(port, { type: "get_screenshot" }));
  if (options.world) {
    bridge.getWorldObjectTree = async (callOptions) => parseWorldObjectTree(await invoke(port, { type: "get_world_object_tree" }, callOptions));
    bridge.findWorldObjects = async (selector, callOptions) => parseWorldQueryResult(await invoke(port, worldQueryCommand(selector), callOptions));
  }
  return bridge;
}

export function createGodotWebBridge(portName = "__guaGodotWebPort", options: GuaInPageBridgeOptions = {}): GuaBrowserBridge {
  return createGuaInPageBridge(globalPort(portName, "Godot Web Export"), { ...options, world: options.world ?? true });
}

export function createUnityWebGlBridge(portName = "__guaUnityWebPort", options: GuaInPageBridgeOptions = {}): GuaBrowserBridge {
  return createGuaInPageBridge(globalPort(portName, "Unity WebGL"), { ...options, world: options.world ?? true });
}

function worldQueryCommand(selector: GuaWorldSelector): GuaInPageCommand {
  const state = selector.state;
  return compact({
    type: "query_world_objects" as const,
    worldId: selector.id,
    kind: selector.kind,
    label: selector.label,
    tag: selector.tag,
    parentId: selector.parentId,
    directChild: selector.directChild ? 1 as const : undefined,
    visibleToPlayer: filter(selector.visibleToPlayer),
    active: filter(selector.active),
    stateKey: state?.key,
    stateType: state === undefined ? undefined : state.value === null ? 0 as const
      : typeof state.value === "string" ? 1 as const : typeof state.value === "number" ? 2 as const : 3 as const,
    stateString: typeof state?.value === "string" ? state.value : undefined,
    stateNumber: typeof state?.value === "number" ? state.value : undefined,
    stateBool: typeof state?.value === "boolean" ? state.value : undefined,
  }) as GuaInPageCommand;
}

function filter(value: boolean | undefined): 0 | 1 | 2 | undefined {
  return value === undefined ? undefined : value ? 2 : 1;
}

function compact<T extends Record<string, unknown>>(value: T): T {
  return Object.fromEntries(Object.entries(value).filter(([, item]) => item !== undefined)) as T;
}

function globalPort(name: string, engine: string): GuaInPagePort {
  return {
    async invoke(command, options) {
      const port = (globalThis as Record<string, unknown>)[name] as GuaInPagePort | undefined;
      if (!port || typeof port.invoke !== "function") {
        throw new GuaWebError("engine_unsupported", `${engine} did not install the same-page Gua port '${name}'.`);
      }
      return await port.invoke(command, options);
    },
  };
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
  if (!record || !hasOnlyProperties(record, treeProperties) ||
      typeof record.screen !== "string" || record.screen.length === 0 ||
      !Array.isArray(record.nodes) || !record.nodes.every(isProtocolNode)) {
    throw new GuaWebError("invalid_request", "The engine returned an invalid protocol UI tree.");
  }
  const hasMetadata = record.schemaVersion !== undefined || record.sessionEpoch !== undefined ||
    record.frameSequence !== undefined || record.revision !== undefined;
  if (!hasMetadata) {
    return { ...record, schemaVersion: 2, frameSequence: 0, revision: 0 } as unknown as GuaUiTree;
  }
  if (record.schemaVersion !== 2 ||
      (record.sessionEpoch !== undefined &&
        (!Number.isInteger(record.sessionEpoch) || (record.sessionEpoch as number) < 1)) ||
      !Number.isInteger(record.frameSequence) || (record.frameSequence as number) < 0 ||
      !Number.isInteger(record.revision) || (record.revision as number) < 0) {
    throw new GuaWebError("invalid_request", "The engine returned an invalid protocol UI tree.");
  }
  return parsed as GuaUiTree;
}

const treeProperties = new Set(["schemaVersion", "sessionEpoch", "frameSequence", "revision", "screen", "nodes"]);
const nodeProperties = new Set([
  "id", "parentId", "role", "label", "text", "value", "visible", "enabled", "bounds", "state", "actions",
]);
const boundProperties = new Set(["x", "y", "w", "h"]);
const stateProperties = new Set([
  "focused", "hovered", "pressed", "checked", "selected", "caretPosition", "selectionStart", "selectionEnd",
  "scrollX", "scrollY", "scrollMaxX", "scrollMaxY", "rangeValue", "rangeMin", "rangeMax", "selectedIndex", "value",
]);
const nodeRoles = new Set([
  "button", "text", "image", "checkbox", "radio", "slider", "textbox", "list", "listitem", "panel", "screen",
  "dialog", "menu", "menuitem", "combobox", "tablist", "tab", "scrollarea",
]);
const nodeActions = new Set(["click", "focus", "set_value", "set_checked", "select", "scroll", "press_key"]);

function isProtocolNode(value: unknown): boolean {
  const node = asRecord(value);
  const bounds = asRecord(node?.bounds);
  if (!node || !hasOnlyProperties(node, nodeProperties) ||
      !isNonEmptyString(node.id) || !isOptionalNonEmptyString(node.parentId) ||
      typeof node.role !== "string" || !nodeRoles.has(node.role) ||
      !isOptionalString(node.label) || !isOptionalString(node.text) || !isOptionalProtocolValue(node.value) ||
      typeof node.visible !== "boolean" || typeof node.enabled !== "boolean" ||
      !bounds || !hasOnlyProperties(bounds, boundProperties) ||
      !isFiniteNumber(bounds.x) || !isFiniteNumber(bounds.y) ||
      !isNonNegativeNumber(bounds.w) || !isNonNegativeNumber(bounds.h) ||
      !isProtocolState(node.state) || !Array.isArray(node.actions) ||
      !node.actions.every(action => typeof action === "string" && nodeActions.has(action)) ||
      new Set(node.actions).size !== node.actions.length) {
    return false;
  }
  return true;
}

function isProtocolState(value: unknown): boolean {
  if (value === undefined) return true;
  const state = asRecord(value);
  if (!state || !hasOnlyProperties(state, stateProperties)) return false;
  for (const key of ["focused", "hovered", "pressed", "checked", "selected"]) {
    if (state[key] !== undefined && typeof state[key] !== "boolean") return false;
  }
  for (const key of ["caretPosition", "selectionStart", "selectionEnd"]) {
    if (state[key] !== undefined && (!Number.isInteger(state[key]) || (state[key] as number) < 0)) return false;
  }
  for (const key of ["scrollX", "scrollY", "rangeValue", "rangeMin", "rangeMax"]) {
    if (state[key] !== undefined && !isFiniteNumber(state[key])) return false;
  }
  for (const key of ["scrollMaxX", "scrollMaxY"]) {
    if (state[key] !== undefined && !isNonNegativeNumber(state[key])) return false;
  }
  if (state.selectedIndex !== undefined &&
      (!Number.isInteger(state.selectedIndex) || (state.selectedIndex as number) < -1)) return false;
  return isOptionalProtocolValue(state.value);
}

function hasOnlyProperties(record: Record<string, unknown>, allowed: Set<string>): boolean {
  return Object.keys(record).every(key => allowed.has(key));
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === "string" && value.length > 0;
}

function isOptionalNonEmptyString(value: unknown): boolean {
  return value === undefined || isNonEmptyString(value);
}

function isOptionalString(value: unknown): boolean {
  return value === undefined || typeof value === "string";
}

function isFiniteNumber(value: unknown): value is number {
  return typeof value === "number" && Number.isFinite(value);
}

function isNonNegativeNumber(value: unknown): boolean {
  return isFiniteNumber(value) && value >= 0;
}

function isOptionalProtocolValue(value: unknown): boolean {
  return value === undefined || value === null || typeof value === "string" || typeof value === "boolean" || isFiniteNumber(value);
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
  return value !== null && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : undefined;
}

function isErrorCode(value: string): value is GuaWebError["code"] {
  return [
    "webmcp_unsupported", "engine_unsupported", "invalid_request", "node_not_found", "hidden", "disabled",
    "unsupported_action", "action_failed", "timeout", "aborted",
  ].includes(value);
}
