import { describe, expect, test } from "bun:test";
import { registerWorldWebMcpTools } from "../src/index";

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
});
