mergeInto(LibraryManager.library, {
  GuaUnityWebInstall: function () {
    if (globalThis.__guaUnityWebPort) return;
    const pending = new Map();
    let nextCallId = 1;
    globalThis.__guaUnityWebResolveInternal = function (callId, payload, failed) {
      const entry = pending.get(callId);
      if (!entry) return;
      pending.delete(callId);
      const value = JSON.parse(payload);
      if (failed) entry.reject(value); else entry.resolve(value);
    };
    globalThis.__guaUnityWebPort = {
      invoke(command) {
        if (command.type === 'get_screenshot') return Promise.reject({ code: 'engine_unsupported', message: 'Unity WebGL screenshot readback is not enabled.' });
        const callId = nextCallId++;
        return new Promise((resolve, reject) => {
          pending.set(callId, { resolve, reject });
          SendMessage('Gua Runtime', 'HandleWebRequest', JSON.stringify({ callId, command }));
        });
      }
    };
  },
  GuaUnityWebResolve: function (callId, jsonPointer, failed) {
    globalThis.__guaUnityWebResolveInternal(callId, UTF8ToString(jsonPointer), failed);
  }
});
