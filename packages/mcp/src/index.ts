import path from "node:path";

import {
  GuaAutomationManager,
  type GuaRecording,
  type RecordedAction,
  type RecordingStep,
  validateRecording,
} from "./automation.js";

type JsonRpcId = string | number | null;

interface JsonRpcRequest {
  jsonrpc?: "2.0";
  id?: JsonRpcId;
  method: string;
  params?: unknown;
}

interface JsonRpcResponse {
  jsonrpc: "2.0";
  id: JsonRpcId;
  result?: unknown;
  error?: JsonRpcError;
}

interface JsonRpcError {
  code: number;
  message: string;
  data?: unknown;
}

interface GuaBounds {
  x: number;
  y: number;
  w: number;
  h: number;
}

interface GuaNodeState {
  focused?: boolean;
  hovered?: boolean;
  pressed?: boolean;
  checked?: boolean;
  value?: number | string | boolean | null;
}

interface GuaNode {
  id: string;
  parentId?: string;
  role: string;
  label?: string;
  visible: boolean;
  enabled: boolean;
  bounds: GuaBounds;
  state?: GuaNodeState;
  actions: string[];
}

interface GuaUiTree {
  sessionEpoch?: number;
  frameSequence?: number;
  revision?: number;
  screen: string;
  nodes: GuaNode[];
}

interface GuaLogEntry {
  sequence: number;
  level: "trace" | "debug" | "info" | "warn" | "error";
  message: string;
}

interface GuaScreenshot {
  dataUri: string;
  width: number;
  height: number;
}

type GuaBridgeResponse =
  | { id: number; ok: true; result: BridgeResult }
  | { id: number; ok: false; error: string };

interface McpTool {
  name: GuaMcpTool;
  description: string;
  inputSchema: Record<string, unknown>;
}

type ToolResult = {
  content: Array<{ type: "text"; text: string }>;
  isError?: boolean;
};

interface GuaActionReceipt { requestId: number }
interface GuaActionEvent {
  requestId: number;
  action: number;
  succeeded: boolean;
  error: number;
  nodeId: string;
  value: string;
  sensitive: boolean;
  sessionEpoch: number;
  frameSequence: number;
  revision: number;
}

type BridgeResult = unknown;

export interface GuaMcpServerOptions {
  bridgeUrl?: string;
  artifactDirectory?: string;
}

const defaultBridgeUrl = "ws://127.0.0.1:8765";

export const guaMcpTools = [
  "get_ui_tree",
  "click_node",
  "focus_node",
  "set_value",
  "set_checked",
  "select",
  "scroll",
  "press_key",
  "wait_for_node",
  "get_screenshot",
  "get_logs",
  "start_recording",
  "stop_recording",
  "save_recording",
  "replay_recording",
  "compare_screenshot",
  "get_visual_artifacts",
  "run_test",
] as const;

export type GuaMcpTool = (typeof guaMcpTools)[number];

