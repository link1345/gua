extends RefCounted

# Same-page Godot Web Export port consumed by gua-webmcp. It deliberately has
# no WebSocket endpoint and uses the adapter's request-correlated event queue.

var adapter_ref: WeakRef
var get_tree_callback: JavaScriptObject
var get_world_tree_callback: JavaScriptObject
var query_world_callback: JavaScriptObject
var enqueue_callback: JavaScriptObject
var poll_callback: JavaScriptObject
var cancel_callback: JavaScriptObject
var bridge_owner_id := ""


func attach(gua_adapter: RefCounted) -> bool:
	detach()
	adapter_ref = weakref(gua_adapter)
	if not OS.has_feature("web"):
		return false
	var window := JavaScriptBridge.get_interface("window")
	if window == null:
		return false
	get_tree_callback = JavaScriptBridge.create_callback(_get_tree)
	get_world_tree_callback = JavaScriptBridge.create_callback(_get_world_tree)
	query_world_callback = JavaScriptBridge.create_callback(_query_world)
	enqueue_callback = JavaScriptBridge.create_callback(_enqueue_action)
	poll_callback = JavaScriptBridge.create_callback(_poll_action)
	cancel_callback = JavaScriptBridge.create_callback(_cancel_action)
	window.__guaGodotGetTree = get_tree_callback
	window.__guaGodotGetWorldTree = get_world_tree_callback
	window.__guaGodotQueryWorld = query_world_callback
	window.__guaGodotEnqueueAction = enqueue_callback
	window.__guaGodotPollAction = poll_callback
	window.__guaGodotCancelAction = cancel_callback
	bridge_owner_id = str(get_instance_id())
	JavaScriptBridge.eval("""
(() => {
  const engineError = (code, message) => Object.assign(new Error(message), { code });
  const previousPort = globalThis.__guaGodotWebPort;
  if (previousPort && typeof previousPort.__guaUninstall === 'function') previousPort.__guaUninstall();
  const getTree = globalThis.__guaGodotGetTree;
  const getWorldTree = globalThis.__guaGodotGetWorldTree;
  const queryWorld = globalThis.__guaGodotQueryWorld;
  const enqueueAction = globalThis.__guaGodotEnqueueAction;
  const pollAction = globalThis.__guaGodotPollAction;
  const cancelAction = globalThis.__guaGodotCancelAction;
  const pending = new Map();
  let disposed = false;
  const port = {
    __guaOwnerId: "%s",
    __guaUninstall() {
      if (disposed) return;
      disposed = true;
      const error = engineError('engine_unsupported', 'The Godot Gua adapter is no longer available.');
      for (const call of pending.values()) call.cancelOrDrain(error);
    },
    async invoke(command, options) {
      if (disposed) throw engineError('engine_unsupported', 'The Godot Gua adapter is no longer available.');
      if (!command || typeof command.type !== 'string') throw engineError('invalid_request', 'Missing Gua in-page command.');
      const signal = options && options.signal;
      const requestedTimeoutMs = options && options.timeoutMs;
      if (signal && signal.aborted) throw engineError('aborted', 'The Godot Gua call was aborted.');
      if (requestedTimeoutMs !== undefined && (!Number.isInteger(requestedTimeoutMs) || requestedTimeoutMs < 0 || requestedTimeoutMs > 2147483647)) {
        throw engineError('invalid_request', 'Godot Web timeoutMs must be an integer from 0 to 2147483647.');
      }
      if (command.type === 'get_ui_tree') {
        const tree = JSON.parse(getTree());
        if (tree && tree.code) throw engineError(tree.code, tree.message || 'The Godot Gua adapter is unavailable.');
        return tree;
      }
      if (command.type === 'get_world_object_tree') {
        const tree = JSON.parse(getWorldTree());
        if (tree && tree.code) throw engineError(tree.code, tree.message || 'The Godot Gua world adapter is unavailable.');
        return tree;
      }
      if (command.type === 'query_world_objects') {
        const result = JSON.parse(queryWorld(JSON.stringify(command)));
        if (result && result.code) throw engineError(result.code, result.message || 'The Godot Gua world adapter is unavailable.');
        return result;
      }
      if (command.type !== 'perform_action') throw engineError('engine_unsupported', `Godot Web command is unsupported: ${command.type}`);
      const receipt = JSON.parse(enqueueAction(JSON.stringify(command.request)));
      if (!receipt.requestId) throw engineError(receipt.code || 'invalid_request', receipt.message || 'Godot rejected the Gua action.');
      return await new Promise((resolve, reject) => {
        const deadline = performance.now() + (requestedTimeoutMs === undefined ? 5000 : requestedTimeoutMs);
        const call = { reject, timer: 0, signal, aborted: null, settled: false, discardResult: false, drainDeadline: 0, cancelOrDrain: null };
        const finish = (settle) => {
          clearTimeout(call.timer);
          if (call.signal) call.signal.removeEventListener('abort', call.aborted);
          pending.delete(receipt.requestId);
          if (call.settled) return;
          call.settled = true;
          settle();
        };
        const rejectWithoutDropping = (error) => {
          if (call.settled) return;
          call.settled = true;
          if (call.signal) call.signal.removeEventListener('abort', call.aborted);
          reject(error);
        };
        const readResult = () => {
          const result = JSON.parse(pollAction(String(receipt.requestId)));
          if (result && result.code) throw engineError(result.code, result.message || 'The Godot Gua adapter is unavailable.');
          return result;
        };
        const schedulePoll = () => {
          clearTimeout(call.timer);
          call.timer = setTimeout(poll, call.discardResult ? 10 : 0);
        };
        const cancelOrDrain = (error) => {
          let cancellationResult;
          try { cancellationResult = Number(cancelAction(String(receipt.requestId))); }
          catch (_) { cancellationResult = -1; }
          if (cancellationResult === 1) return finish(() => reject(error));
          if (cancellationResult === 0) {
            try { readResult(); } catch (_) {}
            return finish(() => reject(error));
          }
          call.discardResult = true;
          call.drainDeadline = performance.now() + 5000;
          rejectWithoutDropping(error);
          schedulePoll();
        };
        call.cancelOrDrain = cancelOrDrain;
        const aborted = () => cancelOrDrain(engineError('aborted', 'The Godot Gua call was aborted.'));
        call.aborted = aborted;
        pending.set(receipt.requestId, call);
        if (signal) signal.addEventListener('abort', aborted, { once: true });
        if (signal && signal.aborted) return aborted();
        function poll() {
          if (disposed && !call.discardResult) return finish(() => reject(engineError('engine_unsupported', 'The Godot Gua adapter is no longer available.')));
          try {
            const result = readResult();
            if (result) return finish(() => resolve(result));
            if (call.discardResult) {
              if (performance.now() >= call.drainDeadline) return finish(() => {});
              return schedulePoll();
            }
            if (performance.now() >= deadline) {
              return cancelOrDrain(engineError('timeout', 'Timed out waiting for Godot host completion.'));
            }
            schedulePoll();
          } catch (error) {
            return finish(() => reject(error && error.code ? error : engineError('engine_unsupported', 'The Godot Gua adapter is no longer available.')));
          }
        }
        poll();
      });
    }
  };
  globalThis.__guaGodotWebPort = port;
})();
""" % bridge_owner_id)
	return true


