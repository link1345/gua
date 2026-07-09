extends SceneTree

const GuaAutoAdapterScript := preload("res://addons/gua/gua_auto_adapter.gd")

var pressed_count := 0


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var screen := Control.new()
	screen.name = "screen"
	screen.size = Vector2(1280, 720)
	root.add_child(screen)

	var button := Button.new()
	button.name = "start"
	button.text = "Start Game"
	button.position = Vector2(512, 312)
	button.size = Vector2(256, 56)
	button.pressed.connect(_on_start_pressed)
	screen.add_child(button)

	await process_frame

	var ui := GuaAutoAdapterScript.new()
	var missing_methods := ui._missing_context_methods(RefCounted.new())
	if not missing_methods.has("consume_click_request"):
		_fail("Gua smoke did not detect missing consume_click_request on an incompatible context.")
		return

	ui.attach(screen)
	ui.update("title")

	var tree_json := ui.get_ui_tree_json()
	if not tree_json.contains("\"start\"") or not tree_json.contains("\"button\""):
		_fail("Gua smoke did not publish the start button in the UI tree: %s" % tree_json)
		return

	if not ui.enqueue_click("start"):
		_fail("Gua smoke failed to enqueue click request for start.")
		return

	ui.update("title")

	var click_seen := false
	for _attempt in range(8):
		var event := ui.poll_event()
		if event.is_empty():
			break
		if event.get("type", "") == "click" and event.get("node_id", "") == "start":
			click_seen = true
			break

	if not click_seen:
		_fail("Gua smoke did not observe click event for start.")
		return

	if pressed_count != 1:
		_fail("Gua smoke expected one Button.pressed signal, got %d." % pressed_count)
		return

	print("Gua GDScript smoke passed.")
	quit(0)


func _on_start_pressed() -> void:
	pressed_count += 1


func _fail(message: String) -> void:
	push_error(message)
	quit(1)
