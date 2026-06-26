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
