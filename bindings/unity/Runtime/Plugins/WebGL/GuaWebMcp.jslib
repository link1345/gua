mergeInto(LibraryManager.library, {
  GuaUnityWebInstall: function (hostNamePointer, ownerIdPointer, timeoutMs) {
    const hostName = UTF8ToString(hostNamePointer);
    const ownerId = UTF8ToString(ownerIdPointer);
    const previous = globalThis.__guaUnityWebState;
    if (previous && typeof previous.uninstall === 'function') {
      previous.uninstall('engine_unsupported', 'The Unity WebGL Gua runtime was replaced.');
    }

    const pending = new Map();
    const callTimeoutMs = Number.isFinite(timeoutMs) && timeoutMs >= 0 ? timeoutMs : 5000;
    const state = { ownerId, pending, disposed: false, uninstall: null };

    const cancelHostCall = function (callId) {
      try { SendMessage(hostName, 'HandleWebCancellation', String(callId)); } catch (_error) { }
    };

    const rejectPending = function (code, message) {
      for (const [callId, entry] of pending.entries()) {
        clearTimeout(entry.timer);
        cancelHostCall(callId);
        entry.reject({ code, message });
      }
      pending.clear();
    };

    state.uninstall = function (code, message) {
      if (state.disposed) return;
      state.disposed = true;
      rejectPending(code, message);
      if (globalThis.__guaUnityWebState !== state) return;
      delete globalThis.__guaUnityWebState;
      delete globalThis.__guaUnityWebPort;
      delete globalThis.__guaUnityWebResolveInternal;
    };

    globalThis.__guaUnityWebState = state;
    globalThis.__guaUnityWebResolveInternal = function (resolvedOwnerId, callId, payload, failed) {
      if (resolvedOwnerId !== ownerId || state.disposed) return;
      const entry = pending.get(callId);
      if (!entry) return;
      pending.delete(callId);
      clearTimeout(entry.timer);
      try {
        const value = JSON.parse(payload);
        if (failed) entry.reject(value); else entry.resolve(value);
      } catch (_error) {
        entry.reject({ code: 'invalid_request', message: 'Unity WebGL returned malformed Gua JSON.' });
      }
    };
    globalThis.__guaUnityWebPort = {
      __guaOwnerId: ownerId,
      invoke(command) {
        if (state.disposed) return Promise.reject({ code: 'engine_unsupported', message: 'The Unity WebGL Gua runtime is unavailable.' });
        if (command.type === 'get_screenshot') return Promise.reject({ code: 'engine_unsupported', message: 'Unity WebGL screenshot readback is not enabled.' });
        const callId = globalThis.__guaUnityWebNextCallId || 1;
        globalThis.__guaUnityWebNextCallId = callId + 1;
        return new Promise((resolve, reject) => {
          const timer = setTimeout(() => {
            if (!pending.delete(callId)) return;
            cancelHostCall(callId);
            reject({ code: 'timeout', message: 'Timed out waiting for Unity WebGL host completion.' });
          }, callTimeoutMs);
          pending.set(callId, { resolve, reject, timer });
          try {
            SendMessage(hostName, 'HandleWebRequest', JSON.stringify({ callId, command }));
          } catch (_error) {
            clearTimeout(timer);
            pending.delete(callId);
            reject({ code: 'engine_unsupported', message: `Unity WebGL could not reach the Gua runtime '${hostName}'.` });
          }
        });
      }
    };
  },
  GuaUnityWebUninstall: function (ownerIdPointer) {
    const ownerId = UTF8ToString(ownerIdPointer);
    const state = globalThis.__guaUnityWebState;
    if (state && state.ownerId === ownerId && typeof state.uninstall === 'function') {
      state.uninstall('engine_unsupported', 'The Unity WebGL Gua runtime was destroyed.');
    }
  },
  GuaUnityWebResolve: function (ownerIdPointer, callId, jsonPointer, failed) {
    const resolve = globalThis.__guaUnityWebResolveInternal;
    if (typeof resolve === 'function') resolve(UTF8ToString(ownerIdPointer), callId, UTF8ToString(jsonPointer), failed);
  }
});
