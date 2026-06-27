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

function parseJson<T>(json: string, description: string): T {
  try {
    return JSON.parse(json) as T;
  } catch (error) {
    throw new Error(`Invalid ${description} JSON: ${(error as Error).message}`);
  }
}
