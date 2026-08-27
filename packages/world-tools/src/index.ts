export type WorldPrimitive = string | number | boolean | null;
export interface GuaWorldObject {
  id: string; parentId?: string; kind: string; label?: string; description?: string;
  space: "world2d" | "world3d"; position: { x?: number; y?: number; z?: number };
  visibleToPlayer: boolean; active: boolean; agentExposure: "auto" | "private";
  domainId?: string; relatedUiNodeId?: string; tags?: string[]; state: Record<string, WorldPrimitive>;
}
export interface GuaWorldObjectTree { schemaVersion: 1; sessionEpoch: number; frameSequence: number; revision: number; scene: string; objects: GuaWorldObject[] }
export interface GuaWorldSelector { id?: string; kind?: string; label?: string; tag?: string; parentId?: string; directChild?: boolean; visibleToPlayer?: boolean; active?: boolean; state?: { key: string; value: WorldPrimitive } }
export interface GuaWorldQueryResult { valid: boolean; matches: GuaWorldObject[]; error?: string }
export interface GuaWorldProvider {
  getWorldObjectTree(): Promise<GuaWorldObjectTree>;
  findWorldObjects(selector: GuaWorldSelector): Promise<GuaWorldQueryResult>;
  waitForWorldObject(selector: GuaWorldSelector, timeoutMs: number): Promise<GuaWorldObject>;
}

export function parseWorldObjectTree(value: unknown): GuaWorldObjectTree {
  const parsed = parseJson(value);
  const tree = record(parsed);
  if (!tree || !only(tree, treeKeys) || tree.schemaVersion !== 1 || !positiveInteger(tree.sessionEpoch) ||
      !nonNegativeInteger(tree.frameSequence) || !nonNegativeInteger(tree.revision) ||
      !nonEmptyString(tree.scene) || !Array.isArray(tree.objects) || !tree.objects.every(worldObject)) {
    throw new TypeError("The engine returned an invalid protocol World Object Tree.");
  }
  return parsed as GuaWorldObjectTree;
}

export function parseWorldQueryResult(value: unknown): GuaWorldQueryResult {
  const parsed = parseJson(value);
  const result = record(parsed);
  if (!result || !only(result, queryKeys) || typeof result.valid !== "boolean" ||
      !Array.isArray(result.matches) || !result.matches.every(worldObject) ||
      (result.error !== undefined && typeof result.error !== "string")) {
    throw new TypeError("The engine returned an invalid world query result.");
  }
  return parsed as GuaWorldQueryResult;
}

const treeKeys = new Set(["schemaVersion", "sessionEpoch", "frameSequence", "revision", "scene", "objects"]);
const queryKeys = new Set(["valid", "matches", "error"]);
const objectKeys = new Set([
  "id", "parentId", "kind", "label", "description", "space", "position", "visibleToPlayer", "active",
  "agentExposure", "domainId", "relatedUiNodeId", "tags", "state",
]);
const position2dKeys = new Set(["x", "y"]);
const position3dKeys = new Set(["x", "y", "z"]);
const kindPattern = /^[a-z][a-z0-9_.-]*$/;

function worldObject(value: unknown): value is GuaWorldObject {
  const object = record(value);
  const position = record(object?.position);
  const state = record(object?.state);
  if (!object || !only(object, objectKeys) || !nonEmptyString(object.id) || !optionalNonEmptyString(object.parentId) ||
      typeof object.kind !== "string" || !kindPattern.test(object.kind) ||
      (object.label !== undefined && typeof object.label !== "string") ||
      (object.description !== undefined && typeof object.description !== "string") ||
      (object.space !== "world2d" && object.space !== "world3d") || !position ||
      !only(position, object.space === "world2d" ? position2dKeys : position3dKeys) ||
      !optionalFiniteNumber(position.x) || !optionalFiniteNumber(position.y) ||
      (object.space === "world3d" && !optionalFiniteNumber(position.z)) ||
      typeof object.visibleToPlayer !== "boolean" || typeof object.active !== "boolean" ||
      (object.agentExposure !== "auto" && object.agentExposure !== "private") ||
      !optionalNonEmptyString(object.domainId) || !optionalNonEmptyString(object.relatedUiNodeId) ||
      (object.tags !== undefined && (!Array.isArray(object.tags) || !object.tags.every(nonEmptyString) || new Set(object.tags).size !== object.tags.length)) ||
      !state || Object.keys(state).some((key) => key.length === 0) || !Object.values(state).every(worldPrimitive)) return false;
  return true;
}