const tools: McpTool[] = [
  {
    name: "get_ui_tree",
    description: "Read the current Gua semantic UI tree from the running game bridge.",
    inputSchema: objectSchema({}),
  },
  {
    name: "click_node",
    description: "Click a visible semantic UI node and wait for request-correlated host completion when supported.",
    inputSchema: objectSchema({
      nodeId: stringProperty("The target Gua node id."),
    }, ["nodeId"]),
  },
  {
    name: "focus_node",
    description: "Focus a semantic UI node and record the action when a recording is active.",
    inputSchema: objectSchema({ nodeId: stringProperty("The target Gua node id.") }, ["nodeId"]),
  },
  {
    name: "set_value",
    description: "Set a semantic UI node value. Sensitive values require a secretKey and are never written to recordings.",
    inputSchema: objectSchema({
      nodeId: stringProperty("The target Gua node id."),
      value: { type: "string", description: "The value to send to the host." },
      sensitive: { type: "boolean", description: "Redact the value from events and recordings." },
      secretKey: stringProperty("Stable key used to resolve a sensitive value during replay."),
    }, ["nodeId", "value"]),
  },
  {
    name: "set_checked",
    description: "Set the checked state of a semantic UI node.",
    inputSchema: objectSchema({
      nodeId: stringProperty("The target Gua node id."),
      checked: { type: "boolean" },
    }, ["nodeId", "checked"]),
  },
  {
    name: "select",
    description: "Select a value on a semantic UI node.",
    inputSchema: objectSchema({
      nodeId: stringProperty("The target Gua node id."),
      value: stringProperty("The option value to select."),
    }, ["nodeId", "value"]),
  },
  {
    name: "scroll",
    description: "Scroll a semantic UI node using host pixels or semantic lines.",
    inputSchema: objectSchema({
      nodeId: stringProperty("The target Gua node id."),
      deltaX: numberProperty("Horizontal scroll delta."),
      deltaY: numberProperty("Vertical scroll delta."),
      scrollUnit: { type: "integer", enum: [0, 1], description: "0 = pixels, 1 = semantic lines." },
    }, ["nodeId", "deltaX", "deltaY"]),
  },
  {
    name: "press_key",
    description: "Send a key press to a node or the host's current focus.",
    inputSchema: objectSchema({
      key: stringProperty("The logical key name to press, such as Enter or Escape."),
      nodeId: stringProperty("Optional target node id; omit to use current focus."),
      modifiers: { type: "integer", minimum: 0, maximum: 15, description: "Shift=1, Alt=2, Control=4, Meta=8." },
    }, ["key"]),
  },
  {
    name: "wait_for_node",
    description: "Poll the UI tree until a node id appears or the timeout expires.",
    inputSchema: objectSchema({
      nodeId: stringProperty("The target Gua node id."),
      timeoutMs: {
        type: "integer",
        minimum: 0,
        description: "Maximum wait time in milliseconds. Defaults to 5000.",
      },
    }, ["nodeId"]),
  },
  {
    name: "get_screenshot",
    description: "Read the latest Gua screenshot payload from the running game bridge.",
    inputSchema: objectSchema({}),
  },
  {
    name: "get_logs",
    description: "Read ordered runtime logs from the running game bridge.",
    inputSchema: objectSchema({}),
  },
  {
    name: "start_recording",
    description: "Start recording semantic actions invoked through this MCP server.",
    inputSchema: objectSchema({}),
  },
  {
    name: "stop_recording",
    description: "Stop the active recording and return a recording.schema.json-compatible document.",
    inputSchema: objectSchema({}),
  },
  {
    name: "save_recording",
    description: "Save the most recently completed recording under the configured Gua artifact directory.",
    inputSchema: objectSchema({ name: stringProperty("Safe recording name without a directory path.") }, ["name"]),
  },
  {
    name: "replay_recording",
    description: "Replay a saved or inline recording and wait for each semantic action's correlated completion.",
    inputSchema: objectSchema({
      name: stringProperty("Saved recording name. Omit when providing recording or replaying the last recording."),
      recording: { type: "object", description: "Inline recording.schema.json-compatible document." },
      secrets: { type: "object", additionalProperties: { type: "string" }, description: "secretKey to plaintext value map." },
      timingMode: { type: "string", enum: ["prefer_conditions", "preserve_delays"] },
      timeoutMs: { type: "integer", minimum: 1, maximum: 300000 },
    }),
  },
  {
    name: "compare_screenshot",
    description: "Compare the latest PNG screenshot with an explicit name/variant baseline and write machine-readable artifacts.",
    inputSchema: objectSchema({
      name: stringProperty("Stable test name."),
      variant: stringProperty("Explicit renderer/OS variant. Defaults to default."),
      updateBaseline: { type: "boolean", description: "Create or replace an approved baseline explicitly." },
      pixelThreshold: { type: "number", minimum: 0, maximum: 255 },
      maxDifferentPixelRatio: { type: "number", minimum: 0, maximum: 1 },
      masks: {
        type: "array",
        items: objectSchema({
          x: { type: "integer", minimum: 0 }, y: { type: "integer", minimum: 0 },
          width: { type: "integer", minimum: 1 }, height: { type: "integer", minimum: 1 },
        }, ["x", "y", "width", "height"]),
      },
    }, ["name"]),
  },
  {
    name: "get_visual_artifacts",
    description: "List visual comparison artifacts and return the latest comparison manifest.",
    inputSchema: objectSchema({ name: stringProperty("Optional stable test name filter.") }),
  },
  {
    name: "run_test",
    description: "Run a small protocol-level UI test made of wait and click steps.",
    inputSchema: objectSchema({
      steps: {
        type: "array",
        minItems: 1,
        items: {
          type: "object",
          additionalProperties: false,
          required: ["action", "nodeId"],
          properties: {
            action: {
              type: "string",
              enum: ["wait_for_node", "click_node"],
            },
            nodeId: stringProperty("The target Gua node id."),
            timeoutMs: {
              type: "integer",
              minimum: 0,
            },
          },
        },
      },
    }, ["steps"]),
  },
];

