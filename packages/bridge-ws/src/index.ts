import type {
  GuaInspectorCommand,
  GuaInspectorResponse,
  GuaLogEntry,
  GuaScreenshot,
  GuaUiTree,
  GuaClockStatus,
  GuaContextStatus,
  GuaWorldObjectTree,
} from "@gua/inspector/core";

const defaultPort = 8765;
const port = Number.parseInt(Bun.env.GUA_BRIDGE_PORT ?? Bun.argv[2] ?? `${defaultPort}`, 10);
type GuaInspectorResult = GuaUiTree | GuaWorldObjectTree | GuaLogEntry[] | GuaScreenshot | GuaClockStatus | GuaContextStatus | null;

export function handleMessage(message: string | Buffer, target: DemoRuntime = runtime): GuaInspectorResponse {
  if (typeof message !== "string") {
    return { id: 0, ok: false, error: "Expected a JSON text message." };
  }

  let command: GuaInspectorCommand;
  try {
    command = JSON.parse(message) as GuaInspectorCommand;
  } catch (error) {
    return { id: 0, ok: false, error: `Invalid command JSON: ${(error as Error).message}` };
  }

  try {
    switch (command.type) {
      case "get_ui_tree":
        return ok(command.id, target.getUiTree());
      case "get_world_object_tree":
        return ok(command.id, { schemaVersion: 1, sessionEpoch: 1, frameSequence: 0, revision: 0, scene: "demo", objects: [] });
      case "get_logs":
        return ok(command.id, target.getLogs());
      case "get_screenshot":
        return ok(command.id, target.getScreenshot());
      case "get_context_status": return ok(command.id, target.getContextStatus());
      case "get_clock": return ok(command.id, target.getClock());
      case "clock_install": return ok(command.id, target.installClock(command.initialTimeMs, command.stepMs));
      case "clock_pause": return ok(command.id, target.pauseClock());
      case "clock_run_for": return ok(command.id, target.runClockFor(command.durationMs, command.stepMs));
      case "clock_resume": return ok(command.id, target.resumeClock());
      case "poll_events":
        return ok(command.id, null);
      case "click_node":
        target.clickNode(command.nodeId);
        return ok(command.id, null);
      case "focus_node":
        target.focusNode(command.nodeId);
        return ok(command.id, null);
      case "press_key":
        target.pressKey(command.key);
        return ok(command.id, null);
      case "set_value":
        target.log("info", `set_value(${command.nodeId})`);
        return ok(command.id, null);
      case "set_checked":
        target.log("info", `set_checked(${command.nodeId}, ${command.checked})`);
        return ok(command.id, null);
      case "select":
        target.log("info", `select(${command.nodeId}, ${command.value})`);
        return ok(command.id, null);
      case "scroll":
        target.log("info", `scroll(${command.nodeId}, ${command.deltaX}, ${command.deltaY})`);
        return ok(command.id, null);
    }
  } catch (error) {
    return { id: command.id, ok: false, error: (error as Error).message };
  }
}

function ok(id: number, result: GuaInspectorResult): GuaInspectorResponse {
  return { id, ok: true, result };
}

export class DemoRuntime {
  private clock: GuaClockStatus = { schemaVersion: 1, installed: false, state: "running", nowMs: 0, defaultStepMs: 1000 / 60, pendingMs: 0, generation: 0, completedOperationSequence: 0 };
  private nextClockOperationSequence = 1;
  private runningClockStartMs = 0;
  private runningClockStartTimeMs = 0;
  private screen: "title" | "loading" = "title";
  private focusedNodeId = "start";
  private frameSequence = 1;
  private revision = 1;
  private logs: GuaLogEntry[] = [
    { sequence: 1, level: "info", message: "Demo runtime started." },
    { sequence: 2, level: "debug", message: "Serving Gua protocol snapshots over WebSocket." },
  ];

  constructor(private readonly monotonicNow: () => number = () => performance.now()) {}