func detach() -> void:
	if not bridge_owner_id.is_empty() and OS.has_feature("web"):
		JavaScriptBridge.eval("""
(() => {
  const port = globalThis.__guaGodotWebPort;
  if (!port || port.__guaOwnerId !== "%s") return;
  port.__guaUninstall();
  delete globalThis.__guaGodotWebPort;
  delete globalThis.__guaGodotGetTree;
  delete globalThis.__guaGodotGetWorldTree;
  delete globalThis.__guaGodotQueryWorld;
  delete globalThis.__guaGodotEnqueueAction;
  delete globalThis.__guaGodotPollAction;
  delete globalThis.__guaGodotCancelAction;
})();
""" % bridge_owner_id)
	bridge_owner_id = ""
	get_tree_callback = null
	get_world_tree_callback = null
	query_world_callback = null
	enqueue_callback = null
	poll_callback = null
	cancel_callback = null
	adapter_ref = null


func _notification(what: int) -> void:
	if what == NOTIFICATION_PREDELETE:
		detach()


func _get_tree(_arguments: Array) -> String:
	var adapter := _adapter()
	if adapter == null:
		return JSON.stringify({"code": "engine_unsupported", "message": "The Godot Gua adapter is no longer available."})
	return adapter.get_ui_tree_json()


