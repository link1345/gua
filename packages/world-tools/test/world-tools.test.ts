import { describe, expect, test } from "bun:test";
import { parseWorldObjectTree, parseWorldQueryResult, registerWorldWebMcpTools, selectorFromArguments, worldObservationTools } from "../src/index";

describe("World WebMCP tools", () => {
  test("feature detects and pins calls to the injected provider", async () => {
    const tools = new Map<string, (args: Record<string, unknown>) => Promise<unknown>>();
    const calls: string[] = [];
    const selectors: unknown[] = [];
    const provider = { getWorldObjectTree: async () => { calls.push("tree"); return { schemaVersion: 1 as const, sessionEpoch: 1, frameSequence: 1, revision: 1, scene: "test", objects: [] }; }, findWorldObjects: async (selector: unknown) => { selectors.push(selector); calls.push("find"); return { valid: true, sessionEpoch: 1, frameSequence: 1, revision: 1, matches: [] }; }, waitForWorldObject: async () => { throw new Error("timeout"); } };
    expect(registerWorldWebMcpTools(undefined, provider)).toBe(false);
    expect(registerWorldWebMcpTools({ registerTool: (tool) => tools.set(tool.name, tool.execute) }, provider)).toBe(true);
    expect(await tools.get("get_world_object_tree")?.({})).toEqual(expect.objectContaining({ scene: "test" }));
    await tools.get("find_world_objects")?.({ stateKey: "locked", stateValue: true });
    expect(calls).toEqual(["tree", "find"]);
    expect(selectors).toEqual([{ state: { key: "locked", value: true } }]);
    expect(tools.has("interact_world_object")).toBe(false);
  });

  test("requires complete primitive state criteria", () => {
    expect(worldObservationTools.every((tool) => !("profile" in tool.inputSchema.properties))).toBe(true);
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

  test("requires complete spatial criteria and validates limit", () => {
    expect(() => selectorFromArguments({ relativeToObjectId: "player" })).toThrow("supplied together");
    expect(() => selectorFromArguments({ maxDistance: 5 })).toThrow("supplied together");
    expect(() => selectorFromArguments({ relativeToObjectId: "", maxDistance: 5 })).toThrow("non-empty string");
    expect(() => selectorFromArguments({ relativeToObjectId: "player", maxDistance: -1 })).toThrow("non-negative");
    expect(() => selectorFromArguments({ limit: 1 })).toThrow("required when limit");
    expect(() => selectorFromArguments({ relativeToObjectId: "player", maxDistance: 5, limit: 0 })).toThrow("1 through");
    expect(selectorFromArguments({ relativeToObjectId: "player", maxDistance: 5, limit: 2 }))
      .toEqual({ near: { relativeToObjectId: "player", maxDistance: 5 }, limit: 2 });
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
    const provider = { getWorldObjectTree: async () => ({ schemaVersion: 1 as const, sessionEpoch: 1, frameSequence: 1, revision: 1, scene: "test", objects: [] }), findWorldObjects: async () => ({ valid: true, sessionEpoch: 1, frameSequence: 1, revision: 1, matches: [] }), waitForWorldObject: async () => { throw new Error("timeout"); } };
    registerWorldWebMcpTools({ registerTool: (tool) => tools.set(tool.name, tool.execute) }, provider);
    await expect(tools.get("get_world_object_tree")?.({ id: "door" })).rejects.toThrow("Unknown get_world_object_tree argument");
    await expect(tools.get("find_world_objects")?.({ timeoutMs: 10 })).rejects.toThrow("Unknown find_world_objects argument");
    await expect(tools.get("wait_for_world_object")?.({ timeoutMs: 0 })).rejects.toThrow("timeoutMs must be an integer");
  });

  test("validates World Object Tree and query payloads before exposing them", () => {
    const object = { id: "door", kind: "door", label: "Door", space: "world3d", position: { x: 1, y: 2, z: 3 },
      visibleToPlayer: true, active: true, agentExposure: "auto", tags: ["interactive"], state: { locked: true } };
    const tree = { schemaVersion: 1, sessionEpoch: 1, frameSequence: 2, revision: 3, scene: "level", objects: [object] };
    expect(parseWorldObjectTree(JSON.stringify(tree))).toEqual(tree);
    const query = { valid: true, sessionEpoch: 1, frameSequence: 2, revision: 3, matches: [object],
      spatial: { relativeToObjectId: "player", truncated: false, distances: [{ objectId: "door", distance: 5 }] } };
    expect(parseWorldQueryResult(query)).toEqual(query);
    const near = { near: { relativeToObjectId: "player", maxDistance: 5 }, limit: 1 };
    expect(parseWorldQueryResult(query, near)).toEqual(query);
    expect(() => parseWorldQueryResult({ ...query, spatial: undefined }, near)).toThrow("invalid world query result");
    expect(() => parseWorldQueryResult(query, {})).toThrow("invalid world query result");
    expect(() => parseWorldQueryResult({ ...query, spatial: { ...query.spatial, relativeToObjectId: "other" } }, near)).toThrow("invalid world query result");
    const selfMatch = { ...object, id: "player" };
    expect(() => parseWorldQueryResult({ ...query, matches: [selfMatch], spatial: { ...query.spatial,
      distances: [{ objectId: "player", distance: 0 }] } }, near)).toThrow("invalid world query result");
    const incompleteMatch = { ...object, position: { x: 1 } };
    expect(() => parseWorldQueryResult({ ...query, matches: [incompleteMatch] }, near)).toThrow("invalid world query result");
    expect(() => parseWorldQueryResult({ ...query, spatial: { ...query.spatial, truncated: true } }, { near: near.near })).toThrow("invalid world query result");
    expect(() => parseWorldQueryResult({ ...query, spatial: { ...query.spatial, distances: [] } })).toThrow("invalid world query result");
    expect(() => parseWorldQueryResult({ ...query, spatial: { ...query.spatial, distances: [{ objectId: "other", distance: 5 }] } })).toThrow("invalid world query result");
    const secondObject = { ...object, id: "enemy" };
    const unordered = { ...query, matches: [object, secondObject], spatial: { ...query.spatial,
      distances: [{ objectId: "door", distance: 5 }, { objectId: "enemy", distance: 4 }] } };
    expect(() => parseWorldQueryResult(unordered)).toThrow("invalid world query result");
    const unorderedTie = { ...query, matches: [secondObject, object], spatial: { ...query.spatial,
      distances: [{ objectId: "enemy", distance: 5 }, { objectId: "door", distance: 5 }] } };
    expect(() => parseWorldQueryResult(unorderedTie)).toThrow("invalid world query result");
    const bmpObject = { ...object, id: "\uE000" };
    const astralObject = { ...object, id: "\u{10000}" };
    const unicodeTie = { ...query, matches: [bmpObject, astralObject], spatial: { ...query.spatial,
      distances: [{ objectId: bmpObject.id, distance: 5 }, { objectId: astralObject.id, distance: 5 }] } };
    expect(parseWorldQueryResult(unicodeTie)).toEqual(unicodeTie);
    expect(() => parseWorldQueryResult({ ...unicodeTie, matches: [...unicodeTie.matches].reverse(), spatial: {
      ...unicodeTie.spatial, distances: [...unicodeTie.spatial.distances].reverse(),
    } })).toThrow("invalid world query result");
    const projected = { ...object, label: undefined, tags: undefined, position: { x: 1 }, domainId: "", relatedUiNodeId: "" };
    expect(parseWorldObjectTree({ ...tree, objects: [projected] }).objects).toEqual([projected]);
    for (const invalid of [
      { ...tree, sessionEpoch: 0 },
      { ...tree, objects: [{ ...object, kind: "Door" }] },
      { ...tree, objects: [{ ...object, space: "world2d", position: { x: 1, y: 2, z: 3 } }] },
      { ...tree, objects: [{ ...object, tags: ["same", "same"] }] },
      { ...tree, objects: [{ ...object, state: { distance: Number.NaN } }] },
    ]) expect(() => parseWorldObjectTree(invalid)).toThrow("invalid protocol World Object Tree");
    expect(() => parseWorldQueryResult({ valid: true, matches: [{ ...object, position: null }] })).toThrow("invalid world query result");
    expect(() => parseWorldQueryResult({ valid: true, matches: [object] })).toThrow("invalid world query result");
    expect(parseWorldQueryResult({ valid: false, matches: [], error: "invalid selector" })).toEqual({ valid: false, matches: [], error: "invalid selector" });
  });
});