  getUiTree(): GuaUiTree {
    if (this.screen === "loading") {
      return {
        schemaVersion: 2,
        sessionEpoch: 1,
        frameSequence: this.frameSequence,
        revision: this.revision,
        screen: "loading",
        nodes: [
          node("root", "screen", "Loading Screen", { x: 0, y: 0, w: 1280, h: 720 }, false),
          node("loading", "text", "Loading...", { x: 544, y: 328, w: 192, h: 48 }, false, "root"),
        ],
      };
    }

    return {
      schemaVersion: 2,
      sessionEpoch: 1,
      frameSequence: this.frameSequence,
      revision: this.revision,
      screen: "title",
      nodes: [
        node("root", "screen", "Title Screen", { x: 0, y: 0, w: 1280, h: 720 }, false),
        node("menu", "panel", "Main Menu", { x: 448, y: 232, w: 384, h: 256 }, false, "root"),
        node("start", "button", "Start Game", { x: 512, y: 312, w: 256, h: 56 }, true, "menu", this.focusedNodeId === "start"),
        node("settings", "button", "Settings", { x: 512, y: 384, w: 256, h: 56 }, true, "menu", this.focusedNodeId === "settings"),
      ],
    };
  }

  getLogs(): GuaLogEntry[] {
    return this.logs;
  }

  getScreenshot(): GuaScreenshot {
    return {
      dataUri: `data:image/svg+xml,${encodeURIComponent(this.renderScreenshotSvg())}`,
      width: 1280,
      height: 720,
    };
  }
  getContextStatus(): GuaContextStatus {
    return {
      sessionEpoch: 1,
      frameSequence: this.frameSequence,
      revision: this.revision,
      nodeCount: this.getUiTree().nodes.length,
      pendingRequestCount: 0,
      inFlightRequestCount: 0,
      unconsumedEventCount: 0,
      logCount: this.logs.length,
      hasScreenshot: true,
      firstPendingAction: 0,
      firstPendingNodeId: "",
      firstEventAction: 0,
      firstEventNodeId: "",
    };
  }
  getClock() { this.advanceRunningClock(); return this.clock; }
  installClock(initial = 0, step = 1000 / 60) {
    if (this.clock.installed) throw new Error("invalid_state");
    const nextTime = initial + step;
    if (!Number.isFinite(initial) || initial < 0 || !Number.isFinite(step) || step <= 0 ||
        !Number.isFinite(nextTime) || nextTime <= initial) throw new Error("invalid_duration");
    this.clock = { ...this.clock, installed: true, state: "running", nowMs: initial, defaultStepMs: step, generation: this.clock.generation + 1 };
    this.runningClockStartMs = initial;
    this.runningClockStartTimeMs = this.monotonicNow();
    return this.clock;
  }
  pauseClock() { if (!this.clock.installed) throw new Error("not_installed"); this.advanceRunningClock(); this.clock = { ...this.clock, state: "paused" }; return this.clock; }
  runClockFor(duration: number, step?: number) {
    if (this.clock.state !== "paused") throw new Error("invalid_state");
    const nextTime = this.clock.nowMs + duration;
    const selectedStep = step ?? this.clock.defaultStepMs;
    const emittedStep = Math.min(duration, selectedStep);
    if (!Number.isFinite(duration) || duration < 0 || !Number.isFinite(nextTime) || duration > 0 && nextTime <= this.clock.nowMs ||
        !Number.isFinite(selectedStep) || selectedStep <= 0 ||
        !Number.isFinite(this.clock.nowMs + selectedStep) || this.clock.nowMs + selectedStep <= this.clock.nowMs ||
        duration > 0 && (this.clock.nowMs + emittedStep <= this.clock.nowMs || nextTime - emittedStep >= nextTime))
      throw new Error("invalid_duration");
    const operationSequence = this.nextClockOperationSequence++;
    this.clock = { ...this.clock, nowMs: nextTime };
    const result = { ...this.clock, operationSequence, completionSessionEpoch: 1, completionAfterFrameSequence: this.frameSequence };
    setTimeout(() => {
      this.clock = { ...this.clock, completedOperationSequence: operationSequence };
      this.frameSequence += 1;
    }, 0);
    return result;
  }
  resumeClock() { if (!this.clock.installed) throw new Error("not_installed"); this.advanceRunningClock(); this.clock = { ...this.clock, state: "running" }; this.runningClockStartMs = this.clock.nowMs; this.runningClockStartTimeMs = this.monotonicNow(); return this.clock; }

  private advanceRunningClock() {
    if (!this.clock.installed || this.clock.state !== "running") return;
    const elapsedMs = Math.max(0, this.monotonicNow() - this.runningClockStartTimeMs);
    const nextTime = this.runningClockStartMs + elapsedMs;
    if (Number.isFinite(nextTime) && nextTime > this.clock.nowMs)
      this.clock = { ...this.clock, nowMs: nextTime };
  }