export async function runGuaMcpServer(options: GuaMcpServerOptions = {}): Promise<void> {
  const bridgeUrl = options.bridgeUrl ?? Bun.env.GUA_BRIDGE_URL ?? defaultBridgeUrl;
  const bridge = new GuaBridgeClient(bridgeUrl);
  const artifactDirectory = path.resolve(options.artifactDirectory ?? Bun.env.GUA_ARTIFACT_DIR ?? ".gua");
  const automation = new GuaAutomationManager(artifactDirectory);

  writeLog("info", `Gua MCP server connecting to ${bridgeUrl}; artifacts: ${artifactDirectory}`);

  let pending = "";
  const decoder = new TextDecoder();
  try {
    for await (const chunk of Bun.stdin.stream()) {
      pending += decoder.decode(chunk, { stream: true });
      pending = await drainPendingLines(pending, bridge, automation);
    }

    pending += decoder.decode();
    const finalLine = pending.trim();
    if (finalLine.length > 0) {
      await handleLine(finalLine, bridge, automation);
    }
  } finally {
    bridge.close();
  }
}

async function drainPendingLines(input: string, bridge: GuaBridgeClient, automation: GuaAutomationManager): Promise<string> {
  let pending = input;
  while (true) {
    const newlineIndex = pending.indexOf("\n");
    if (newlineIndex < 0) {
      return pending;
    }

    const line = pending.slice(0, newlineIndex).trim();
    pending = pending.slice(newlineIndex + 1);

    if (line.length === 0) {
      continue;
    }

    await handleLine(line, bridge, automation);
  }
}

async function handleLine(line: string, bridge: GuaBridgeClient, automation: GuaAutomationManager): Promise<void> {
  let request: unknown;
  try {
    request = JSON.parse(line) as JsonRpcRequest;
  } catch (error) {
    writeResponse({
      jsonrpc: "2.0",
      id: null,
      error: jsonRpcError(-32700, `Invalid JSON: ${(error as Error).message}`),
    });
    return;
  }

  if (!isJsonRpcRequest(request)) {
    writeResponse({
      jsonrpc: "2.0",
      id: isRecord(request) && isJsonRpcId(request.id) ? request.id : null,
      error: jsonRpcError(-32600, "Invalid JSON-RPC request."),
    });
    return;
  }

  if (request.id === undefined) {
    await handleNotification(request);
    return;
  }

  try {
    writeResponse({
      jsonrpc: "2.0",
      id: request.id,
      result: await handleRequest(request, bridge, automation),
    });
  } catch (error) {
    writeResponse({
      jsonrpc: "2.0",
      id: request.id,
      error: toJsonRpcError(error),
    });
  }
}

async function handleNotification(request: JsonRpcRequest): Promise<void> {
  if (request.method === "notifications/cancelled") {
    return;
  }

  if (request.method === "notifications/initialized") {
    return;
  }
}

async function handleRequest(request: JsonRpcRequest, bridge: GuaBridgeClient, automation: GuaAutomationManager): Promise<unknown> {
  switch (request.method) {
    case "initialize":
      return {
        protocolVersion: "2024-11-05",
        capabilities: {
          tools: {},
        },
        serverInfo: {
          name: "gui-mcp",
          version: "0.0.0",
        },
      };
    case "ping":
      return {};
    case "tools/list":
      return { tools };
    case "tools/call":
      return callTool(request.params, bridge, automation);
    default:
      throw new RpcFailure(-32601, `Unsupported MCP method: ${request.method}`);
  }
}

async function callTool(params: unknown, bridge: GuaBridgeClient, automation: GuaAutomationManager): Promise<ToolResult> {
  if (!isRecord(params) || typeof params.name !== "string") {
    throw new RpcFailure(-32602, "tools/call requires a tool name.");
  }

  const name = params.name;
  if (!isGuaMcpTool(name)) {
    throw new RpcFailure(-32602, `Unknown Gua MCP tool: ${name}`);
  }

  try {
    const result = await executeTool(name, isRecord(params.arguments) ? params.arguments : {}, bridge, automation);
    return textResult(result);
  } catch (error) {
    return textResult({ error: (error as Error).message }, true);
  }
}