func _get_world_tree(_arguments: Array) -> String:
	var adapter := _adapter()
	if adapter == null:
		return JSON.stringify({"code": "engine_unsupported", "message": "The Godot Gua adapter is no longer available."})
	return adapter.get_player_world_object_tree_json()


func _query_world(arguments: Array) -> String:
	var adapter := _adapter()
	if adapter == null:
		return JSON.stringify({"code": "engine_unsupported", "message": "The Godot Gua adapter is no longer available."})
	var source = JSON.parse_string(str(arguments[0])) if not arguments.is_empty() else null
	if not source is Dictionary:
		return JSON.stringify({"code": "invalid_request", "message": "World query must be an object."})
	var command: Dictionary = source
	return adapter.query_player_world_objects_json({
		"id": command.get("worldId", ""),
		"kind": command.get("kind", ""),
		"label": command.get("label", ""),
		"tag": command.get("tag", ""),
		"parent_id": command.get("parentId", ""),
		"direct_child": command.get("directChild", 0),
		"visible_to_player": command.get("visibleToPlayer", 0),
		"active": command.get("active", 0),
		"state_key": command.get("stateKey", ""),
		"state_type": command.get("stateType", 0),
		"state_string": command.get("stateString", ""),
		"state_number": command.get("stateNumber", 0.0),
		"state_bool": command.get("stateBool", false),
	})


func _enqueue_action(arguments: Array) -> String:
	var adapter := _adapter()
	if adapter == null:
		return JSON.stringify({"code": "engine_unsupported", "message": "The Godot Gua adapter is no longer available."})
	var source = JSON.parse_string(str(arguments[0])) if not arguments.is_empty() else null
	if not source is Dictionary:
		return JSON.stringify({"code": "invalid_request", "message": "Action request must be an object."})
	var request: Dictionary = source
	var native_request := {
		"action": request.get("action", ""),
		"node_id": request.get("nodeId", ""),
		"value": request.get("value", ""),
		"bool_value": request.get("checked", false),
		"delta_x": request.get("deltaX", 0.0),
		"delta_y": request.get("deltaY", 0.0),
		"scroll_unit": request.get("scrollUnit", 0),
		"key": request.get("key", ""),
		"modifiers": request.get("modifiers", 0),
		"sensitive": request.get("sensitive", false),
	}
	var receipt: Dictionary = adapter.enqueue_action(native_request)
	if receipt.get("error_code", -1) != 0:
		var error_code := int(receipt.get("error_code", -1))
		return JSON.stringify({"code": _web_error_code(error_code), "message": "Godot rejected the Gua action.", "hostError": error_code})
	return JSON.stringify({"requestId": receipt.get("request_id", 0)})


func _poll_action(arguments: Array) -> String:
	var adapter := _adapter()
	if adapter == null:
		return JSON.stringify({"code": "engine_unsupported", "message": "The Godot Gua adapter is no longer available."})
	var request_id := int(str(arguments[0])) if not arguments.is_empty() else 0
	var result: Dictionary = adapter.poll_action_result(request_id)
	return "null" if result.is_empty() else JSON.stringify(result)


func _cancel_action(arguments: Array) -> int:
	var adapter := _adapter()
	if adapter == null:
		return 0
	var request_id := int(str(arguments[0])) if not arguments.is_empty() else 0
	return adapter.cancel_action_request(request_id)


func _adapter() -> RefCounted:
	return adapter_ref.get_ref() if adapter_ref != null else null


func _web_error_code(error_code: int) -> String:
	match error_code:
		-2:
			return "node_not_found"
		-3:
			return "hidden"
		-4:
			return "disabled"
		-5:
			return "unsupported_action"
		_:
			return "invalid_request"