  clickNode(nodeId: string): void {
    this.assertNodeExists(nodeId);
    this.log("info", `click_node(${nodeId})`);

    if (nodeId === "start") {
      this.screen = "loading";
      this.focusedNodeId = "loading";
      this.frameSequence += 1;
      this.revision += 1;
      this.log("info", "Screen changed to loading.");
    }
  }

  focusNode(nodeId: string): void {
    this.assertNodeExists(nodeId);
    if (this.focusedNodeId !== nodeId) {
      this.frameSequence += 1;
      this.revision += 1;
    }
    this.focusedNodeId = nodeId;
    this.log("debug", `focus_node(${nodeId})`);
  }

  pressKey(key: string): void {
    if (key.length === 0) {
      throw new Error("Gua key must not be empty.");
    }

    this.log("info", `press_key(${key})`);
  }

  log(level: GuaLogEntry["level"], message: string): void {
    this.logs = [
      ...this.logs,
      {
        sequence: this.logs.length + 1,
        level,
        message,
      },
    ];
  }

  private assertNodeExists(nodeId: string): void {
    if (!this.getUiTree().nodes.some((candidate) => candidate.id === nodeId)) {
      throw new Error(`Gua node not found: ${nodeId}`);
    }
  }

  private renderScreenshotSvg(): string {
    const tree = this.getUiTree();
    const title = tree.screen === "loading" ? "Loading..." : "Gua Demo Runtime";
    const subtitle = tree.screen === "loading" ? "Start command was received over WebSocket." : "Connected through the sample bridge.";

    return `<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="720" viewBox="0 0 1280 720">
      <rect width="1280" height="720" fill="#101820"/>
      <rect x="448" y="232" width="384" height="256" fill="#1f2937" stroke="#4b647f" stroke-width="2"/>
      <text x="640" y="284" fill="#e8edf4" font-family="Segoe UI, sans-serif" font-size="34" text-anchor="middle">${title}</text>
      <text x="640" y="520" fill="#91a4b7" font-family="Segoe UI, sans-serif" font-size="20" text-anchor="middle">${subtitle}</text>
      ${tree.screen === "title" ? this.renderButton("Start Game", 512, 312, this.focusedNodeId === "start") : ""}
      ${tree.screen === "title" ? this.renderButton("Settings", 512, 384, this.focusedNodeId === "settings") : ""}
    </svg>`;
  }

  private renderButton(label: string, x: number, y: number, focused: boolean): string {
    const stroke = focused ? "#f2c66d" : "#5d7288";
    return `<rect x="${x}" y="${y}" width="256" height="56" fill="#253448" stroke="${stroke}" stroke-width="2"/>
      <text x="${x + 128}" y="${y + 36}" fill="#f5f7fb" font-family="Segoe UI, sans-serif" font-size="22" text-anchor="middle">${label}</text>`;
  }
}

function node(
  id: string,
  role: string,
  label: string,
  bounds: { x: number; y: number; w: number; h: number },
  enabled: boolean,
  parentId?: string,
  focused = false,
): GuaUiTree["nodes"][number] {
  return {
    id,
    parentId,
    role,
    label,
    visible: true,
    enabled,
    bounds,
    state: focused ? { focused: true } : undefined,
    actions: enabled ? ["click", "focus"] : [],
  };
}

const runtime = new DemoRuntime();

if (import.meta.main) {
  const server = Bun.serve({
    port,
    fetch(request, server) {
      if (server.upgrade(request)) {
        return undefined;
      }

      return Response.json({
        name: "Gua WebSocket bridge",
        websocket: `ws://127.0.0.1:${port}`,
        commands: ["get_ui_tree", "get_logs", "get_screenshot", "click_node", "focus_node", "press_key"],
      });
    },
    websocket: {
      open() {
        runtime.log("info", "Inspector connected.");
      },
      message(socket, message) {
        const response = handleMessage(message);
        socket.send(JSON.stringify(response));
      },
      close() {
        runtime.log("info", "Inspector disconnected.");
      },
    },
  });

  console.log(`Gua WebSocket bridge listening on ws://127.0.0.1:${server.port}`);
}
