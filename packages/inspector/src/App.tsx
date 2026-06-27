import { useCallback, useEffect, useMemo, useState } from "react";

import {
  type GuaInspectorClient,
  type GuaNode,
  type InspectorState,
  MockInspectorClient,
  createInspectorState,
  getSelectedNode,
  readSnapshot,
  selectNode,
  updateInspectorState,
} from "./core";

export interface GuaInspectorAppProps {
  client?: GuaInspectorClient;
}

export function GuaInspectorApp({ client }: GuaInspectorAppProps) {
  const inspectorClient = useMemo(() => client ?? new MockInspectorClient(), [client]);
  const [state, setState] = useState<InspectorState>(() => createInspectorState());
  const [status, setStatus] = useState<"idle" | "refreshing" | "error">("idle");
  const [error, setError] = useState<string | null>(null);

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

  const selectedNode = getSelectedNode(state);

  const clickSelected = async () => {
    if (selectedNode === null || !selectedNode.actions.includes("click")) {
      return;
    }

    await inspectorClient.clickNode(selectedNode.id);
    await refresh();
  };

  const focusSelected = async () => {
    if (selectedNode === null || !selectedNode.actions.includes("focus")) {
      return;
    }

    await inspectorClient.focusNode(selectedNode.id);
    await refresh();
  };

  return (
    <div className="gua-app">
      <header className="gua-topbar">
        <div>
          <div className="gua-brand">Gua Inspector</div>
          <div className="gua-screen">{state.uiTree.screen}</div>
        </div>
        <div className="gua-topbar__actions">
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
        <NodeDetailPanel node={selectedNode} onClick={() => void clickSelected()} onFocus={() => void focusSelected()} />
        <ScreenshotPanel screenshot={state.screenshot} selectedNode={selectedNode} />
        <LogPanel logs={state.logs} />
      </main>
    </div>
  );
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
}

function NodeDetailPanel({ node, onClick, onFocus }: NodeDetailPanelProps) {
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
