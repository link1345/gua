import { createGodotWebBridge, createUnityWebGlBridge, registerGuaWebMcp } from "gua-webmcp";

const status = document.querySelector<HTMLElement>("#gua-webmcp-status");
void install();

async function install(): Promise<void> {
  const engine = await waitForEngine();
  const bridge = engine === "godot" ? createGodotWebBridge() : createUnityWebGlBridge();
  const registration = await registerGuaWebMcp(bridge);
  if (status) status.textContent = registration.supported
    ? `Gua ${engine} tools registered: ${registration.registeredTools.join(", ")}`
    : registration.error?.message ?? "WebMCP is unavailable.";
}

async function waitForEngine(): Promise<"godot" | "unity"> {
  for (;;) {
    if ("__guaGodotWebPort" in globalThis) return "godot";
    if ("__guaUnityWebPort" in globalThis) return "unity";
    await new Promise((resolve) => setTimeout(resolve, 25));
  }
}