async function executeTool(
  name: GuaMcpTool,
  args: Record<string, unknown>,
  bridge: GuaBridgeClient,
  automation: GuaAutomationManager,
): Promise<unknown> {
  switch (name) {
    case "get_ui_tree":
      return bridge.getUiTree();
    case "click_node":
      return performAndRecord(bridge, automation, { action: "click", nodeId: readStringArg(args, "nodeId") });
    case "focus_node":
      return performAndRecord(bridge, automation, { action: "focus", nodeId: readStringArg(args, "nodeId") });
    case "set_value": {
      const sensitive = readBooleanArg(args, "sensitive", false);
      const secretKey = readOptionalStringArg(args, "secretKey");
      if (sensitive && secretKey === undefined) throw new Error("Sensitive set_value requires secretKey.");
      return performAndRecord(bridge, automation, {
        action: "set_value", nodeId: readStringArg(args, "nodeId"), value: readStringArg(args, "value", true),
        sensitive, secretKey,
      });
    }
    case "set_checked":
      return performAndRecord(bridge, automation, {
        action: "set_checked", nodeId: readStringArg(args, "nodeId"), checked: readRequiredBooleanArg(args, "checked"),
      });
    case "select":
      return performAndRecord(bridge, automation, {
        action: "select", nodeId: readStringArg(args, "nodeId"), value: readStringArg(args, "value"),
      });
    case "scroll":
      return performAndRecord(bridge, automation, {
        action: "scroll", nodeId: readStringArg(args, "nodeId"),
        deltaX: readNumberArg(args, "deltaX", 0), deltaY: readNumberArg(args, "deltaY", 0),
        scrollUnit: readIntegerArg(args, "scrollUnit", 0),
      });
    case "press_key":
      return performAndRecord(bridge, automation, {
        action: "press_key", nodeId: readOptionalStringArg(args, "nodeId"), key: readStringArg(args, "key"),
        modifiers: readIntegerArg(args, "modifiers", 0),
      });
    case "wait_for_node":
      return bridge.waitForNode(
        readStringArg(args, "nodeId"),
        readIntegerArg(args, "timeoutMs", 5000),
      );
    case "get_screenshot":
      return bridge.getScreenshot();
    case "get_logs":
      return bridge.getLogs();
    case "start_recording":
      return { ...automation.startRecording(), artifactDirectory: automation.artifactRoot };
    case "stop_recording": {
      const recording = automation.stopRecording();
      return { recording, stepCount: recording.steps.length };
    }
    case "save_recording":
      return automation.saveRecording(readStringArg(args, "name"));
    case "replay_recording": {
      const inline = args.recording;
      let recording: GuaRecording;
      if (inline !== undefined) {
        validateRecording(inline);
        recording = inline;
        automation.setLastRecording(recording);
      } else if (readOptionalStringArg(args, "name") !== undefined) {
        recording = await automation.loadRecording(readStringArg(args, "name"));
      } else {
        recording = automation.getLastRecording();
      }
      const secrets = isRecord(args.secrets) ? args.secrets : {};
      return replayRecording(
        recording, bridge, secrets,
        args.timingMode === "preserve_delays" ? "preserve_delays" : "prefer_conditions",
        readIntegerArg(args, "timeoutMs", 10000),
      );
    }
    case "compare_screenshot":
      return automation.compareScreenshot(await bridge.getScreenshot(), {
        name: readStringArg(args, "name"),
        variant: readOptionalStringArg(args, "variant"),
        updateBaseline: readBooleanArg(args, "updateBaseline", false),
        pixelThreshold: readNumberArg(args, "pixelThreshold", 0),
        maxDifferentPixelRatio: readNumberArg(args, "maxDifferentPixelRatio", 0),
        masks: readMasks(args.masks),
      });
    case "get_visual_artifacts":
      return automation.getVisualArtifacts(readOptionalStringArg(args, "name"));
    case "run_test":
      return runTest(readTestSteps(args), bridge, automation);
  }
}

async function runTest(
  steps: TestStep[],
  bridge: GuaBridgeClient,
  automation: GuaAutomationManager,
): Promise<{ ok: true; steps: TestStepResult[] }> {
  const results: TestStepResult[] = [];

  for (const [index, step] of steps.entries()) {
    if (step.action === "wait_for_node") {
      await bridge.waitForNode(step.nodeId, step.timeoutMs ?? 5000);
    } else {
      await performAndRecord(bridge, automation, { action: "click", nodeId: step.nodeId });
    }

    results.push({
      index,
      action: step.action,
      nodeId: step.nodeId,
      ok: true,
    });
  }

  return { ok: true, steps: results };
}

