export type WorldPrimitive = string | number | boolean | null;
export interface GuaWorldObject {
  id: string; parentId?: string; kind: string; label: string; description?: string;
  space: "world2d" | "world3d"; position: { x: number; y: number; z?: number };
  visibleToPlayer: boolean; active: boolean; agentExposure: "auto" | "private";
  domainId?: string; relatedUiNodeId?: string; tags: string[]; state: Record<string, WorldPrimitive>;
}
export interface GuaWorldObjectTree { schemaVersion: 1; sessionEpoch: number; frameSequence: number; revision: number; scene: string; objects: GuaWorldObject[] }
export interface GuaWorldSelector { id?: string; kind?: string; label?: string; tag?: string; parentId?: string; directChild?: boolean; visibleToPlayer?: boolean; active?: boolean; state?: { key: string; value: WorldPrimitive } }
export interface GuaWorldQueryResult { valid: boolean; matches: GuaWorldObject[]; error?: string }
export interface GuaWorldProvider {
  getWorldObjectTree(profile?: "player"): Promise<GuaWorldObjectTree>;
  findWorldObjects(selector: GuaWorldSelector, profile?: "player"): Promise<GuaWorldQueryResult>;
  waitForWorldObject(selector: GuaWorldSelector, timeoutMs: number, profile?: "player"): Promise<GuaWorldObject>;
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
      return provider.getWorldObjectTree("player");
    }
    if (tool.name === "find_world_objects" && Object.prototype.hasOwnProperty.call(args, "timeoutMs"))
      throw new TypeError("Unknown find_world_objects argument: timeoutMs.");
    const selector = selectorFromArguments(args);
    if (tool.name === "find_world_objects") return provider.findWorldObjects(selector, "player");
    if (Object.prototype.hasOwnProperty.call(args, "timeoutMs") &&
        (typeof args.timeoutMs !== "number" || !Number.isInteger(args.timeoutMs) || args.timeoutMs < 1 || args.timeoutMs > 300000))
      throw new TypeError("timeoutMs must be an integer from 1 through 300000.");
    const timeoutMs = typeof args.timeoutMs === "number" ? args.timeoutMs : 5000;
    return provider.waitForWorldObject(selector, timeoutMs, "player");
  }});
  return true;
}
