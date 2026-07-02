export interface GuaBounds {
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface GuaNodeState {
  focused?: boolean;
  hovered?: boolean;
  pressed?: boolean;
  checked?: boolean;
  value?: number | string | boolean | null;
}

export interface GuaNode {
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

export interface GuaUiTree {
  screen: string;
  nodes: GuaNode[];
}

export interface GuaLogEntry {
  sequence: number;
  level: "trace" | "debug" | "info" | "warn" | "error";
  message: string;
}

export interface GuaScreenshot {
  dataUri: string;
  width: number;
  height: number;
}

export interface InspectorPanel {
  id: "tree" | "node" | "screenshot" | "logs";
  title: string;
}

export interface InspectorSnapshot {
  uiTree: GuaUiTree;
  logs: GuaLogEntry[];
  screenshot: GuaScreenshot;
}

export interface InspectorState extends InspectorSnapshot {
  selectedNodeId: string | null;
}

export interface GuaInspectorClient {
  getUiTree(): Promise<GuaUiTree>;
  getLogs(): Promise<GuaLogEntry[]>;
  getScreenshot(): Promise<GuaScreenshot>;
  clickNode(nodeId: string): Promise<void>;
  focusNode(nodeId: string): Promise<void>;
}

export type GuaInspectorCommand =
  | { id: number; type: "get_ui_tree" }
  | { id: number; type: "get_logs" }
  | { id: number; type: "get_screenshot" }
  | { id: number; type: "click_node"; nodeId: string }
  | { id: number; type: "focus_node"; nodeId: string }
  | { id: number; type: "press_key"; key: string };

type GuaInspectorCommandInput =
  | { type: "get_ui_tree" }
  | { type: "get_logs" }
  | { type: "get_screenshot" }
  | { type: "click_node"; nodeId: string }
  | { type: "focus_node"; nodeId: string };

export type GuaInspectorResponse =
  | { id: number; ok: true; result: GuaUiTree | GuaLogEntry[] | GuaScreenshot | null }
  | { id: number; ok: false; error: string };

export type GuaInspectorNotification =
  | { type: "snapshot"; snapshot: InspectorSnapshot };

export type SnapshotListener = (snapshot: InspectorSnapshot) => void;

export const initialPanels: InspectorPanel[] = [
  { id: "tree", title: "UI Tree" },
  { id: "node", title: "Node Detail" },
  { id: "screenshot", title: "Screenshot" },
  { id: "logs", title: "Logs" },
];

export function createInspectorState(snapshot?: Partial<InspectorSnapshot>): InspectorState {
  const uiTree = snapshot?.uiTree ?? { screen: "unknown", nodes: [] };
  return {
    uiTree,
    logs: snapshot?.logs ?? [],
    screenshot: snapshot?.screenshot ?? { dataUri: "", width: 0, height: 0 },
    selectedNodeId: uiTree.nodes[0]?.id ?? null,
  };
}

export function parseInspectorSnapshot(input: {
  uiTreeJson: string;
  logsJson?: string;
  screenshotJson?: string;
}): InspectorSnapshot {
  return {
    uiTree: parseJson<GuaUiTree>(input.uiTreeJson, "Gua UI tree"),
    logs: input.logsJson === undefined ? [] : parseJson<GuaLogEntry[]>(input.logsJson, "Gua logs"),
    screenshot: input.screenshotJson === undefined
      ? { dataUri: "", width: 0, height: 0 }
      : parseJson<GuaScreenshot>(input.screenshotJson, "Gua screenshot"),
  };
}

export function selectNode(state: InspectorState, nodeId: string | null): InspectorState {
  const selectedNodeId = nodeId !== null && state.uiTree.nodes.some((node) => node.id === nodeId)
    ? nodeId
    : state.uiTree.nodes[0]?.id ?? null;

  return {
    ...state,
    selectedNodeId,
  };
}

export function updateInspectorState(state: InspectorState, snapshot: Partial<InspectorSnapshot>): InspectorState {
  const next: InspectorState = {
    ...state,
    ...snapshot,
  };

  return selectNode(next, state.selectedNodeId);
}

export function getSelectedNode(state: InspectorState): GuaNode | null {
  return state.uiTree.nodes.find((node) => node.id === state.selectedNodeId) ?? null;
}

export async function readSnapshot(client: GuaInspectorClient): Promise<InspectorSnapshot> {
  const [uiTree, logs, screenshot] = await Promise.all([
    client.getUiTree(),
    client.getLogs(),
    client.getScreenshot(),
  ]);

  return { uiTree, logs, screenshot };
}

export class MockInspectorClient implements GuaInspectorClient {
  private logs: GuaLogEntry[] = [
    { sequence: 1, level: "info", message: "Inspector connected to mock runtime." },
    { sequence: 2, level: "debug", message: "Title screen snapshot received." },
  ];

  private screen: "title" | "loading" = "title";

  async getUiTree(): Promise<GuaUiTree> {
    if (this.screen === "loading") {
      return {
        screen: "loading",
        nodes: [
          {
            id: "root",
            role: "screen",
            label: "Loading Screen",
            visible: true,
            enabled: false,
            bounds: { x: 0, y: 0, w: 1280, h: 720 },
            actions: [],
          },
          {
            id: "loading",
            role: "text",
            label: "Loading...",
            visible: true,
            enabled: false,
            bounds: { x: 544, y: 328, w: 192, h: 48 },
            actions: [],
          },
        ],
      };
    }

    return {
      screen: "title",
      nodes: [
        {
          id: "root",
          role: "screen",
          label: "Title Screen",
          visible: true,
          enabled: false,
          bounds: { x: 0, y: 0, w: 1280, h: 720 },
          actions: [],
        },
        {
          id: "menu",
          parentId: "root",
          role: "panel",
          label: "Main Menu",
          visible: true,
          enabled: false,
          bounds: { x: 448, y: 232, w: 384, h: 256 },
          actions: [],
        },
        {
          id: "start",
          parentId: "menu",
          role: "button",
          label: "Start Game",
          visible: true,
          enabled: true,
          bounds: { x: 512, y: 312, w: 256, h: 56 },
          state: { focused: true },
          actions: ["click", "focus"],
        },
        {
          id: "settings",
          parentId: "menu",
          role: "button",
          label: "Settings",
          visible: true,
          enabled: true,
          bounds: { x: 512, y: 384, w: 256, h: 56 },
          actions: ["click", "focus"],
        },
      ],
    };
  }

  async getLogs(): Promise<GuaLogEntry[]> {
    return this.logs;
  }

  async getScreenshot(): Promise<GuaScreenshot> {
    return {
      dataUri: "",
      width: 1280,
      height: 720,
    };
  }

  async clickNode(nodeId: string): Promise<void> {
    this.logs = [
      ...this.logs,
      {
        sequence: this.logs.length + 1,
        level: "info",
        message: `click_node(${nodeId})`,
      },
    ];

    if (nodeId === "start") {
      this.screen = "loading";
    }
  }

  async focusNode(nodeId: string): Promise<void> {
    this.logs = [
      ...this.logs,
      {
        sequence: this.logs.length + 1,
        level: "debug",
        message: `focus_node(${nodeId})`,
      },
    ];
  }
}

interface PendingRequest {
  resolve(value: unknown): void;
  reject(reason: Error): void;
  timeoutId: ReturnType<typeof setTimeout>;
}

export class WebSocketInspectorClient implements GuaInspectorClient {
  private socket: WebSocket | null = null;
  private connectPromise: Promise<WebSocket> | null = null;
  private nextId = 1;
  private pending = new Map<number, PendingRequest>();
  private snapshotListeners = new Set<SnapshotListener>();

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

  async focusNode(nodeId: string): Promise<void> {
    await this.request<null>({ type: "focus_node", nodeId });
  }

  close(): void {
    this.rejectAll(new Error("Gua Inspector WebSocket client closed."));
    this.socket?.close();
    this.socket = null;
    this.connectPromise = null;
    this.snapshotListeners.clear();
  }

  subscribeSnapshots(listener: SnapshotListener): () => void {
    this.snapshotListeners.add(listener);
    void this.connect().catch(() => {
      this.snapshotListeners.delete(listener);
    });
    return () => {
      this.snapshotListeners.delete(listener);
    };
  }

  private async request<T>(command: GuaInspectorCommandInput): Promise<T> {
    const socket = await this.connect();
    const id = this.nextId++;
    const payload = { ...command, id } as GuaInspectorCommand;

    return new Promise<T>((resolve, reject) => {
      const timeoutId = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`Timed out waiting for ${command.type}.`));
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
        this.rejectAll(new Error("Gua Inspector WebSocket connection closed."));
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

    let parsed: GuaInspectorResponse | GuaInspectorNotification;
    try {
      parsed = JSON.parse(data) as GuaInspectorResponse | GuaInspectorNotification;
    } catch {
      return;
    }

    if (isNotification(parsed)) {
      for (const listener of this.snapshotListeners) {
        listener(parsed.snapshot);
      }
      return;
    }

    const response = parsed;
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

function parseJson<T>(json: string, description: string): T {
  try {
    return JSON.parse(json) as T;
  } catch (error) {
    throw new Error(`Invalid ${description} JSON: ${(error as Error).message}`);
  }
}

function isNotification(value: GuaInspectorResponse | GuaInspectorNotification): value is GuaInspectorNotification {
  return "type" in value && value.type === "snapshot";
}