interface SemanticActionInput {
  action: RecordedAction;
  nodeId?: string;
  value?: string;
  checked?: boolean;
  key?: string;
  modifiers?: number;
  deltaX?: number;
  deltaY?: number;
  scrollUnit?: number;
  sensitive?: boolean;
  secretKey?: string;
  waitCondition?: string;
}

async function performAndRecord(
  bridge: GuaBridgeClient,
  automation: GuaAutomationManager | undefined,
  input: SemanticActionInput,
  timeoutMs = 10000,
): Promise<{ ok: true; requestId?: number; completion?: GuaActionEvent }> {
  const before = await bridge.getUiTree();
  const receipt = await bridge.performAction(input);
  const completion = receipt === null ? undefined : await bridge.waitForAction(receipt.requestId, timeoutMs);
  if (completion?.succeeded === false) {
    throw new Error(`Gua action ${input.action} failed with error ${completion.error}.`);
  }
  const after = completion === undefined ? await bridge.getUiTree() : undefined;
  automation?.recordAction({
    action: input.action,
    requestId: receipt?.requestId,
    nodeId: input.nodeId,
    preRevision: before.revision ?? 0,
    postRevision: completion?.revision ?? after?.revision ?? before.revision ?? 0,
    waitCondition: input.waitCondition,
    value: actionValue(input),
    sensitive: input.sensitive,
    secretKey: input.secretKey,
    deltaX: input.deltaX,
    deltaY: input.deltaY,
    scrollUnit: input.scrollUnit,
    modifiers: input.modifiers,
  });
  return compactResult({ ok: true as const, requestId: receipt?.requestId, completion });
}

async function replayRecording(
  recording: GuaRecording,
  bridge: GuaBridgeClient,
  secrets: Record<string, unknown>,
  timingMode: "prefer_conditions" | "preserve_delays",
  timeoutMs: number,
): Promise<{ ok: true; steps: Array<{ index: number; action: RecordedAction; requestId?: number }> }> {
  validateRecording(recording);
  const results: Array<{ index: number; action: RecordedAction; requestId?: number }> = [];
  let previous = 0;
  for (const [index, step] of recording.steps.entries()) {
    if (timingMode === "prefer_conditions" && step.waitCondition !== undefined) {
      await waitForCondition(bridge, step.waitCondition, timeoutMs);
    } else if (step.relativeMilliseconds > previous) {
      await sleep(step.relativeMilliseconds - previous);
    }
    previous = step.relativeMilliseconds;
    const nodeId = await resolveTarget(bridge, step);
    const value = step.sensitive ? readSecret(secrets, step.secretKey as string) : step.value;
    const result = await performAndRecord(bridge, undefined, {
      action: step.action,
      nodeId,
      value,
      checked: step.action === "set_checked" ? value === "true" : undefined,
      key: step.action === "press_key" ? value : undefined,
      modifiers: step.modifiers,
      deltaX: step.deltaX,
      deltaY: step.deltaY,
      scrollUnit: step.scrollUnit,
      sensitive: step.sensitive,
      secretKey: step.secretKey,
    }, timeoutMs);
    results.push(compactResult({ index, action: step.action, requestId: result.requestId }));
  }
  return { ok: true, steps: results };
}

async function resolveTarget(bridge: GuaBridgeClient, step: RecordingStep): Promise<string | undefined> {
  if (step.coordinateFallback !== undefined) {
    throw new Error("Coordinate fallback replay is disabled; use a semantic target.");
  }
  const target = step.target as NonNullable<RecordingStep["target"]>;
  if (target.currentFocus === true) return undefined;
  if (target.id !== undefined) return target.id;
  const tree = await bridge.getUiTree();
  const candidates = tree.nodes.filter((node) => node.role === target.role &&
    (target.name === undefined || node.label === target.name) &&
    (target.scope === undefined || isDescendantOf(node, target.scope, tree.nodes)));
  if (candidates.length !== 1) {
    throw new Error(`Recording target ${target.role}/${target.name ?? ""} matched ${candidates.length} nodes.`);
  }
  return candidates[0]?.id;
}

