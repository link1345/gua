import { describe, expect, test } from "bun:test";

import {
  createGuaInPageBridge,
  createGodotWebBridge,
  createUnityWebGlBridge,
  GuaWebError,
  registerGuaWebMcp,
  type GuaBrowserBridge,
  type GuaWorldObject,
  type GuaWorldObjectTree,
  type GuaUiTree,
  type GuaWebActionRequest,
} from "../src/index";

type Registered = {
  name: string;
  description?: string;
  inputSchema?: Record<string, unknown>;
  execute(input: Record<string, unknown>, options?: { signal?: AbortSignal }): Promise<unknown>;
};

function modelDocument() {
  const tools = new Map<string, Registered>();
  const signals = new Map<string, AbortSignal | undefined>();
  return {
    tools,
    signals,
    document: {
      modelContext: {
        registerTool(tool: Registered, options?: { signal?: AbortSignal }) {
          tools.set(tool.name, tool);
          signals.set(tool.name, options?.signal);
        },
      },
    } as unknown as Document & { modelContext: { registerTool(tool: Registered, options?: { signal?: AbortSignal }): void } },
  };
}

function tree(nodes: GuaUiTree["nodes"] = []): GuaUiTree {
  return { schemaVersion: 2, sessionEpoch: 1, frameSequence: 2, revision: 2, screen: "title", nodes };
}

function button(id = "start"): GuaUiTree["nodes"][number] {
  return { id, role: "button", label: "Start", visible: true, enabled: true, bounds: { x: 0, y: 0, w: 10, h: 10 }, actions: ["click", "focus"] };
}

function worldObject(id = "door"): GuaWorldObject {
  return { id, kind: "door", label: "Door", space: "world3d", position: { x: 1, y: 2, z: 3 },
    visibleToPlayer: true, active: true, agentExposure: "auto", tags: ["interactive"], state: { locked: true } };
}

function worldTree(objects: GuaWorldObject[] = []): GuaWorldObjectTree {
  return { schemaVersion: 1, sessionEpoch: 1, frameSequence: 2, revision: 2, scene: "level", objects };
}

