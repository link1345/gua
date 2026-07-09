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

	var checkbox := CheckBox.new()
	checkbox.name = "remember"
	checkbox.text = "Remember me"
	checkbox.button_pressed = false
	screen.add_child(checkbox)

	var line_edit := LineEdit.new()
	line_edit.name = "name"
	line_edit.text = "Gua"
	screen.add_child(line_edit)

	var option := OptionButton.new()
	option.name = "difficulty"
	option.add_item("Easy")
	option.add_item("Hard")
	option.select(1)
	screen.add_child(option)

	var item_list := ItemList.new()
	item_list.name = "servers"
	item_list.add_item("Tokyo")
	item_list.add_item("Osaka")
	item_list.select(0)
	screen.add_child(item_list)

	var tabs := TabContainer.new()
	tabs.name = "tabs"
	var general_tab := Control.new()
	general_tab.name = "General"
	tabs.add_child(general_tab)
	var audio_tab := Control.new()
	audio_tab.name = "Audio"
	tabs.add_child(audio_tab)
	tabs.current_tab = 1
	screen.add_child(tabs)

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
	var tree = JSON.parse_string(tree_json)
	if tree.get("schemaVersion", 0) != 2 or tree.get("frameSequence", 0) != 1:
		_fail("Gua smoke did not publish v2 snapshot metadata: %s" % tree_json)
		return
	if not tree_json.contains("\"checked\":false"):
		_fail("Gua smoke collapsed an observed false checkbox state into unknown: %s" % tree_json)
		return
	if not tree_json.contains("\"role\":\"combobox\"") or not tree_json.contains("\"value\":\"Hard\""):
		_fail("Gua smoke did not publish OptionButton value state: %s" % tree_json)
		return
	if not tree_json.contains("servers$item:0") or not tree_json.contains("tabs$tab:1"):
		_fail("Gua smoke did not publish stable ItemList/TabContainer semantic children: %s" % tree_json)
		return

	var first_revision = tree.get("revision", 0)
	ui.update("title")
	var stable_tree = JSON.parse_string(ui.get_ui_tree_json())
	if stable_tree.get("frameSequence", 0) != 2 or stable_tree.get("revision", 0) != first_revision:
		_fail("Gua smoke changed revision for an unchanged semantic frame: %s" % ui.get_ui_tree_json())
		return

	checkbox.visible = false
	ui.update("title")
	var hidden_tree = JSON.parse_string(ui.get_ui_tree_json())
	var hidden_checkbox = _find_node(hidden_tree, "remember")
	if hidden_checkbox == null or hidden_checkbox.get("visible", true):
		_fail("Gua smoke removed a hidden in-tree control instead of publishing visible=false.")
		return

	screen.remove_child(checkbox)
	checkbox.queue_free()
	ui.update("title")
	if _find_node(JSON.parse_string(ui.get_ui_tree_json()), "remember") != null:
		_fail("Gua smoke retained a detached control in the semantic snapshot.")
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


func _find_node(tree: Dictionary, id: String) -> Variant:
	for node in tree.get("nodes", []):
		if node.get("id", "") == id:
			return node
	return null


func _fail(message: String) -> void:
	push_error(message)
	quit(1)
