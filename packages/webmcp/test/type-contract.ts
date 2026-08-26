import type { GuaNode, GuaWebActionRequest } from "../src/index.js";

const node: GuaNode = {
  id: "name",
  role: "textbox",
  text: "Player name",
  value: "Gua",
  visible: true,
  enabled: true,
  bounds: { x: 0, y: 0, w: 100, h: 20 },
  actions: ["set_value"],
};

const validActions: GuaWebActionRequest[] = [
  { action: "click", nodeId: "start" },
  { action: "set_value", nodeId: "name", value: "Codex" },
  { action: "set_checked", nodeId: "music", checked: false },
  { action: "select", nodeId: "server", value: "west" },
  { action: "scroll", nodeId: "list", deltaX: 0, deltaY: 1 },
  { action: "press_key", key: "Escape" },
];

// @ts-expect-error set_value must include value.
const missingValue: GuaWebActionRequest = { action: "set_value", nodeId: "name" };
// @ts-expect-error set_checked must include checked.
const missingChecked: GuaWebActionRequest = { action: "set_checked", nodeId: "music" };
// @ts-expect-error scroll must include both deltas.
const missingDelta: GuaWebActionRequest = { action: "scroll", nodeId: "list", deltaX: 0 };
// @ts-expect-error press_key must include key.
const missingKey: GuaWebActionRequest = { action: "press_key" };

void [node, validActions, missingValue, missingChecked, missingDelta, missingKey];