describe("registerGuaWebMcp", () => {
  test("feature detects WebMCP without breaking the page", async () => {
    const bridge = bridgeWithTree(tree());
    const registration = await registerGuaWebMcp(bridge, { document: {} as Document });
    expect(registration.supported).toBe(false);
    expect(registration.error?.code).toBe("webmcp_unsupported");
  });

  test("cleans up partial registrations when the browser rejects access", async () => {
    let registrations = 0;
    let firstSignal: AbortSignal | undefined;
    const document = {
      modelContext: {
        registerTool(_tool: Registered, options?: { signal?: AbortSignal }) {
          registrations += 1;
          firstSignal ??= options?.signal;
          if (registrations === 2) throw new DOMException("Permission denied", "NotAllowedError");
        },
      },
    } as unknown as Document & { modelContext: { registerTool(tool: Registered, options?: { signal?: AbortSignal }): void } };

    const registration = await registerGuaWebMcp(bridgeWithTree(tree()), { document });
    expect(registration.supported).toBe(false);
    expect(registration.registeredTools).toEqual([]);
    expect(registration.error?.code).toBe("webmcp_unsupported");
    expect(firstSignal?.aborted).toBe(true);
  });

  test("registers the browser tool surface and waits for correlated host completion", async () => {
    const page = modelDocument();
    const requests: GuaWebActionRequest[] = [];
    const bridge: GuaBrowserBridge = {
      getUiTree: async () => tree([button()]),
      performAction: async (request) => {
        requests.push(request);
        return { requestId: 41, action: request.action, nodeId: request.nodeId, succeeded: true, revision: 3 };
      },
      getScreenshot: async () => ({ dataUri: "data:image/png;base64,AA==", width: 1, height: 1 }),
    };
    const registration = await registerGuaWebMcp(bridge, { document: page.document });
    expect(registration.supported).toBe(true);
    expect(registration.registeredTools).toContain("get_screenshot");
    const setValueSchema = page.tools.get("set_value")!.inputSchema as { properties: Record<string, unknown> };
    expect(setValueSchema.properties.secretKey).toBeUndefined();
    expect(setValueSchema.properties.value).not.toHaveProperty("minLength");
    for (const [toolName, propertyName] of [
      ["click_node", "nodeId"],
      ["focus_node", "nodeId"],
      ["set_value", "nodeId"],
      ["set_checked", "nodeId"],
      ["select", "nodeId"],
      ["select", "value"],
      ["scroll", "nodeId"],
      ["press_key", "key"],
      ["press_key", "nodeId"],
      ["wait_for_node", "nodeId"],
    ] as const) {
      const schema = page.tools.get(toolName)!.inputSchema as { properties: Record<string, { minLength?: number }> };
      expect(schema.properties[propertyName]?.minLength).toBe(1);
    }
    expect(page.tools.get("get_screenshot")!.description).toContain("latest screenshot published");
    const waitSchema = page.tools.get("wait_for_node")!.inputSchema as { properties: { timeoutMs: { maximum?: number } } };
    expect(waitSchema.properties.timeoutMs.maximum).toBe(2_147_483_647);

    const result = await page.tools.get("click_node")!.execute({ nodeId: "start" }) as { content: Array<{ text: string }> };
    expect(requests).toEqual([{ action: "click", nodeId: "start" }]);
    expect(JSON.parse(result.content[0]!.text).requestId).toBe(41);

    registration.unregister();
    expect(page.signals.get("click_node")?.aborted).toBe(true);
  });

  test("validates live semantic state before dispatch", async () => {
    const page = modelDocument();
    let called = false;
    const bridge: GuaBrowserBridge = {
      getUiTree: async () => tree([{ ...button(), visible: false }]),
      performAction: async () => { called = true; throw new Error("should not run"); },
    };
    await registerGuaWebMcp(bridge, { document: page.document });
    const result = await page.tools.get("click_node")!.execute({ nodeId: "start" }) as { isError: boolean; content: Array<{ text: string }> };
    expect(called).toBe(false);
    expect(result.isError).toBe(true);
    expect(JSON.parse(result.content[0]!.text).error.code).toBe("hidden");
  });

  test("rejects invalid completion request IDs from custom bridges", async () => {
    for (const requestId of [0, -1, 1.5, Number.NaN, Number.POSITIVE_INFINITY]) {
      const page = modelDocument();
      const bridge: GuaBrowserBridge = {
        getUiTree: async () => tree([button()]),
        performAction: async () => ({ requestId, action: "click", succeeded: true }),
      };
      await registerGuaWebMcp(bridge, { document: page.document });
      const result = await page.tools.get("click_node")!.execute({ nodeId: "start" }) as {
        isError: boolean;
        content: Array<{ text: string }>;
      };
      expect(result.isError).toBe(true);
      expect(JSON.parse(result.content[0]!.text).error.code).toBe("invalid_request");
    }
  });

  test("registers read-only world tools and delegates selectors to the engine bridge", async () => {
    const page = modelDocument();
    const selectors: unknown[] = [];
    let queryCount = 0;
    const bridge: GuaBrowserBridge = {
      getUiTree: async () => tree(),
      performAction: async (request) => ({ requestId: 1, action: request.action, succeeded: true }),
      getWorldObjectTree: async () => worldTree([worldObject()]),
      findWorldObjects: async (selector) => {
        selectors.push(selector);
        queryCount += 1;
        return { valid: true, matches: queryCount >= 2 ? [worldObject()] : [] };
      },
    };
    const registration = await registerGuaWebMcp(bridge, { document: page.document, pollIntervalMs: 0 });
    expect(registration.registeredTools).toEqual(expect.arrayContaining([
      "get_world_object_tree", "find_world_objects", "wait_for_world_object",
    ]));
    expect(registration.registeredTools).not.toContain("interact_world_object");

    const treeResult = await page.tools.get("get_world_object_tree")!.execute({}) as { content: Array<{ text: string }> };
    expect(JSON.parse(treeResult.content[0]!.text).objects[0].id).toBe("door");
    await page.tools.get("find_world_objects")!.execute({ parentId: "room", directChild: true, stateKey: "locked", stateValue: true });
    const waitResult = await page.tools.get("wait_for_world_object")!.execute({ id: "door", timeoutMs: 100 }) as { content: Array<{ text: string }> };
    expect(JSON.parse(waitResult.content[0]!.text).id).toBe("door");
    expect(selectors).toEqual([
      { parentId: "room", directChild: true, state: { key: "locked", value: true } },
      { id: "door" },
    ]);
  });

  test("rejects malformed world selectors before invoking the engine", async () => {
    const page = modelDocument();
    let queries = 0;
    const bridge: GuaBrowserBridge = {
      getUiTree: async () => tree(),
      performAction: async (request) => ({ requestId: 1, action: request.action, succeeded: true }),
      findWorldObjects: async () => { queries += 1; return { valid: true, matches: [] }; },
    };
    await registerGuaWebMcp(bridge, { document: page.document });
    for (const input of [{ directChild: true }, { stateKey: "locked" }, { stateValue: true }, { timeoutMs: 1 }]) {
      const result = await page.tools.get("find_world_objects")!.execute(input) as { content: Array<{ text: string }> };
      expect(JSON.parse(result.content[0]!.text).error.code).toBe("invalid_request");
    }
    expect(queries).toBe(0);
  });

  test.each([
    [-2, "node_not_found"],
    [-3, "hidden"],
    [-4, "disabled"],
    [-5, "unsupported_action"],
    ["unsupported_action", "unsupported_action"],
    [-6, "invalid_request"],
    ["invalid_value", "invalid_request"],
  ] as const)("preserves host completion error %s as %s", async (hostError, expectedCode) => {
    const page = modelDocument();
    const bridge: GuaBrowserBridge = {
      getUiTree: async () => tree([button()]),
      performAction: async () => ({ requestId: 42, action: "click", nodeId: "start", succeeded: false, error: hostError }),
    };
    await registerGuaWebMcp(bridge, { document: page.document });

    const result = await page.tools.get("click_node")!.execute({ nodeId: "start" }) as { content: Array<{ text: string }> };

    expect(JSON.parse(result.content[0]!.text).error).toMatchObject({ code: expectedCode, details: { hostError } });
  });

  test("waits on fresh snapshots and redacts sensitive completion values", async () => {
    const page = modelDocument();
    let reads = 0;
    const bridge: GuaBrowserBridge = {
      getUiTree: async () => tree(++reads >= 2 ? [button("password")] : []),
      performAction: async (request) => ({
        requestId: 9, action: request.action, nodeId: request.nodeId, succeeded: true,
        value: request.value, sensitive: request.sensitive,
      }),
    };
    const registration = await registerGuaWebMcp(bridge, { document: page.document, pollIntervalMs: 0 });
    expect(registration.registeredTools).not.toContain("get_screenshot");
    const waited = await page.tools.get("wait_for_node")!.execute({ nodeId: "password", timeoutMs: 100 }) as { content: Array<{ text: string }> };
    expect(JSON.parse(waited.content[0]!.text).id).toBe("password");
    const result = await page.tools.get("set_value")!.execute({ nodeId: "password", value: "secret-marker", sensitive: true }) as { content: Array<{ text: string }> };
    expect(result.content[0]!.text).not.toContain("secret-marker");
  });

  test("allows set_value to clear a control with an empty string", async () => {
    const page = modelDocument();
    const requests: GuaWebActionRequest[] = [];
    const bridge: GuaBrowserBridge = {
      getUiTree: async () => tree([{ ...button("name"), role: "textbox", actions: ["set_value"] }]),
      performAction: async (request) => {
        requests.push(request);
        return { requestId: 10, action: request.action, succeeded: true };
      },
    };
    await registerGuaWebMcp(bridge, { document: page.document });
    const result = await page.tools.get("set_value")!.execute({ nodeId: "name", value: "" }) as { isError?: boolean };
    expect(result.isError).toBeUndefined();
    expect(requests).toEqual([{ action: "set_value", nodeId: "name", value: "", sensitive: false }]);
  });

  test("rejects malformed sensitive flags before forwarding plaintext", async () => {
    const page = modelDocument();
    let called = false;
    const bridge: GuaBrowserBridge = {
      getUiTree: async () => tree([{ ...button("password"), role: "textbox", actions: ["set_value"] }]),
      performAction: async () => {
        called = true;
        return { requestId: 11, action: "set_value", succeeded: true };
      },
    };
    await registerGuaWebMcp(bridge, { document: page.document });

    const result = await page.tools.get("set_value")!.execute({ nodeId: "password", value: "secret-marker", sensitive: "true" }) as { content: Array<{ text: string }> };

    expect(called).toBe(false);
    expect(JSON.parse(result.content[0]!.text).error.code).toBe("invalid_request");
    expect(result.content[0]!.text).not.toContain("secret-marker");
  });

  test("rejects browser timer delays above the signed 32-bit range", async () => {
    const page = modelDocument();
    await registerGuaWebMcp(bridgeWithTree(tree()), { document: page.document });

    const result = await page.tools.get("wait_for_node")!.execute({ nodeId: "missing", timeoutMs: 2_147_483_648 }) as { content: Array<{ text: string }> };
    expect(JSON.parse(result.content[0]!.text).error.code).toBe("invalid_request");

    const invalidRegistration = await registerGuaWebMcp(bridgeWithTree(tree()), {
      document: modelDocument().document,
      defaultTimeoutMs: 2_147_483_648,
    });
    expect(invalidRegistration.supported).toBe(false);
    expect(invalidRegistration.error?.code).toBe("invalid_request");
  });

  test("redacts sensitive values from structured bridge failures", async () => {
    const page = modelDocument();
    const bridge: GuaBrowserBridge = {
      getUiTree: async () => tree([{ ...button("password"), actions: ["set_value"] }]),
      performAction: async () => { throw new GuaWebError("action_failed", "Rejected secret-marker", { received: "secret-marker" }); },
    };
    await registerGuaWebMcp(bridge, { document: page.document });
    const result = await page.tools.get("set_value")!.execute({ nodeId: "password", value: "secret-marker", sensitive: true }) as { content: Array<{ text: string }> };
    expect(result.content[0]!.text).not.toContain("secret-marker");
    expect(result.content[0]!.text).toContain("[REDACTED]");
  });

  test("redacts JSON syntax characters without corrupting structured failures", async () => {
    const page = modelDocument();
    const bridge: GuaBrowserBridge = {
      getUiTree: async () => tree([{ ...button("password"), actions: ["set_value"] }]),
      performAction: async () => { throw new GuaWebError("action_failed", "Rejected value", { received: '"', nested: ['before"after'] }); },
    };
    await registerGuaWebMcp(bridge, { document: page.document });
    const result = await page.tools.get("set_value")!.execute({ nodeId: "password", value: '"', sensitive: true }) as { isError: boolean; content: Array<{ text: string }> };
    const payload = JSON.parse(result.content[0]!.text);
    expect(result.isError).toBe(true);
    expect(payload.error.details).toEqual({ received: "[REDACTED]", nested: ["before[REDACTED]after"] });
  });

  test("removes wait polling abort listeners after each completed delay", async () => {
    const page = modelDocument();
    let reads = 0;
    const bridge = bridgeWithTree({
      get sessionEpoch() { return 1; },
      get frameSequence() { return reads; },
      get revision() { return reads; },
      screen: "title",
      get nodes() { return ++reads >= 4 ? [button("later")] : []; },
    });
    await registerGuaWebMcp(bridge, { document: page.document, pollIntervalMs: 1 });
    const controller = new AbortController();
    const originalAdd = controller.signal.addEventListener.bind(controller.signal);
    const originalRemove = controller.signal.removeEventListener.bind(controller.signal);
    let activeListeners = 0;
    controller.signal.addEventListener = ((...args: Parameters<AbortSignal["addEventListener"]>) => {
      activeListeners += 1;
      return originalAdd(...args);
    }) as AbortSignal["addEventListener"];
    controller.signal.removeEventListener = ((...args: Parameters<AbortSignal["removeEventListener"]>) => {
      activeListeners -= 1;
      return originalRemove(...args);
    }) as AbortSignal["removeEventListener"];

    const result = await page.tools.get("wait_for_node")!.execute({ nodeId: "later", timeoutMs: 100 }, { signal: controller.signal }) as { isError?: boolean };
    expect(result.isError).toBeUndefined();
    expect(activeListeners).toBe(0);
  });

  test("returns a structured timeout when host completion never arrives", async () => {
    const page = modelDocument();
    let engineSignal: AbortSignal | undefined;
    const bridge: GuaBrowserBridge = {
      getUiTree: async () => tree([button()]),
      performAction: async (_request, options) => {
        engineSignal = options?.signal;
        return await new Promise((_resolve, reject) => options?.signal?.addEventListener(
          "abort", () => reject(new GuaWebError("aborted", "Engine action cancelled.")), { once: true },
        ));
      },
    };
    await registerGuaWebMcp(bridge, { document: page.document, defaultTimeoutMs: 0 });
    const result = await page.tools.get("click_node")!.execute({ nodeId: "start" }) as { content: Array<{ text: string }> };
    expect(JSON.parse(result.content[0]!.text).error.code).toBe("timeout");
    expect(engineSignal?.aborted).toBe(true);
  });

  test("propagates caller aborts to an enqueued engine action", async () => {
    const page = modelDocument();
    let engineSignal: AbortSignal | undefined;
    let markActionStarted!: () => void;
    const actionStarted = new Promise<void>((resolve) => { markActionStarted = resolve; });
    const bridge: GuaBrowserBridge = {
      getUiTree: async () => tree([button()]),
      performAction: async (_request, options) => {
        engineSignal = options?.signal;
        markActionStarted();
        return await new Promise((_resolve, reject) => options?.signal?.addEventListener(
          "abort", () => reject(new GuaWebError("aborted", "Engine action cancelled.")), { once: true },
        ));
      },
    };
    await registerGuaWebMcp(bridge, { document: page.document });
    const controller = new AbortController();
    const pending = page.tools.get("click_node")!.execute({ nodeId: "start" }, { signal: controller.signal });
    await actionStarted;
    controller.abort();
    const result = await pending as { content: Array<{ text: string }> };
    expect(JSON.parse(result.content[0]!.text).error.code).toBe("aborted");
    expect(engineSignal?.aborted).toBe(true);
  });

  test("propagates the configured timeout to the engine action call", async () => {
    const page = modelDocument();
    let engineTimeoutMs: number | undefined;
    const bridge: GuaBrowserBridge = {
      getUiTree: async () => tree([button()]),
      performAction: async (request, options) => {
        engineTimeoutMs = options?.timeoutMs;
        return { requestId: 12, action: request.action, succeeded: true };
      },
    };
    await registerGuaWebMcp(bridge, { document: page.document, defaultTimeoutMs: 7500 });

    await page.tools.get("click_node")!.execute({ nodeId: "start" });

    expect(engineTimeoutMs).toBe(7500);
  });

  test("times out stalled semantic tree reads", async () => {
    const page = modelDocument();
    const bridge: GuaBrowserBridge = {
      getUiTree: async () => new Promise(() => {}),
      performAction: async (request) => ({ requestId: 1, action: request.action, succeeded: true }),
    };
    await registerGuaWebMcp(bridge, { document: page.document, defaultTimeoutMs: 0 });

    for (const [name, input] of [
      ["get_ui_tree", {}],
      ["wait_for_node", { nodeId: "missing", timeoutMs: 0 }],
      ["click_node", { nodeId: "start" }],
    ] as const) {
      const result = await page.tools.get(name)!.execute(input) as { content: Array<{ text: string }> };
      expect(JSON.parse(result.content[0]!.text).error.code).toBe("timeout");
    }
  });

  test("aborts a stalled screenshot read", async () => {
    const page = modelDocument();
    const bridge: GuaBrowserBridge = {
      getUiTree: async () => tree(),
      performAction: async (request) => ({ requestId: 1, action: request.action, succeeded: true }),
      getScreenshot: async () => new Promise(() => {}),
    };
    await registerGuaWebMcp(bridge, { document: page.document });
    const controller = new AbortController();
    const pending = page.tools.get("get_screenshot")!.execute({}, { signal: controller.signal });
    controller.abort();
    const result = await pending as { content: Array<{ text: string }> };
    expect(JSON.parse(result.content[0]!.text).error.code).toBe("aborted");
  });

  test("keeps registrations and state scoped to each document", async () => {
    const first = modelDocument();
    const second = modelDocument();
    await registerGuaWebMcp(bridgeWithTree(tree([button("first")])), { document: first.document });
    await registerGuaWebMcp(bridgeWithTree(tree([button("second")])), { document: second.document });
    const firstTree = await first.tools.get("get_ui_tree")!.execute({}) as { content: Array<{ text: string }> };
    const secondTree = await second.tools.get("get_ui_tree")!.execute({}) as { content: Array<{ text: string }> };
    expect(firstTree.content[0]!.text).toContain("first");
    expect(firstTree.content[0]!.text).not.toContain("second");
    expect(secondTree.content[0]!.text).toContain("second");
  });
});