function isDescendantOf(node: GuaNode, scopeId: string, nodes: GuaNode[]): boolean {
  let parentId = node.parentId;
  while (parentId !== undefined) {
    if (parentId === scopeId) return true;
    parentId = nodes.find((candidate) => candidate.id === parentId)?.parentId;
  }
  return false;
}

async function waitForCondition(bridge: GuaBridgeClient, condition: string, timeoutMs: number): Promise<void> {
  const parts = condition.split(":");
  if (parts.length < 2) throw new Error(`Unsupported recording wait condition: ${condition}`);
  const id = decodeURIComponent(parts[1] as string);
  const expected = parts[2] === undefined ? undefined : decodeURIComponent(parts[2]);
  const startedAt = Date.now();
  while (Date.now() - startedAt <= timeoutMs) {
    const node = (await bridge.getUiTree()).nodes.find((candidate) => candidate.id === id);
    const matched = parts[0] === "visible" ? node?.visible === true
      : parts[0] === "hidden" ? node === undefined || node.visible === false
      : parts[0] === "enabled" ? node?.enabled === true
      : parts[0] === "disabled" ? node !== undefined && node.enabled === false
      : parts[0] === "focused" ? node?.state?.focused === true
      : parts[0] === "unfocused" ? node !== undefined && node.state?.focused !== true
      : parts[0] === "checked" ? node?.state?.checked === true
      : parts[0] === "unchecked" ? node !== undefined && node.state?.checked !== true
      : parts[0] === "text" ? node?.label === expected
      : parts[0] === "value" ? String(node?.state?.value ?? "") === expected
      : false;
    if (matched) return;
    await sleep(50);
  }
  throw new Error(`Timed out waiting for recording condition: ${condition}`);
}

function actionValue(input: SemanticActionInput): string | undefined {
  if (input.action === "set_checked") return String(input.checked === true);
  if (input.action === "press_key") return input.key;
  if (input.action === "set_value" || input.action === "select") return input.value;
  return undefined;
}

function actionCommandType(action: RecordedAction): BridgeCommandInput["type"] {
  switch (action) {
    case "click": return "click_node";
    case "focus": return "focus_node";
    case "set_value": return "set_value";
    case "set_checked": return "set_checked";
    case "select": return "select";
    case "scroll": return "scroll";
    case "press_key": return "press_key";
  }
}

function readSecret(secrets: Record<string, unknown>, key: string): string {
  const value = secrets[key];
  if (typeof value !== "string") throw new Error(`Replay requires a secret value for key '${key}'.`);
  return value;
}

function compactResult<T extends Record<string, unknown>>(value: T): T {
  return Object.fromEntries(Object.entries(value).filter(([, item]) => item !== undefined)) as T;
}

class GuaBridgeClient {
  private socket: WebSocket | null = null;
  private connectPromise: Promise<WebSocket> | null = null;
  private nextId = 1;
  private readonly pending = new Map<number, PendingRequest>();

  constructor(
    private readonly url: string,
    private readonly requestTimeoutMs = 5000,
  ) {
  }

  async getUiTree(): Promise<GuaUiTree> {
    return this.request<GuaUiTree>({ type: "get_ui_tree" });
  }

  async getLogs(): Promise<GuaLogEntry[]> {
    return this.request<GuaLogEntry[]>({ type: "get_logs" });
  }

  async getScreenshot(): Promise<GuaScreenshot> {
    return this.request<GuaScreenshot>({ type: "get_screenshot" });
  }

  async performAction(input: SemanticActionInput): Promise<GuaActionReceipt | null> {
    const type = actionCommandType(input.action);
    return this.request<GuaActionReceipt | null>(compactResult({
      type,
      nodeId: input.nodeId,
      value: input.action === "press_key" ? undefined : input.value,
      checked: input.checked,
      key: input.key,
      modifiers: input.modifiers,
      deltaX: input.deltaX,
      deltaY: input.deltaY,
      scrollUnit: input.scrollUnit,
      sensitive: input.sensitive,
    }) as BridgeCommandInput);
  }

  async waitForAction(requestId: number, timeoutMs: number): Promise<GuaActionEvent> {
    const startedAt = Date.now();
    while (Date.now() - startedAt <= timeoutMs) {
      const event = await this.request<GuaActionEvent | null>({ type: "poll_events", requestId });
      if (event !== null) return event;
      await sleep(25);
    }
    throw new Error(`Timed out waiting for Gua action request ${requestId}.`);
  }

