extends RefCounted

# Same-page Godot Web Export port consumed by gua-webmcp. It deliberately has
# no WebSocket endpoint and uses the adapter's request-correlated event queue.

var adapter: RefCounted
var get_tree_callback: JavaScriptObject
var enqueue_callback: JavaScriptObject
var poll_callback: JavaScriptObject


func attach(gua_adapter: RefCounted) -> bool:
	adapter = gua_adapter
	if not OS.has_feature("web"):
		return false
	var window := JavaScriptBridge.get_interface("window")
	if window == null:
		return false
	get_tree_callback = JavaScriptBridge.create_callback(_get_tree)
	enqueue_callback = JavaScriptBridge.create_callback(_enqueue_action)
	poll_callback = JavaScriptBridge.create_callback(_poll_action)
	window.__guaGodotGetTree = get_tree_callback
	window.__guaGodotEnqueueAction = enqueue_callback
	window.__guaGodotPollAction = poll_callback
	JavaScriptBridge.eval("""
(() => {
  const engineError = (code, message) => Object.assign(new Error(message), { code });
  globalThis.__guaGodotWebPort = {
    async invoke(command) {
      if (!command || typeof command.type !== 'string') throw engineError('invalid_request', 'Missing Gua in-page command.');
      if (command.type === 'get_ui_tree') return JSON.parse(globalThis.__guaGodotGetTree());
      if (command.type !== 'perform_action') throw engineError('engine_unsupported', `Godot Web command is unsupported: ${command.type}`);
      const receipt = JSON.parse(globalThis.__guaGodotEnqueueAction(JSON.stringify(command.request)));
      if (!receipt.requestId) throw engineError(receipt.code || 'invalid_request', receipt.message || 'Godot rejected the Gua action.');
      return await new Promise((resolve, reject) => {
        const deadline = performance.now() + 5000;
        const poll = () => {
          const result = JSON.parse(globalThis.__guaGodotPollAction(String(receipt.requestId)));
          if (result) return resolve(result);
          if (performance.now() >= deadline) return reject(engineError('timeout', 'Timed out waiting for Godot host completion.'));
          setTimeout(poll, 0);
        };
        poll();
      });
    }
  };
})();
""")
	return true


func _get_tree(_arguments: Array) -> String:
	return adapter.get_ui_tree_json()


func _enqueue_action(arguments: Array) -> String:
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
		return JSON.stringify({"code": "invalid_request", "message": "Godot rejected the Gua action.", "hostError": receipt.get("error_code")})
	return JSON.stringify({"requestId": receipt.get("request_id", 0)})


func _poll_action(arguments: Array) -> String:
	var request_id := int(str(arguments[0])) if not arguments.is_empty() else 0
	var result: Dictionary = adapter.poll_action_result(request_id)
	return "null" if result.is_empty() else JSON.stringify(result)