describe("Gua same-page engine port", () => {
  test("normalizes legacy trees without protocol metadata", async () => {
    const bridge = createGuaInPageBridge({ invoke: async () => ({ screen: "legacy", nodes: [] }) });
    await expect(bridge.getUiTree()).resolves.toEqual({
      schemaVersion: 2,
      frameSequence: 0,
      revision: 0,
      screen: "legacy",
      nodes: [],
    });
  });

  test("rejects malformed versioned trees", async () => {
    for (const invalid of [
      { frameSequence: 1, revision: 1, screen: "title", nodes: [] },
      { schemaVersion: 1, frameSequence: 1, revision: 1, screen: "title", nodes: [] },
      { schemaVersion: 2, revision: 1, screen: "title", nodes: [] },
      { schemaVersion: 2, frameSequence: 1, screen: "title", nodes: [] },
      { schemaVersion: 2, sessionEpoch: 0, frameSequence: 1, revision: 1, screen: "title", nodes: [] },
      { schemaVersion: 2, sessionEpoch: -1, frameSequence: 1, revision: 1, screen: "title", nodes: [] },
      { schemaVersion: 2, sessionEpoch: "1", frameSequence: 1, revision: 1, screen: "title", nodes: [] },
      { sessionEpoch: 1, screen: "title", nodes: [] },
    ]) {
      const bridge = createGuaInPageBridge({ invoke: async () => invalid });
      await expect(bridge.getUiTree()).rejects.toMatchObject({ code: "invalid_request" });
    }
  });

  test("rejects malformed nodes before exposing a protocol tree", async () => {
    const valid = button();
    for (const invalidNode of [
      { ...valid, actions: null },
      { id: "start", visible: true, enabled: true, actions: [] },
      { ...valid, bounds: { x: 0, y: 0, w: -1, h: 10 } },
      { ...valid, actions: ["click", "click"] },
      { ...valid, state: { selectedIndex: -2 } },
      { ...valid, state: [] },
      { ...valid, unexpected: true },
    ]) {
      const bridge = createGuaInPageBridge({ invoke: async () => ({ ...tree(), nodes: [invalidNode] }) });
      await expect(bridge.getUiTree()).rejects.toMatchObject({ code: "invalid_request" });
    }

    const legacyBridge = createGuaInPageBridge({ invoke: async () => ({ screen: "legacy", nodes: [{ ...valid, actions: null }] }) });
    await expect(legacyBridge.getUiTree()).rejects.toMatchObject({ code: "invalid_request" });
  });

  test("resolves replacement Godot and Unity global ports for every call", async () => {
    for (const [portName, createBridge] of [
      ["__guaTestGodotPort", createGodotWebBridge],
      ["__guaTestUnityPort", createUnityWebGlBridge],
    ] as const) {
      const globals = globalThis as Record<string, unknown>;
      globals[portName] = { invoke: async () => tree([{ ...button("first") }]) };
      const bridge = createBridge(portName);
      expect((await bridge.getUiTree()).nodes[0]?.id).toBe("first");
      globals[portName] = { invoke: async () => tree([{ ...button("replacement") }]) };
      expect((await bridge.getUiTree()).nodes[0]?.id).toBe("replacement");
      delete globals[portName];
      await expect(bridge.getUiTree()).rejects.toMatchObject({ code: "engine_unsupported" });
    }
  });

  test("maps protocol commands without a transport or session router", async () => {
    const commands: unknown[] = [];
    const signals: Array<AbortSignal | undefined> = [];
    const bridge = createGuaInPageBridge({
      async invoke(command, options) {
        commands.push(command);
        signals.push(options?.signal);
        if (command.type === "get_ui_tree") return JSON.stringify(tree([button()]));
        if (command.type === "perform_action") return {
          requestId: 7, action: command.request.action, nodeId: command.request.nodeId,
          succeeded: true, sessionEpoch: 1, frameSequence: 3, revision: 3,
        };
        throw new Error("unsupported");
      },
    });
    expect((await bridge.getUiTree()).nodes[0]?.id).toBe("start");
    const controller = new AbortController();
    expect((await bridge.performAction({ action: "click", nodeId: "start" }, { signal: controller.signal })).requestId).toBe(7);
    expect(commands).toEqual([
      { type: "get_ui_tree" },
      { type: "perform_action", request: { action: "click", nodeId: "start" } },
    ]);
    expect(signals).toEqual([undefined, controller.signal]);
  });

  test("rejects completions without a positive integer request ID", async () => {
    for (const requestId of [0, -1, 1.5, Number.NaN, Number.POSITIVE_INFINITY]) {
      const bridge = createGuaInPageBridge({
        invoke: async () => ({ requestId, succeeded: true }),
      });
      await expect(bridge.performAction({ action: "click", nodeId: "start" }))
        .rejects.toMatchObject({ code: "invalid_request" });
    }
  });

  test("maps world reads and queries to the engine-owned port", async () => {
    const commands: unknown[] = [];
    const bridge = createGuaInPageBridge({ invoke: async (command) => {
      commands.push(command);
      if (command.type === "get_world_object_tree") return worldTree([worldObject()]);
      if (command.type === "query_world_objects") return { valid: true, matches: [worldObject()] };
      throw new Error("unsupported");
    } }, { world: true });
    expect((await bridge.getWorldObjectTree!()).objects[0]?.id).toBe("door");
    expect((await bridge.findWorldObjects!({ visibleToPlayer: false, state: { key: "locked", value: true } })).matches).toHaveLength(1);
    expect(commands).toEqual([
      { type: "get_world_object_tree" },
      { type: "query_world_objects", visibleToPlayer: 1, stateKey: "locked", stateType: 3, stateBool: true },
    ]);
  });

  test("preserves protocol top-level node text and value", async () => {
    const bridge = createGuaInPageBridge({
      invoke: async () => JSON.stringify(tree([{
        ...button("name"), role: "textbox", text: "Player name", value: "Gua", actions: ["set_value"],
      }])),
    });
    const node = (await bridge.getUiTree()).nodes[0]!;
    expect(node.text).toBe("Player name");
    expect(node.value).toBe("Gua");
  });

  test("does not expose screenshot support until explicitly enabled", () => {
    const port = { invoke: async () => ({}) };
    expect(createGuaInPageBridge(port).getScreenshot).toBeUndefined();
    expect(createGuaInPageBridge(port, { screenshot: true }).getScreenshot).toBeFunction();
  });

  test("preserves recognized structured engine error codes", async () => {
    for (const code of ["node_not_found", "hidden", "disabled", "unsupported_action", "timeout"] as const) {
      const bridge = createGuaInPageBridge({
        invoke: async () => { throw { code, message: "Host rejected the action." }; },
      });
      await expect(bridge.performAction({ action: "click", nodeId: "start" })).rejects.toMatchObject({ code });
    }
  });
});

function bridgeWithTree(value: GuaUiTree): GuaBrowserBridge {
  return {
    getUiTree: async () => value,
    performAction: async (request) => ({ requestId: 1, action: request.action, succeeded: true }),
  };
}