  async waitForNode(nodeId: string, timeoutMs: number): Promise<{ ok: true; node: GuaNode }> {
    const startedAt = Date.now();

    while (Date.now() - startedAt <= timeoutMs) {
      const tree = await this.getUiTree();
      const node = tree.nodes.find((candidate) => candidate.id === nodeId);
      if (node !== undefined) {
        return { ok: true, node };
      }

      await sleep(50);
    }

    throw new Error(`Timed out waiting for Gua node: ${nodeId}`);
  }

  close(): void {
    this.rejectAll(new Error("Gua MCP bridge client closed."));
    this.socket?.close();
    this.socket = null;
    this.connectPromise = null;
  }

  private async request<T>(command: BridgeCommandInput): Promise<T> {
    const socket = await this.connect();
    const id = this.nextId++;
    const payload = { ...command, id } as BridgeCommand;

    return new Promise<T>((resolve, reject) => {
      const timeoutId = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`Timed out waiting for Gua bridge command: ${command.type}`));
      }, this.requestTimeoutMs);

      this.pending.set(id, {
        resolve: (value) => resolve(value as T),
        reject,
        timeoutId,
      });

      socket.send(JSON.stringify(payload));
    });
  }

  private async connect(): Promise<WebSocket> {
    if (this.socket !== null && this.socket.readyState === WebSocket.OPEN) {
      return this.socket;
    }

    if (this.connectPromise !== null) {
      return this.connectPromise;
    }

    this.connectPromise = new Promise<WebSocket>((resolve, reject) => {
      const socket = new WebSocket(this.url);

      socket.addEventListener("open", () => {
        this.socket = socket;
        this.connectPromise = null;
        resolve(socket);
      });

      socket.addEventListener("message", (event) => {
        this.handleMessage(event.data);
      });

      socket.addEventListener("close", () => {
        this.socket = null;
        this.connectPromise = null;
        this.rejectAll(new Error("Gua bridge WebSocket connection closed."));
      });

      socket.addEventListener("error", () => {
        const error = new Error(`Failed to connect to Gua bridge at ${this.url}.`);
        this.connectPromise = null;
        reject(error);
        this.rejectAll(error);
      });
    });

    return this.connectPromise;
  }

  private handleMessage(data: unknown): void {
    if (typeof data !== "string") {
      return;
    }

    let response: GuaBridgeResponse;
    try {
      response = JSON.parse(data) as GuaBridgeResponse;
    } catch {
      return;
    }

    if (!("id" in response)) {
      return;
    }

    const pending = this.pending.get(response.id);
    if (pending === undefined) {
      return;
    }

    clearTimeout(pending.timeoutId);
    this.pending.delete(response.id);

    if (response.ok) {
      pending.resolve(response.result);
    } else {
      pending.reject(new Error(response.error));
    }
  }

  private rejectAll(error: Error): void {
    for (const [id, pending] of this.pending) {
      clearTimeout(pending.timeoutId);
      pending.reject(error);
      this.pending.delete(id);
    }
  }
}

type BridgeCommandInput =
  | { type: "get_ui_tree" }
  | { type: "get_logs" }
  | { type: "get_screenshot" }
  | { type: "poll_events"; requestId: number }
  | {
      type: "click_node" | "focus_node" | "set_value" | "set_checked" | "select" | "scroll" | "press_key";
      nodeId?: string;
      value?: string;
      checked?: boolean;
      key?: string;
      modifiers?: number;
      deltaX?: number;
      deltaY?: number;
      scrollUnit?: number;
      sensitive?: boolean;
    };

type BridgeCommand = BridgeCommandInput & { id: number };

interface PendingRequest {
  resolve(value: unknown): void;
  reject(reason: Error): void;
  timeoutId: ReturnType<typeof setTimeout>;
}

interface TestStep {
  action: "wait_for_node" | "click_node";
  nodeId: string;
  timeoutMs?: number;
}

interface TestStepResult {
  index: number;
  action: TestStep["action"];
  nodeId: string;
  ok: true;
}

function readTestSteps(args: Record<string, unknown>): TestStep[] {
  if (!Array.isArray(args.steps) || args.steps.length === 0) {
    throw new Error("run_test requires at least one step.");
  }

  return args.steps.map((step, index) => {
    if (!isRecord(step)) {
      throw new Error(`run_test step ${index} must be an object.`);
    }

    if (step.action !== "wait_for_node" && step.action !== "click_node") {
      throw new Error(`run_test step ${index} has unsupported action.`);
    }

    return {
      action: step.action,
      nodeId: readStringArg(step, "nodeId"),
      timeoutMs: readIntegerArg(step, "timeoutMs", 5000),
    };
  });
}

