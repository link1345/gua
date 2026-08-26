import { afterEach, describe, expect, test } from "bun:test";

type UnityWebLibrary = {
  GuaUnityWebInstall(hostName: string, ownerId: string, timeoutMs: number): void;
  GuaUnityWebUninstall(ownerId: string): void;
  GuaUnityWebResolve(ownerId: string, callId: number, payload: string, failed: number): void;
};

type UnityWebPort = { invoke(command: unknown): Promise<unknown> };

const unityGlobals = globalThis as typeof globalThis & {
  __guaUnityWebPort?: UnityWebPort;
  __guaUnityWebState?: unknown;
  __guaUnityWebResolveInternal?: unknown;
  __guaUnityWebNextCallId?: number;
};

afterEach(() => {
  delete unityGlobals.__guaUnityWebPort;
  delete unityGlobals.__guaUnityWebState;
  delete unityGlobals.__guaUnityWebResolveInternal;
  delete unityGlobals.__guaUnityWebNextCallId;
});

async function loadUnityWebLibrary() {
  const library: Record<string, unknown> = {};
  const messages: Array<{ hostName: string; methodName: string; payload: string }> = [];
  const source = await Bun.file(new URL(
    "../../../bindings/unity/Runtime/Plugins/WebGL/GuaWebMcp.jslib",
    import.meta.url,
  )).text();
  const evaluate = new Function("LibraryManager", "mergeInto", "UTF8ToString", "SendMessage", source);
  evaluate(
    { library },
    (target: Record<string, unknown>, entries: Record<string, unknown>) => Object.assign(target, entries),
    (value: string) => value,
    (hostName: string, methodName: string, payload: string) => messages.push({ hostName, methodName, payload }),
  );
  return { library: library as unknown as UnityWebLibrary, messages };
}

describe("Unity WebGL same-page port", () => {
  test("routes calls to the installed runtime object and resolves by owner", async () => {
    const { library, messages } = await loadUnityWebLibrary();
    library.GuaUnityWebInstall("Custom Gua Host", "owner-1", 100);
    const result = unityGlobals.__guaUnityWebPort!.invoke({ type: "get_ui_tree" });

    expect(messages).toHaveLength(1);
    expect(messages[0]!.hostName).toBe("Custom Gua Host");
    expect(messages[0]!.methodName).toBe("HandleWebRequest");
    const envelope = JSON.parse(messages[0]!.payload) as { callId: number };
    library.GuaUnityWebResolve("wrong-owner", envelope.callId, JSON.stringify({ ignored: true }), 0);
    library.GuaUnityWebResolve("owner-1", envelope.callId, JSON.stringify({ screen: "title" }), 0);

    await expect(result).resolves.toEqual({ screen: "title" });
  });

  test("rejects pending calls when the runtime is replaced or destroyed", async () => {
    const { library } = await loadUnityWebLibrary();
    library.GuaUnityWebInstall("First Host", "owner-1", 100);
    const replaced = unityGlobals.__guaUnityWebPort!.invoke({ type: "get_ui_tree" }).catch((error) => error);

    library.GuaUnityWebInstall("Second Host", "owner-2", 100);
    await expect(replaced).resolves.toMatchObject({ code: "engine_unsupported" });

    const destroyed = unityGlobals.__guaUnityWebPort!.invoke({ type: "get_ui_tree" }).catch((error) => error);
    library.GuaUnityWebUninstall("owner-1");
    expect(unityGlobals.__guaUnityWebPort).toBeDefined();
    library.GuaUnityWebUninstall("owner-2");
    await expect(destroyed).resolves.toMatchObject({ code: "engine_unsupported" });
    expect(unityGlobals.__guaUnityWebPort).toBeUndefined();
  });

  test("times out and removes calls that Unity never resolves", async () => {
    const { library, messages } = await loadUnityWebLibrary();
    library.GuaUnityWebInstall("Missing Host", "owner-1", 0);
    await expect(unityGlobals.__guaUnityWebPort!.invoke({ type: "get_ui_tree" }))
      .rejects.toMatchObject({ code: "timeout" });
    expect(messages.at(-1)).toMatchObject({ hostName: "Missing Host", methodName: "HandleWebCancellation" });
  });
});
