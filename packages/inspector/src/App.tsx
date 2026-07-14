import { useCallback, useEffect, useRef, useState } from "react";

import {
  type GuaInspectorClient,
  type GuaNode,
  type InspectorSnapshot,
  type InspectorState,
  MockInspectorClient,
  WebSocketInspectorClient,
  createInspectorState,
  getSelectedNode,
  readSnapshot,
  selectNode,
  updateInspectorState,
} from "./core";
import {
  InspectorRecorder,
  type BrowserVisualResult,
  type GuaRecording,
  type SemanticActionInput,
  compareImages,
  replayRecording,
  validateRecording,
} from "./automation";

export interface GuaInspectorAppProps {
  client?: GuaInspectorClient;
}

const defaultWebSocketUrl = "ws://127.0.0.1:8765";

export function GuaInspectorApp({ client }: GuaInspectorAppProps) {
  const [inspectorClient, setInspectorClient] = useState<GuaInspectorClient>(() => client ?? new MockInspectorClient());
  const [clientLabel, setClientLabel] = useState(() => client === undefined ? "Mock runtime" : "Custom client");
  const [webSocketUrl, setWebSocketUrl] = useState(() => window.localStorage.getItem("gua.inspector.wsUrl") ?? defaultWebSocketUrl);
  const [state, setState] = useState<InspectorState>(() => createInspectorState());
  const [status, setStatus] = useState<"idle" | "refreshing" | "error">("idle");
  const [error, setError] = useState<string | null>(null);
  const [autoRefresh, setAutoRefresh] = useState(false);
  const recorder = useRef(new InspectorRecorder());
  const [recording, setRecording] = useState(false);
  const [lastRecording, setLastRecording] = useState<GuaRecording | null>(null);
  const [baselineDataUri, setBaselineDataUri] = useState<string | null>(null);
  const [visualResult, setVisualResult] = useState<BrowserVisualResult | null>(null);
  const [secretsJson, setSecretsJson] = useState("{}");

  useEffect(() => {
    if (client !== undefined) {
      setInspectorClient(client);
      setClientLabel("Custom client");
    }
  }, [client]);

  const refresh = useCallback(async () => {
    setStatus("refreshing");
    setError(null);
    try {
      const snapshot = await readSnapshot(inspectorClient);
      setState((current) => updateInspectorState(current, snapshot));
      setStatus("idle");
    } catch (caught) {
      setError((caught as Error).message);
      setStatus("error");
    }
  }, [inspectorClient]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  useEffect(() => {
    const maybeSubscribable = inspectorClient as GuaInspectorClient & {
      subscribeSnapshots?: (listener: (snapshot: InspectorSnapshot) => void) => () => void;
    };

    const unsubscribe = maybeSubscribable.subscribeSnapshots?.((snapshot) => {
      setState((current) => updateInspectorState(current, snapshot));
      setStatus("idle");
      setError(null);
    });

    return () => {
      unsubscribe?.();
    };
  }, [inspectorClient]);

  useEffect(() => {
    if (!autoRefresh) {
      return undefined;
    }

    const intervalId = window.setInterval(() => {
      void refresh();
    }, 500);

    return () => window.clearInterval(intervalId);
  }, [autoRefresh, refresh]);

  const connectMock = () => {
    closeClient(inspectorClient);
    setState(createInspectorState());
    setError(null);
    setClientLabel("Mock runtime");
    setInspectorClient(new MockInspectorClient());
  };

  const connectWebSocket = () => {
    closeClient(inspectorClient);
    window.localStorage.setItem("gua.inspector.wsUrl", webSocketUrl);
    setState(createInspectorState());
    setError(null);
    setClientLabel(webSocketUrl);
    setInspectorClient(new WebSocketInspectorClient(webSocketUrl));
  };

  const selectedNode = getSelectedNode(state);

  const performAction = async (action: SemanticActionInput) => {
    const before = await inspectorClient.getUiTree();
    const outcome = await inspectorClient.performAction(action);
    const after = await inspectorClient.getUiTree();
    recorder.current.record(action, outcome, before.revision, outcome.completion?.revision ?? after.revision);
    await refresh();
    return outcome;
  };

  const clickSelected = async () => {
    if (selectedNode === null || !selectedNode.actions.includes("click")) {
      return;
    }

    await performAction({ action: "click", nodeId: selectedNode.id });
  };

  const focusSelected = async () => {
    if (selectedNode === null || !selectedNode.actions.includes("focus")) {
      return;
    }

    await performAction({ action: "focus", nodeId: selectedNode.id });
  };

  const startRecording = () => {
    try {
      recorder.current.start();
      setRecording(true);
      setError(null);
    } catch (caught) { setError((caught as Error).message); }
  };

  const stopRecording = () => {
    try {
      setLastRecording(recorder.current.stop());
      setRecording(false);
      setError(null);
    } catch (caught) { setError((caught as Error).message); }
  };

  const replay = async () => {
    if (lastRecording === null) return;
    try {
      const parsedSecrets = JSON.parse(secretsJson) as unknown;
      if (typeof parsedSecrets !== "object" || parsedSecrets === null || Array.isArray(parsedSecrets)) {
        throw new Error("Secrets must be a JSON object.");
      }
      await replayRecording(
        lastRecording,
        () => inspectorClient.getUiTree(),
        (action) => inspectorClient.performAction(action),
        parsedSecrets as Record<string, string>,
      );
      await refresh();
      setError(null);
    } catch (caught) { setError((caught as Error).message); }
  };

  const compare = async () => {
    if (baselineDataUri === null || state.screenshot.dataUri.length === 0) return;
    try {
      setVisualResult(await compareImages(baselineDataUri, state.screenshot.dataUri));
      setError(null);
    } catch (caught) { setError((caught as Error).message); }
  };

  return (
    <div className="gua-app">
      <header className="gua-topbar">
        <div>
          <div className="gua-brand">Gua Inspector</div>
          <div className="gua-screen">{state.uiTree.screen} · {clientLabel}</div>
        </div>
        <div className="gua-topbar__actions">
          {client === undefined ? (
            <form
              className="gua-connect"
              onSubmit={(event) => {
                event.preventDefault();
                connectWebSocket();
              }}
            >
              <input
                aria-label="Gua WebSocket bridge URL"
                value={webSocketUrl}
                onChange={(event) => setWebSocketUrl(event.currentTarget.value)}
              />
              <button type="submit">Connect</button>
              <button type="button" onClick={connectMock}>Mock</button>
            </form>
          ) : null}
          <label className="gua-toggle">
            <input
              type="checkbox"
              checked={autoRefresh}
              onChange={(event) => setAutoRefresh(event.currentTarget.checked)}
            />
            <span>Poll</span>
          </label>
          {error !== null ? <span className="gua-error">{error}</span> : null}
          <button type="button" onClick={() => void refresh()} disabled={status === "refreshing"}>
            {status === "refreshing" ? "Refreshing" : "Refresh"}
          </button>
        </div>
      </header>

      <main className="gua-layout">
        <TreePanel
          nodes={state.uiTree.nodes}
          selectedNodeId={state.selectedNodeId}
          onSelect={(nodeId) => setState((current) => selectNode(current, nodeId))}
        />
        <NodeDetailPanel
          node={selectedNode}
          onClick={() => void clickSelected()}
          onFocus={() => void focusSelected()}
          onAction={(action) => void performAction(action)}
        />
        <ScreenshotPanel screenshot={state.screenshot} selectedNode={selectedNode} />
        <LogPanel logs={state.logs} />
        <AutomationPanel
          recording={recording}
          lastRecording={lastRecording}
          screenshotDataUri={state.screenshot.dataUri}
          baselineDataUri={baselineDataUri}
          visualResult={visualResult}
          secretsJson={secretsJson}
          onSecretsJson={setSecretsJson}
          onStart={startRecording}
          onStop={stopRecording}
          onReplay={() => void replay()}
          onDownloadRecording={() => lastRecording !== null && downloadText("gua-recording.json", JSON.stringify(lastRecording, null, 2), "application/json")}
          onImportRecording={(text) => {
            try {
              const value = JSON.parse(text) as unknown;
              validateRecording(value);
              setLastRecording(value);
              setError(null);
            }
            catch (caught) { setError((caught as Error).message); }
          }}
          onUseCurrentBaseline={() => { setBaselineDataUri(state.screenshot.dataUri); setVisualResult(null); }}
          onBaseline={setBaselineDataUri}
          onCompare={() => void compare()}
        />
      </main>
    </div>
  );
}

function closeClient(client: GuaInspectorClient): void {
  const maybeClosable = client as GuaInspectorClient & { close?: () => void };
  maybeClosable.close?.();
}

interface TreePanelProps {
  nodes: GuaNode[];
  selectedNodeId: string | null;
  onSelect(nodeId: string): void;
}

function TreePanel({ nodes, selectedNodeId, onSelect }: TreePanelProps) {
  return (
    <section className="gua-panel gua-tree-panel">
      <PanelHeader title="UI Tree" detail={`${nodes.length} nodes`} />
      <ol className="gua-tree">
        {nodes.map((node) => (
          <li key={node.id}>
            <button
              type="button"
              className="gua-tree__node"
              data-depth={node.parentId === undefined ? 0 : 1}
              aria-selected={node.id === selectedNodeId}
              onClick={() => onSelect(node.id)}
            >
              <span className="gua-role">{node.role}</span>
              <span className="gua-tree__label">
                <span>{node.label || node.id}</span>
                <small>#{node.id}</small>
              </span>
              <NodeFlags node={node} />
            </button>
          </li>
        ))}
      </ol>
    </section>
  );
}

interface NodeDetailPanelProps {
  node: GuaNode | null;
  onClick(): void;
  onFocus(): void;
  onAction(action: SemanticActionInput): void;
}

function NodeDetailPanel({ node, onClick, onFocus, onAction }: NodeDetailPanelProps) {
  const [value, setValue] = useState("");
  const [key, setKey] = useState("Enter");
  const [sensitive, setSensitive] = useState(false);
  const [secretKey, setSecretKey] = useState("");
  if (node === null) {
    return (
      <section className="gua-panel">
        <PanelHeader title="Node Detail" />
        <p className="gua-muted">No node selected.</p>
      </section>
    );
  }

  return (
    <section className="gua-panel">
      <PanelHeader title="Node Detail" detail={node.id} />
      <div className="gua-command-row">
        <button type="button" onClick={onClick} disabled={!node.actions.includes("click")}>
          Click
        </button>
        <button type="button" onClick={onFocus} disabled={!node.actions.includes("focus")}>
          Focus
        </button>
      </div>
      <div className="gua-action-editor">
        {node.actions.includes("set_value") || node.actions.includes("select") ? (
          <>
            <input aria-label="Action value" value={value} onChange={(event) => setValue(event.currentTarget.value)} placeholder="value" />
            {node.actions.includes("set_value") ? (
              <button
                type="button"
                onClick={() => onAction({ action: "set_value", nodeId: node.id, value, sensitive, secretKey: sensitive ? secretKey : undefined })}
                disabled={sensitive && secretKey.length === 0}
              >Set value</button>
            ) : null}
            {node.actions.includes("select") ? <button type="button" onClick={() => onAction({ action: "select", nodeId: node.id, value })}>Select</button> : null}
            {node.actions.includes("set_value") ? (
              <label><input type="checkbox" checked={sensitive} onChange={(event) => setSensitive(event.currentTarget.checked)} /> Sensitive</label>
            ) : null}
            {sensitive ? <input aria-label="Secret key" value={secretKey} onChange={(event) => setSecretKey(event.currentTarget.value)} placeholder="secret key" /> : null}
          </>
        ) : null}
        {node.actions.includes("set_checked") ? (
          <div className="gua-command-row">
            <button type="button" onClick={() => onAction({ action: "set_checked", nodeId: node.id, checked: true })}>Check</button>
            <button type="button" onClick={() => onAction({ action: "set_checked", nodeId: node.id, checked: false })}>Uncheck</button>
          </div>
        ) : null}
        {node.actions.includes("scroll") ? (
          <div className="gua-command-row">
            <button type="button" onClick={() => onAction({ action: "scroll", nodeId: node.id, deltaX: 0, deltaY: -1, scrollUnit: 1 })}>Scroll up</button>
            <button type="button" onClick={() => onAction({ action: "scroll", nodeId: node.id, deltaX: 0, deltaY: 1, scrollUnit: 1 })}>Scroll down</button>
          </div>
        ) : null}
        {node.actions.includes("press_key") ? (
          <div className="gua-command-row">
            <input aria-label="Key name" value={key} onChange={(event) => setKey(event.currentTarget.value)} />
            <button type="button" onClick={() => onAction({ action: "press_key", nodeId: node.id, key })} disabled={key.length === 0}>Press key</button>
          </div>
        ) : null}
      </div>
      <table className="gua-detail">
        <tbody>
          <DetailRow name="id" value={node.id} />
          <DetailRow name="role" value={node.role} />
          <DetailRow name="label" value={node.label ?? ""} />
          <DetailRow name="parent" value={node.parentId ?? ""} />
          <DetailRow name="visible" value={String(node.visible)} />
          <DetailRow name="enabled" value={String(node.enabled)} />
          <DetailRow name="bounds" value={`${node.bounds.x}, ${node.bounds.y}, ${node.bounds.w}, ${node.bounds.h}`} />
          <DetailRow name="state" value={JSON.stringify(node.state ?? {})} />
          <DetailRow name="actions" value={node.actions.join(", ")} />
        </tbody>
      </table>
    </section>
  );
}

interface ScreenshotPanelProps {
  screenshot: {
    dataUri: string;
    width: number;
    height: number;
  };
  selectedNode: GuaNode | null;
}

function ScreenshotPanel({ screenshot, selectedNode }: ScreenshotPanelProps) {
  const hasImage = screenshot.dataUri.length > 0;
  const boxStyle = selectedNode === null || screenshot.width <= 0 || screenshot.height <= 0
    ? undefined
    : {
        left: `${(selectedNode.bounds.x / screenshot.width) * 100}%`,
        top: `${(selectedNode.bounds.y / screenshot.height) * 100}%`,
        width: `${(selectedNode.bounds.w / screenshot.width) * 100}%`,
        height: `${(selectedNode.bounds.h / screenshot.height) * 100}%`,
      };

  return (
    <section className="gua-panel gua-screenshot-panel">
      <PanelHeader
        title="Screenshot"
        detail={screenshot.width > 0 && screenshot.height > 0 ? `${screenshot.width} x ${screenshot.height}` : undefined}
      />
      <div className="gua-screenshot">
        {hasImage ? (
          <div className="gua-screenshot__stage">
            <img src={screenshot.dataUri} width={screenshot.width} height={screenshot.height} alt="Gua runtime screenshot" />
            {boxStyle !== undefined ? <span className="gua-bounds" style={boxStyle} /> : null}
          </div>
        ) : (
          <div className="gua-placeholder">
            <strong>No screenshot captured</strong>
            <span>Runtime adapters can provide a data URI for this panel.</span>
          </div>
        )}
      </div>
    </section>
  );
}

function LogPanel({ logs }: { logs: Array<{ sequence: number; level: string; message: string }> }) {
  return (
    <section className="gua-panel gua-log-panel">
      <PanelHeader title="Logs" detail={`${logs.length} entries`} />
      <div className="gua-log-list">
        {logs.length === 0 ? (
          <p className="gua-muted">No logs.</p>
        ) : (
          logs.map((entry) => (
            <div className="gua-log" key={entry.sequence}>
              <span className="gua-muted">{entry.sequence}</span>
              <span className={`gua-log__level gua-log__level--${entry.level}`}>{entry.level}</span>
              <span>{entry.message}</span>
            </div>
          ))
        )}
      </div>
    </section>
  );
}

interface AutomationPanelProps {
  recording: boolean;
  lastRecording: GuaRecording | null;
  screenshotDataUri: string;
  baselineDataUri: string | null;
  visualResult: BrowserVisualResult | null;
  secretsJson: string;
  onSecretsJson(value: string): void;
  onStart(): void;
  onStop(): void;
  onReplay(): void;
  onDownloadRecording(): void;
  onImportRecording(text: string): void;
  onUseCurrentBaseline(): void;
  onBaseline(dataUri: string): void;
  onCompare(): void;
}

function AutomationPanel(props: AutomationPanelProps) {
  return (
    <section className="gua-panel gua-automation-panel">
      <PanelHeader title="Automation" detail={props.recording ? "recording" : `${props.lastRecording?.steps.length ?? 0} steps`} />
      <div className="gua-automation-grid">
        <div>
          <h3>Recording / Replay</h3>
          <div className="gua-command-row">
            <button type="button" onClick={props.onStart} disabled={props.recording}>Start recording</button>
            <button type="button" onClick={props.onStop} disabled={!props.recording}>Stop</button>
            <button type="button" onClick={props.onReplay} disabled={props.lastRecording === null || props.recording}>Replay</button>
            <button type="button" onClick={props.onDownloadRecording} disabled={props.lastRecording === null}>Download JSON</button>
          </div>
          <label className="gua-file-field">
            <span>Import recording JSON</span>
            <input
              type="file"
              accept="application/json,.json"
              onChange={(event) => {
                const file = event.currentTarget.files?.[0];
                if (file !== undefined) void file.text().then(props.onImportRecording);
              }}
            />
          </label>
          <label className="gua-file-field">
            <span>Replay secrets (kept in memory only)</span>
            <textarea value={props.secretsJson} onChange={(event) => props.onSecretsJson(event.currentTarget.value)} rows={3} />
          </label>
        </div>
        <div>
          <h3>Visual comparison</h3>
          <div className="gua-command-row">
            <button type="button" onClick={props.onUseCurrentBaseline} disabled={props.screenshotDataUri.length === 0}>
              Use current as baseline
            </button>
            <button type="button" onClick={props.onCompare} disabled={props.baselineDataUri === null || props.screenshotDataUri.length === 0}>
              Compare
            </button>
          </div>
          <label className="gua-file-field">
            <span>Choose baseline image</span>
            <input
              type="file"
              accept="image/png,image/*"
              onChange={(event) => {
                const file = event.currentTarget.files?.[0];
                if (file !== undefined) void readFileAsDataUri(file).then(props.onBaseline);
              }}
            />
          </label>
          {props.visualResult !== null ? (
            <div className={props.visualResult.matched ? "gua-visual-result gua-visual-result--matched" : "gua-visual-result gua-visual-result--failed"}>
              <strong>{props.visualResult.matched ? "Matched" : `Failed: ${props.visualResult.reason}`}</strong>
              <span>{props.visualResult.differentPixels} / {props.visualResult.comparedPixels} pixels ({(props.visualResult.differentPixelRatio * 100).toFixed(4)}%)</span>
              <div className="gua-command-row">
                <button type="button" onClick={() => downloadDataUri("actual.png", props.visualResult?.actualDataUri as string)}>Actual</button>
                <button type="button" onClick={() => downloadDataUri("expected.png", props.visualResult?.expectedDataUri as string)}>Expected</button>
                <button type="button" onClick={() => downloadDataUri("diff.png", props.visualResult?.diffDataUri as string)}>Diff</button>
                <button type="button" onClick={() => downloadText("comparison.json", props.visualResult?.comparisonJson as string, "application/json")}>Manifest</button>
              </div>
            </div>
          ) : null}
        </div>
      </div>
    </section>
  );
}

function readFileAsDataUri(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => typeof reader.result === "string" ? resolve(reader.result) : reject(new Error("Image could not be read."));
    reader.onerror = () => reject(reader.error ?? new Error("Image could not be read."));
    reader.readAsDataURL(file);
  });
}

function downloadText(name: string, text: string, type: string): void {
  const url = URL.createObjectURL(new Blob([text], { type }));
  clickDownload(name, url);
  URL.revokeObjectURL(url);
}

function downloadDataUri(name: string, dataUri: string): void { clickDownload(name, dataUri); }

function clickDownload(name: string, href: string): void {
  const anchor = document.createElement("a");
  anchor.download = name;
  anchor.href = href;
  anchor.click();
}

function PanelHeader({ title, detail }: { title: string; detail?: string }) {
  return (
    <div className="gua-panel__header">
      <h2>{title}</h2>
      {detail !== undefined ? <span>{detail}</span> : null}
    </div>
  );
}

function DetailRow({ name, value }: { name: string; value: string }) {
  return (
    <tr>
      <th>{name}</th>
      <td>{value}</td>
    </tr>
  );
}

function NodeFlags({ node }: { node: GuaNode }) {
  return (
    <span className="gua-flags">
      {!node.visible ? <span>hidden</span> : null}
      {!node.enabled ? <span>disabled</span> : null}
      {node.state?.focused === true ? <span>focused</span> : null}
    </span>
  );
}