function readStringArg(args: Record<string, unknown>, name: string, allowEmpty = false): string {
  const value = args[name];
  if (typeof value !== "string" || (!allowEmpty && value.length === 0)) {
    throw new Error(`Expected non-empty string argument: ${name}`);
  }

  return value;
}

function readOptionalStringArg(args: Record<string, unknown>, name: string): string | undefined {
  const value = args[name];
  if (value === undefined) return undefined;
  if (typeof value !== "string" || value.length === 0) throw new Error(`Expected non-empty string argument: ${name}`);
  return value;
}

function readIntegerArg(args: Record<string, unknown>, name: string, fallback: number): number {
  const value = args[name];
  if (value === undefined) {
    return fallback;
  }

  if (typeof value !== "number" || !Number.isInteger(value) || value < 0) {
    throw new Error(`Expected non-negative integer argument: ${name}`);
  }

  return value;
}

function readNumberArg(args: Record<string, unknown>, name: string, fallback: number): number {
  const value = args[name];
  if (value === undefined) return fallback;
  if (typeof value !== "number" || !Number.isFinite(value)) throw new Error(`Expected finite number argument: ${name}`);
  return value;
}

function readBooleanArg(args: Record<string, unknown>, name: string, fallback: boolean): boolean {
  const value = args[name];
  if (value === undefined) return fallback;
  if (typeof value !== "boolean") throw new Error(`Expected boolean argument: ${name}`);
  return value;
}

function readRequiredBooleanArg(args: Record<string, unknown>, name: string): boolean {
  if (args[name] === undefined) throw new Error(`Expected boolean argument: ${name}`);
  return readBooleanArg(args, name, false);
}

function readMasks(value: unknown): Array<{ x: number; y: number; width: number; height: number }> | undefined {
  if (value === undefined) return undefined;
  if (!Array.isArray(value)) throw new Error("Expected masks to be an array.");
  return value.map((mask, index) => {
    if (!isRecord(mask)) throw new Error(`Mask ${index} must be an object.`);
    const x = readIntegerArg(mask, "x", 0);
    const y = readIntegerArg(mask, "y", 0);
    const width = readIntegerArg(mask, "width", 0);
    const height = readIntegerArg(mask, "height", 0);
    if (width === 0 || height === 0) throw new Error(`Mask ${index} width and height must be positive.`);
    return { x, y, width, height };
  });
}

function textResult(value: unknown, isError = false): ToolResult {
  return {
    content: [
      {
        type: "text",
        text: typeof value === "string" ? value : JSON.stringify(value, null, 2),
      },
    ],
    isError,
  };
}

function isJsonRpcRequest(value: unknown): value is JsonRpcRequest {
  return isRecord(value) && typeof value.method === "string";
}

function isJsonRpcId(value: unknown): value is JsonRpcId {
  return typeof value === "string" || typeof value === "number" || value === null;
}

function isGuaMcpTool(value: string): value is GuaMcpTool {
  return (guaMcpTools as readonly string[]).includes(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function objectSchema(
  properties: Record<string, unknown>,
  required: string[] = [],
): Record<string, unknown> {
  return {
    type: "object",
    additionalProperties: false,
    properties,
    required,
  };
}

function stringProperty(description: string): Record<string, unknown> {
  return {
    type: "string",
    minLength: 1,
    description,
  };
}

function numberProperty(description: string): Record<string, unknown> {
  return { type: "number", description };
}

function writeResponse(response: JsonRpcResponse): void {
  process.stdout.write(`${JSON.stringify(response)}\n`);
}

function writeLog(level: "info" | "error", message: string): void {
  process.stderr.write(`[gui-mcp] ${level}: ${message}\n`);
}

function jsonRpcError(code: number, message: string, data?: unknown): JsonRpcError {
  return { code, message, data };
}

function toJsonRpcError(error: unknown): JsonRpcError {
  if (error instanceof RpcFailure) {
    return jsonRpcError(error.code, error.message);
  }

  return jsonRpcError(-32603, (error as Error).message);
}

class RpcFailure extends Error {
  constructor(
    readonly code: number,
    message: string,
  ) {
    super(message);
  }
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
