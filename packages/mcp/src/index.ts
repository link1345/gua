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

type BridgeResult = GuaUiTree | GuaLogEntry[] | GuaScreenshot | null;

export interface GuaMcpServerOptions {
  bridgeUrl?: string;
}

const defaultBridgeUrl = "ws://127.0.0.1:8765";

export const guaMcpTools = [
  "get_ui_tree",
  "click_node",
  "press_key",
  "wait_for_node",
  "get_screenshot",
  "get_logs",
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
    description: "Send a click command for a visible semantic UI node.",
    inputSchema: objectSchema({
      nodeId: stringProperty("The target Gua node id."),
    }, ["nodeId"]),
  },
  {
    name: "press_key",
    description: "Send a key press command through the Gua bridge when the bridge supports it.",
    inputSchema: objectSchema({
      key: stringProperty("The logical key name to press, such as Enter or Escape."),
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

  writeLog("info", `Gua MCP server connecting to ${bridgeUrl}`);

  let pending = "";
  const decoder = new TextDecoder();
  try {
    for await (const chunk of Bun.stdin.stream()) {
      pending += decoder.decode(chunk, { stream: true });
      pending = await drainPendingLines(pending, bridge);
    }

    pending += decoder.decode();
    const finalLine = pending.trim();
    if (finalLine.length > 0) {
      await handleLine(finalLine, bridge);
    }
  } finally {
    bridge.close();
  }
}

async function drainPendingLines(input: string, bridge: GuaBridgeClient): Promise<string> {
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

    await handleLine(line, bridge);
  }
}

async function handleLine(line: string, bridge: GuaBridgeClient): Promise<void> {
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
      result: await handleRequest(request, bridge),
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

async function handleRequest(request: JsonRpcRequest, bridge: GuaBridgeClient): Promise<unknown> {
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
      return callTool(request.params, bridge);
    default:
      throw new RpcFailure(-32601, `Unsupported MCP method: ${request.method}`);
  }
}

async function callTool(params: unknown, bridge: GuaBridgeClient): Promise<ToolResult> {
  if (!isRecord(params) || typeof params.name !== "string") {
    throw new RpcFailure(-32602, "tools/call requires a tool name.");
  }

  const name = params.name;
  if (!isGuaMcpTool(name)) {
    throw new RpcFailure(-32602, `Unknown Gua MCP tool: ${name}`);
  }

  try {
    const result = await executeTool(name, isRecord(params.arguments) ? params.arguments : {}, bridge);
    return textResult(result);
  } catch (error) {
    return textResult({ error: (error as Error).message }, true);
  }
}

async function executeTool(name: GuaMcpTool, args: Record<string, unknown>, bridge: GuaBridgeClient): Promise<unknown> {
  switch (name) {
    case "get_ui_tree":
      return bridge.getUiTree();
    case "click_node":
      await bridge.clickNode(readStringArg(args, "nodeId"));
      return { ok: true };
    case "press_key":
      await bridge.pressKey(readStringArg(args, "key"));
      return { ok: true };
    case "wait_for_node":
      return bridge.waitForNode(
        readStringArg(args, "nodeId"),
        readIntegerArg(args, "timeoutMs", 5000),
      );
    case "get_screenshot":
      return bridge.getScreenshot();
    case "get_logs":
      return bridge.getLogs();
    case "run_test":
      return runTest(readTestSteps(args), bridge);
  }
}

async function runTest(steps: TestStep[], bridge: GuaBridgeClient): Promise<{ ok: true; steps: TestStepResult[] }> {
  const results: TestStepResult[] = [];

  for (const [index, step] of steps.entries()) {
    if (step.action === "wait_for_node") {
      await bridge.waitForNode(step.nodeId, step.timeoutMs ?? 5000);
    } else {
      await bridge.clickNode(step.nodeId);
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

  async clickNode(nodeId: string): Promise<void> {
    await this.request<null>({ type: "click_node", nodeId });
  }

  async pressKey(key: string): Promise<void> {
    await this.request<null>({ type: "press_key", key });
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
  | { type: "click_node"; nodeId: string }
  | { type: "press_key"; key: string };

type BridgeCommand = BridgeCommandInput & { id: number };

interface PendingRequest {
  resolve(value: BridgeResult): void;
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

function readStringArg(args: Record<string, unknown>, name: string): string {
  const value = args[name];
  if (typeof value !== "string" || value.length === 0) {
    throw new Error(`Expected non-empty string argument: ${name}`);
  }

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