function parseJson(value: unknown): unknown {
  if (typeof value !== "string") return value;
  try { return JSON.parse(value); }
  catch { throw new TypeError("The engine returned malformed world JSON."); }
}
function record(value: unknown): Record<string, unknown> | undefined {
  return value !== null && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : undefined;
}
function only(value: Record<string, unknown>, keys: Set<string>): boolean { return Object.keys(value).every((key) => keys.has(key)); }
function nonEmptyString(value: unknown): value is string { return typeof value === "string" && value.length > 0; }
function optionalNonEmptyString(value: unknown): boolean { return value === undefined || nonEmptyString(value); }
function finiteNumber(value: unknown): value is number { return typeof value === "number" && Number.isFinite(value); }
function optionalFiniteNumber(value: unknown): boolean { return value === undefined || finiteNumber(value); }
function positiveInteger(value: unknown): boolean { return Number.isInteger(value) && (value as number) >= 1; }
function nonNegativeInteger(value: unknown): boolean { return Number.isInteger(value) && (value as number) >= 0; }
function worldPrimitive(value: unknown): value is WorldPrimitive {
  return value === null || typeof value === "string" || typeof value === "boolean" || finiteNumber(value);
}
const string = (description: string) => ({ type: "string", minLength: 1, description });
const selectorProperties = {
  id: string("Exact stable object id."), kind: string("Exact semantic object kind."), label: string("Exact object label."),
  tag: string("Required object tag."), parentId: string("Scope parent id."), directChild: { type: "boolean" },
  visibleToPlayer: { type: "boolean" }, active: { type: "boolean" },
  stateKey: string("Primitive state key."),
  stateValue: { anyOf: [
    { type: ["string", "boolean", "null"] },
    { type: "number", not: { type: "integer" } },
    { type: "integer", minimum: Number.MIN_SAFE_INTEGER, maximum: Number.MAX_SAFE_INTEGER },
  ], description: "Exact primitive state value; integers must be safely distinguishable." },
};
const selectorSchema = (properties: Record<string, unknown>) => ({
  type: "object", additionalProperties: false, properties,
  dependentRequired: { stateKey: ["stateValue"], stateValue: ["stateKey"] },
  allOf: [{ if: { required: ["directChild"], properties: { directChild: { const: true } } }, then: { required: ["parentId"] } }],
});
export const worldObservationTools = [
  { name: "get_world_object_tree", description: "Read the host-authorized World Object Tree.", inputSchema: { type: "object", additionalProperties: false, properties: {} } },
  { name: "find_world_objects", description: "Find observable world objects with semantic criteria.", inputSchema: selectorSchema(selectorProperties) },
  { name: "wait_for_world_object", description: "Wait until an observable world object matches.", inputSchema: selectorSchema({ ...selectorProperties, timeoutMs: { type: "integer", minimum: 1, maximum: 300000 } }) },
] as const;
export type WorldObservationToolName = (typeof worldObservationTools)[number]["name"];

export function selectorFromArguments(args: Record<string, unknown>): GuaWorldSelector {
  const knownKeys = new Set(["id", "kind", "label", "tag", "parentId", "directChild", "visibleToPlayer", "active", "stateKey", "stateValue", "timeoutMs"]);
  for (const key of Object.keys(args)) if (!knownKeys.has(key)) throw new TypeError(`Unknown world selector argument: ${key}.`);
  const result: Record<string, unknown> = {};
  for (const key of ["id", "kind", "label", "tag", "parentId"] as const) {
    if (!Object.prototype.hasOwnProperty.call(args, key)) continue;
    if (typeof args[key] !== "string" || args[key].length === 0) throw new TypeError(`${key} must be a non-empty string.`);
    result[key] = args[key];
  }
  for (const key of ["directChild", "visibleToPlayer", "active"] as const) {
    if (!Object.prototype.hasOwnProperty.call(args, key)) continue;
    if (typeof args[key] !== "boolean") throw new TypeError(`${key} must be a boolean.`);
    result[key] = args[key];
  }
  if (result.directChild === true && result.parentId === undefined) throw new TypeError("parentId is required when directChild is true.");
  const hasStateKey = Object.prototype.hasOwnProperty.call(args, "stateKey");
  const hasStateValue = Object.prototype.hasOwnProperty.call(args, "stateValue");
  if (hasStateKey !== hasStateValue) throw new TypeError("stateKey and stateValue must be supplied together.");
  if (hasStateKey) {
    if (typeof args.stateKey !== "string" || args.stateKey.length === 0) throw new TypeError("stateKey must be a non-empty string.");
    const value = args.stateValue;
    if (value !== null && typeof value !== "string" && typeof value !== "boolean" &&
        (typeof value !== "number" || !Number.isFinite(value)))
      throw new TypeError("stateValue must be a primitive JSON value.");
    if (typeof value === "number" && Number.isInteger(value) && !Number.isSafeInteger(value))
      throw new TypeError("Integer stateValue must be a safely distinguishable JSON number.");
    result.state = { key: args.stateKey, value: value as WorldPrimitive };
  }
  return result as GuaWorldSelector;
}

export interface WebMcpModelContext { registerTool(definition: { name: string; description: string; inputSchema: Record<string, unknown>; execute(args: Record<string, unknown>): Promise<unknown> }): void }
export function registerWorldWebMcpTools(modelContext: WebMcpModelContext | undefined, provider: GuaWorldProvider): boolean {
  if (modelContext === undefined) return false;
  for (const tool of worldObservationTools) modelContext.registerTool({ ...tool, execute: async (args) => {
    if (tool.name === "get_world_object_tree") {
      if (Object.keys(args).length !== 0) throw new TypeError(`Unknown get_world_object_tree argument: ${Object.keys(args)[0]}.`);
      return provider.getWorldObjectTree();
    }
    if (tool.name === "find_world_objects" && Object.prototype.hasOwnProperty.call(args, "timeoutMs"))
      throw new TypeError("Unknown find_world_objects argument: timeoutMs.");
    const selector = selectorFromArguments(args);
    if (tool.name === "find_world_objects") return provider.findWorldObjects(selector);
    if (Object.prototype.hasOwnProperty.call(args, "timeoutMs") &&
        (typeof args.timeoutMs !== "number" || !Number.isInteger(args.timeoutMs) || args.timeoutMs < 1 || args.timeoutMs > 300000))
      throw new TypeError("timeoutMs must be an integer from 1 through 300000.");
    const timeoutMs = typeof args.timeoutMs === "number" ? args.timeoutMs : 5000;
    return provider.waitForWorldObject(selector, timeoutMs);
  }});
  return true;
}
