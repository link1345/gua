export interface GuaToolDefinition<Name extends string = string> {
  name: Name;
  description: string;
  inputSchema: Record<string, unknown>;
}

export const guaWebMcpToolNames = [
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
] as const;

export type GuaWebMcpToolName = (typeof guaWebMcpToolNames)[number];

export const guaWebMcpToolDefinitions: readonly GuaToolDefinition<GuaWebMcpToolName>[] = [
  {
    name: "get_ui_tree",
    description: "Read the current Gua semantic UI tree from the running game bridge.",
    inputSchema: objectSchema({}),
  },
  {
    name: "click_node",
    description: "Click a visible semantic UI node and wait for request-correlated host completion when supported.",
    inputSchema: objectSchema({ nodeId: stringProperty("The target Gua node id.") }, ["nodeId"]),
  },
  {
    name: "focus_node",
    description: "Focus a semantic UI node and wait for request-correlated host completion.",
    inputSchema: objectSchema({ nodeId: stringProperty("The target Gua node id.") }, ["nodeId"]),
  },
  {
    name: "set_value",
    description: "Set a semantic UI node value. Sensitive values are never returned by the adapter.",
    inputSchema: objectSchema({
      nodeId: stringProperty("The target Gua node id."),
      value: { type: "string", description: "The value to send to the host." },
      sensitive: { type: "boolean", description: "Redact the value from results and diagnostics." },
    }, ["nodeId", "value"]),
  },
  {
    name: "set_checked",
    description: "Set the checked state of a semantic UI node and wait for host completion.",
    inputSchema: objectSchema({
      nodeId: stringProperty("The target Gua node id."),
      checked: { type: "boolean" },
    }, ["nodeId", "checked"]),
  },
  {
    name: "select",
    description: "Select a value on a semantic UI node and wait for host completion.",
    inputSchema: objectSchema({
      nodeId: stringProperty("The target Gua node id."),
      value: stringProperty("The option value to select."),
    }, ["nodeId", "value"]),
  },
  {
    name: "scroll",
    description: "Scroll a semantic UI node using host pixels or semantic lines and wait for host completion.",
    inputSchema: objectSchema({
      nodeId: stringProperty("The target Gua node id."),
      deltaX: numberProperty("Horizontal scroll delta."),
      deltaY: numberProperty("Vertical scroll delta."),
      scrollUnit: { type: "integer", enum: [0, 1], description: "0 = pixels, 1 = semantic lines." },
    }, ["nodeId", "deltaX", "deltaY"]),
  },
  {
    name: "press_key",
    description: "Send a key press to a node or the host's current focus and wait for host completion.",
    inputSchema: objectSchema({
      key: stringProperty("The logical key name to press, such as Enter or Escape."),
      nodeId: stringProperty("Optional target node id; omit to use current focus."),
      modifiers: { type: "integer", minimum: 0, maximum: 15, description: "Shift=1, Alt=2, Control=4, Meta=8." },
    }, ["key"]),
  },
  {
    name: "wait_for_node",
    description: "Poll the live semantic UI tree until a node id appears or the timeout expires.",
    inputSchema: objectSchema({
      nodeId: stringProperty("The target Gua node id."),
      timeoutMs: { type: "integer", minimum: 0, description: "Maximum wait time in milliseconds. Defaults to 5000." },
    }, ["nodeId"]),
  },
  {
    name: "get_screenshot",
    description: "Read the latest screenshot published by the running game bridge.",
    inputSchema: objectSchema({}),
  },
];

function objectSchema(properties: Record<string, unknown>, required: string[] = []): Record<string, unknown> {
  return { type: "object", additionalProperties: false, properties, ...(required.length === 0 ? {} : { required }) };
}

function stringProperty(description: string): Record<string, unknown> {
  return { type: "string", description };
}

function numberProperty(description: string): Record<string, unknown> {
  return { type: "number", description };
}
