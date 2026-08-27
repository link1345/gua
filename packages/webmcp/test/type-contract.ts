import type { GuaGameInputAction, GuaNode, GuaUiTree, GuaWebActionRequest } from "../src/index.js";

const tree: GuaUiTree = {
  schemaVersion: 2,
  frameSequence: 1,
  revision: 1,
  screen: "title",
  nodes: [],
};

// @ts-expect-error protocol trees require schemaVersion, frameSequence, and revision.
const missingTreeMetadata: GuaUiTree = { screen: "title", nodes: [] };

const node: GuaNode = {
  id: "name",
  role: "textbox",
  text: "Player name",
  value: "Gua",
  visible: true,
  enabled: true,
  bounds: { x: 0, y: 0, w: 100, h: 20 },
  actions: ["set_value"],
  state: {
    caretPosition: 1,
    selectionStart: 0,
    selectionEnd: 1,
    scrollX: 2,
    scrollY: 3,
    scrollMaxX: 20,
    scrollMaxY: 30,
    rangeValue: 5,
    rangeMin: 0,
    rangeMax: 10,
    selectedIndex: -1,
  },
};

const validActions: GuaWebActionRequest[] = [
  { action: "click", nodeId: "start" },
  { action: "set_value", nodeId: "name", value: "Codex" },
  { action: "set_value", nodeId: "name", value: "" },
  { action: "set_checked", nodeId: "music", checked: false },
  { action: "select", nodeId: "server", value: "west" },
  { action: "scroll", nodeId: "list", deltaX: 0, deltaY: 1 },
  { action: "press_key", key: "Escape" },
];

const rangedGameInput: GuaGameInputAction = {
  id: "move", description: "Move", valueType: "axis1d", range: { minimum: -1, maximum: 1 },
  holdable: true, active: true, bindings: [], risk: "safe", requiresConfirmation: false,
};
// @ts-expect-error action ranges follow the protocol's nested range object.
const flatGameInputRange: GuaGameInputAction = { ...rangedGameInput, range: undefined, minimum: -1, maximum: 1 };

// @ts-expect-error set_value must include value.
const missingValue: GuaWebActionRequest = { action: "set_value", nodeId: "name" };
// @ts-expect-error set_checked must include checked.
const missingChecked: GuaWebActionRequest = { action: "set_checked", nodeId: "music" };
// @ts-expect-error scroll must include both deltas.
const missingDelta: GuaWebActionRequest = { action: "scroll", nodeId: "list", deltaX: 0 };
// @ts-expect-error press_key must include key.
const missingKey: GuaWebActionRequest = { action: "press_key" };

void [tree, missingTreeMetadata, node, validActions, rangedGameInput, flatGameInputRange, missingValue, missingChecked, missingDelta, missingKey];
