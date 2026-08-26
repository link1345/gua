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
  stateValue: { type: ["string", "number", "boolean", "null"], description: "Exact primitive state value." },
};
export const worldObservationTools = [
  { name: "get_world_object_tree", description: "Read the host-authorized World Object Tree.", inputSchema: { type: "object", additionalProperties: false, properties: {} } },
  { name: "find_world_objects", description: "Find observable world objects with semantic criteria.", inputSchema: { type: "object", additionalProperties: false, properties: selectorProperties } },
  { name: "wait_for_world_object", description: "Wait until an observable world object matches.", inputSchema: { type: "object", additionalProperties: false, properties: { ...selectorProperties, timeoutMs: { type: "integer", minimum: 1, maximum: 300000 } } } },
] as const;
export type WorldObservationToolName = (typeof worldObservationTools)[number]["name"];

export function selectorFromArguments(args: Record<string, unknown>): GuaWorldSelector {
  const result: Record<string, unknown> = {};
  for (const key of ["id", "kind", "label", "tag", "parentId"] as const) if (typeof args[key] === "string" && args[key].length > 0) result[key] = args[key];
  for (const key of ["directChild", "visibleToPlayer", "active"] as const) if (typeof args[key] === "boolean") result[key] = args[key];
  if (typeof args.stateKey === "string" && args.stateKey.length > 0 && Object.prototype.hasOwnProperty.call(args, "stateValue"))
    result.state = { key: args.stateKey, value: args.stateValue as WorldPrimitive };
  return result as GuaWorldSelector;
}

export interface WebMcpModelContext { registerTool(definition: { name: string; description: string; inputSchema: Record<string, unknown>; execute(args: Record<string, unknown>): Promise<unknown> }): void }
export function registerWorldWebMcpTools(modelContext: WebMcpModelContext | undefined, provider: GuaWorldProvider): boolean {
  if (modelContext === undefined) return false;
  for (const tool of worldObservationTools) modelContext.registerTool({ ...tool, execute: async (args) => {
    const selector = selectorFromArguments(args);
    if (tool.name === "get_world_object_tree") return provider.getWorldObjectTree("player");
    if (tool.name === "find_world_objects") return provider.findWorldObjects(selector, "player");
    const timeoutMs = typeof args.timeoutMs === "number" && Number.isInteger(args.timeoutMs) ? args.timeoutMs : 5000;
    return provider.waitForWorldObject(selector, timeoutMs, "player");
  }});
  return true;
}
