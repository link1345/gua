import { describe, expect, test } from "bun:test";
import { registerWorldWebMcpTools, selectorFromArguments, worldObservationTools } from "../src/index";

describe("World WebMCP tools", () => {
  test("feature detects and pins calls to the injected provider", async () => {
    const tools = new Map<string, (args: Record<string, unknown>) => Promise<unknown>>();
    const profiles: Array<string | undefined> = [];
    const selectors: unknown[] = [];
    const provider = { getWorldObjectTree: async (profile?: "player") => { profiles.push(profile); return { schemaVersion: 1 as const, sessionEpoch: 1, frameSequence: 1, revision: 1, scene: "test", objects: [] }; }, findWorldObjects: async (selector: unknown, profile?: "player") => { selectors.push(selector); profiles.push(profile); return { valid: true, matches: [] }; }, waitForWorldObject: async () => { throw new Error("timeout"); } };
    expect(registerWorldWebMcpTools(undefined, provider)).toBe(false);
    expect(registerWorldWebMcpTools({ registerTool: (tool) => tools.set(tool.name, tool.execute) }, provider)).toBe(true);
    expect(await tools.get("get_world_object_tree")?.({})).toEqual(expect.objectContaining({ scene: "test" }));
    await tools.get("find_world_objects")?.({ stateKey: "locked", stateValue: true });
    expect(profiles).toEqual(["player", "player"]);
    expect(selectors).toEqual([{ state: { key: "locked", value: true } }]);
    expect(tools.has("interact_world_object")).toBe(false);
  });

  test("requires complete primitive state criteria", () => {
    const schemas = worldObservationTools.filter((tool) => tool.name !== "get_world_object_tree").map((tool) => tool.inputSchema);
    expect(schemas.every((schema) => "dependentRequired" in schema)).toBe(true);
    expect(schemas.every((schema) => "anyOf" in schema.properties.stateValue)).toBe(true);
    expect(() => selectorFromArguments({ stateKey: "locked" })).toThrow("supplied together");
    expect(() => selectorFromArguments({ stateValue: true })).toThrow("supplied together");
    expect(() => selectorFromArguments({ stateKey: "locked", stateValue: {} })).toThrow("primitive JSON value");
    expect(() => selectorFromArguments(JSON.parse('{"stateKey":"code","stateValue":9007199254740993}')))
      .toThrow("safely distinguishable");
    expect(selectorFromArguments({ stateKey: "distance", stateValue: 1.5 }))
      .toEqual({ state: { key: "distance", value: 1.5 } });
    expect(selectorFromArguments({ stateKey: "locked", stateValue: false })).toEqual({ state: { key: "locked", value: false } });
  });

  test("requires a parent for direct-child scope", () => {
    const schemas = worldObservationTools.filter((tool) => tool.name !== "get_world_object_tree").map((tool) => tool.inputSchema);
    expect(schemas.every((schema) => "allOf" in schema)).toBe(true);
    expect(() => selectorFromArguments({ directChild: true })).toThrow("parentId is required");
    expect(selectorFromArguments({ parentId: "room", directChild: true })).toEqual({ parentId: "room", directChild: true });
  });

  test("rejects supplied invalid string criteria instead of dropping them", () => {
    for (const key of ["id", "kind", "label", "tag", "parentId"] as const) {
      expect(() => selectorFromArguments({ [key]: "" })).toThrow(`${key} must be a non-empty string`);
      expect(() => selectorFromArguments({ [key]: 42 })).toThrow(`${key} must be a non-empty string`);
    }
    for (const key of ["directChild", "visibleToPlayer", "active"] as const)
      expect(() => selectorFromArguments({ [key]: "true" })).toThrow(`${key} must be a boolean`);
    expect(selectorFromArguments({ id: "door" })).toEqual({ id: "door" });
  });

  test("rejects unknown direct tool arguments", async () => {
    expect(() => selectorFromArguments({ knd: "enemy" })).toThrow("Unknown world selector argument: knd");
    const tools = new Map<string, (args: Record<string, unknown>) => Promise<unknown>>();
    const provider = { getWorldObjectTree: async () => ({ schemaVersion: 1 as const, sessionEpoch: 1, frameSequence: 1, revision: 1, scene: "test", objects: [] }), findWorldObjects: async () => ({ valid: true, matches: [] }), waitForWorldObject: async () => { throw new Error("timeout"); } };
    registerWorldWebMcpTools({ registerTool: (tool) => tools.set(tool.name, tool.execute) }, provider);
    await expect(tools.get("get_world_object_tree")?.({ id: "door" })).rejects.toThrow("Unknown get_world_object_tree argument");
    await expect(tools.get("find_world_objects")?.({ timeoutMs: 10 })).rejects.toThrow("Unknown find_world_objects argument");
    await expect(tools.get("wait_for_world_object")?.({ timeoutMs: 0 })).rejects.toThrow("timeoutMs must be an integer");
  });
});
