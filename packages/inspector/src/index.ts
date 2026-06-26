export interface InspectorPanel {
  id: string;
  title: string;
}

export const initialPanels: InspectorPanel[] = [
  { id: "tree", title: "UI Tree" },
  { id: "node", title: "Node Detail" },
  { id: "logs", title: "Logs" },
];
