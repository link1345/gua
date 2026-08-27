extends Node

const GuaAutoAdapterScript := preload("res://addons/gua/gua_auto_adapter.gd")

var pressed_count := 0
var adapter: Variant
var expected_click_request_id := 0
var click_completed_before_handler := false
var smoke_root: Control
var finishing := false


func _ready() -> void:
	call_deferred("_run")


func _run() -> void:
	var screen := Control.new()
	smoke_root = screen
	screen.name = "screen"
	screen.size = Vector2(1280, 720)
	get_tree().root.add_child(screen)

	var button := Button.new()
	button.name = "start"
	button.text = "Start Game"
	button.position = Vector2(512, 312)
	button.size = Vector2(256, 56)
	button.pressed.connect(_on_start_pressed)
	screen.add_child(button)
	var nonfocus_button := Button.new()
	nonfocus_button.name = "nonfocus"
	nonfocus_button.text = "No Focus"
	nonfocus_button.focus_mode = Control.FOCUS_NONE
	screen.add_child(nonfocus_button)

	var checkbox := CheckBox.new()
	checkbox.name = "remember"
	checkbox.text = "Remember me"
	checkbox.button_pressed = false
	screen.add_child(checkbox)
	var exclusive_group := ButtonGroup.new()
	var grouped_first := CheckBox.new()
	grouped_first.name = "grouped_first"
	grouped_first.button_group = exclusive_group
	grouped_first.button_pressed = true
	screen.add_child(grouped_first)
	var grouped_second := CheckBox.new()
	grouped_second.name = "grouped_second"
	grouped_second.button_group = exclusive_group
	screen.add_child(grouped_second)

	var line_edit := LineEdit.new()
	line_edit.name = "name"
	line_edit.text = "Gua"
	screen.add_child(line_edit)
	var key_events := []
	line_edit.gui_input.connect(func(event):
		if event is InputEventKey:
			key_events.append(event)
	)
	var text_edit := TextEdit.new()
	text_edit.name = "notes"
	text_edit.text = "Old"
	screen.add_child(text_edit)
	var slider := HSlider.new()
	slider.name = "volume"
	slider.value = 10
	screen.add_child(slider)
	var spin_box := SpinBox.new()
	spin_box.name = "limit"
	spin_box.value = 5
	spin_box.focus_mode = Control.FOCUS_ALL
	screen.add_child(spin_box)
	var locked_spin_box := SpinBox.new()
	locked_spin_box.name = "locked_count"
	locked_spin_box.editable = false
	screen.add_child(locked_spin_box)
	var nonfocus_spin_box := SpinBox.new()
	nonfocus_spin_box.name = "nonfocus_count"
	nonfocus_spin_box.focus_mode = Control.FOCUS_NONE
	screen.add_child(nonfocus_spin_box)

	var option := OptionButton.new()
	option.name = "difficulty"
	option.add_item("Easy")
	option.add_item("Hard")
	option.select(1)
	screen.add_child(option)

	var item_list := ItemList.new()
	item_list.name = "servers"
	item_list.size = Vector2(120, 40)
	item_list.icon_mode = ItemList.ICON_MODE_TOP
	item_list.max_columns = 0
	item_list.fixed_column_width = 300
	item_list.add_item("Tokyo")
	item_list.add_item("Osaka")
	for index in range(20):
		item_list.add_item("Server %d" % index)
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
	var scroll := ScrollContainer.new()
	scroll.name = "scroll"
	scroll.scroll_horizontal_custom_step = 7.0
	scroll.scroll_vertical_custom_step = 9.0
	var scroll_content := Control.new()
	scroll_content.custom_minimum_size = Vector2(1000, 1000)
	scroll.add_child(scroll_content)
	screen.add_child(scroll)

	await get_tree().process_frame
	var extension := load("res://addons/gua/gua.gdextension")
	var bare_context: Object = ClassDB.instantiate("GuaContext")
	if extension == null or bare_context == null or bare_context.get_version_json().contains("virtual_clock_v1") \
			or bare_context.get_version_json().contains("world_object_tree_v1"):
		_fail("A bare Godot GuaContext advertised a capability without its adapter pump.")
		return
	if bare_context.publish_game_input_actions("invalid", [{
		"id": "invalid_type", "description": "Invalid type", "value_type": "axis1D",
	}]):
		_fail("GuaContext accepted an unknown game input action value_type.")
		return
	bare_context = null

	var ui := GuaAutoAdapterScript.new()
	adapter = ui
	var missing_methods := ui._missing_context_methods(RefCounted.new())
	if not missing_methods.has("consume_click_request"):
		_fail("Gua smoke did not detect missing consume_click_request on an incompatible context.")
		return

	var door := Node2D.new()
	door.name = "Door"
	door.position = Vector2(640, 180)
	door.add_to_group(&"gua_world_object")
	door.set_meta(&"gua_world_id", "door-a")
	door.set_meta(&"gua_world_kind", "door")
	door.set_meta(&"gua_world_label", "Door A")
	door.set_meta(&"gua_world_visible_to_player", true)
	door.set_meta(&"gua_world_tags", ["east-corridor", "mission-critical"])
	door.set_meta(&"gua_world_state", {"open": false, "locked": true})
	screen.add_child(door)

	ui.attach(screen)
	if not ui.configure_game_input_actions("gameplay", [
		{"id": "jump", "description": "Jump", "value_type": "button", "holdable": true, "bindings": ["Space"]},
		{"id": "throttle", "description": "Throttle", "value_type": "axis1d", "minimum": -1.0, "maximum": 1.0, "holdable": true},
		{"id": "move", "description": "Move", "value_type": "vector2", "minimum": -1.0, "maximum": 1.0, "holdable": true},
		{"id": "chat", "description": "Chat", "value_type": "text"},
	]) or not ui.enable_raw_input():
		_fail("Gua smoke failed to initialize game input capabilities.")
		return
	ui.update("title")
	var game_input_actions: String = ui.context.get_game_input_actions_json()
	if not game_input_actions.contains("\"button\"") or not game_input_actions.contains("\"axis1d\"") or not game_input_actions.contains("\"vector2\"") or not game_input_actions.contains("\"text\""):
		_fail("Gua smoke did not publish every game input action type: %s" % game_input_actions)
		return
	var web_input_bridge = load("res://addons/gua/gua_webmcp_bridge.gd").new()
	var wheel_request: Dictionary = web_input_bridge._native_game_input_request({
		"type": "pointer_wheel", "deltaX": -4.0, "deltaY": 120.0, "wheelUnit": "pixels"
	})
	if wheel_request.get("x", 0.0) != -4.0 or wheel_request.get("y", 0.0) != 120.0:
		_fail("Gua WebMCP wheel mapping lost deltaX/deltaY: %s" % wheel_request)
		return
	web_input_bridge.adapter_ref = weakref(ui)
	var released_owner_id: int = ui.create_game_input_owner()
	web_input_bridge.game_input_owner_id = released_owner_id
	if web_input_bridge._release_game_input_owner(["1"]) != 1 \
			or web_input_bridge.game_input_owner_id == 0 \
			or web_input_bridge.game_input_owner_id == released_owner_id:
		_fail("Gua WebMCP did not replace a released page-local game input owner.")
		return
	web_input_bridge._release_game_input_owner([])
	web_input_bridge.adapter_ref = null
	for code in ["Backspace", "ContextMenu", "F1", "F24", "Numpad0", "Numpad9", "Quote", "ScrollLock"]:
		if ui._keycode_from_w3c(code) == KEY_NONE:
			_fail("Gua raw-keyboard capability did not implement protocol-valid code %s." % code)
			return
	if ui._apply_game_input({"kind": 1, "operation": 2, "owner_id": 101, "target": "move", "value_json": "{\"x\":1,\"y\":0}"}) != 0:
		_fail("Gua smoke could not inject the first owner-scoped semantic value.")
		return
	if ui._apply_game_input({"kind": 1, "operation": 2, "owner_id": 202, "target": "move", "value_json": "{\"x\":0,\"y\":1}"}) != 0:
		_fail("Gua smoke could not inject the second owner-scoped semantic value.")
		return
	ui._apply_game_input({"kind": 6, "operation": 10, "owner_id": 101})
	if ui.get_game_input_action_value("move") != Vector2(0, 1):
		_fail("Gua smoke released another owner's semantic value: %s" % ui.get_game_input_action_value("move"))
		return
	ui._apply_game_input({"kind": 4, "operation": 2, "owner_id": 202, "target": "left_stick_x", "value_json": "1", "device_index": 0})
	ui._apply_game_input({"kind": 4, "operation": 3, "owner_id": 202, "target": "left_stick_x", "device_index": 0})
	if not ui.held_gamepad_axes.is_empty():
		_fail("Gua smoke retained a released gamepad axis.")
		return
	ui._apply_game_input({"kind": 6, "operation": 10, "owner_id": 202})
	if not ui.context.get_version_json().contains("virtual_clock_v1"):
		_fail("GuaAutoAdapter did not enable its pumped virtual-clock capability.")
		return
	if not ui.context.get_version_json().contains("world_object_tree_v1"):
		_fail("GuaAutoAdapter did not enable its pumped World Object Tree capability.")
		return
	var world_tree = JSON.parse_string(ui.context.get_world_object_tree_json())
	var world_door = _find_world_object(world_tree, "door-a")
	if world_door == null or world_door.get("kind", "") != "door" or world_door.get("space", "") != "world2d" \
			or float(world_door.get("position", {}).get("x", -1)) != 640.0 or float(world_door.get("position", {}).get("y", -1)) != 180.0 \
			or not world_door.get("visibleToPlayer", false) or not world_door.get("state", {}).get("locked", false):
		_fail("Gua Godot adapter did not publish the shared Door fixture: %s" % world_tree)
		return
	var world_query = JSON.parse_string(ui.context.query_world_objects_json({"kind": "door", "state_key": "locked", "state_type": 3, "state_bool": true}))
	if not world_query.get("valid", false) or world_query.get("matches", []).size() != 1 \
			or world_query.get("matches", [])[0].get("id", "") != "door-a":
		_fail("Gua Godot world query did not return the shared Door fixture: %s" % world_query)
		return
	var world_status: Dictionary = ui.context.get_context_status()
	if world_status.get("world_frame_sequence", 0) != 1 or world_status.get("world_revision", 0) != 1 \
			or world_status.get("world_object_count", 0) != 1:
		_fail("Gua Godot status omitted World Object Tree metadata: %s" % world_status)
		return
	door.set_meta(&"gua_world_visible_to_player", "false")
	ui._publish_world_frame("title", false)
	var rejected_world_tree = JSON.parse_string(ui.context.get_world_object_tree_json())
	if _find_world_object(rejected_world_tree, "door-a") == null \
			or rejected_world_tree.get("frameSequence", 0) != 1:
		_fail("Gua accepted malformed world visibility metadata or replaced the prior frame: %s" % rejected_world_tree)
		return
	door.set_meta(&"gua_world_visible_to_player", true)
	ui._publish_world_frame("title")
	door.set_meta(&"gua_world_id", "")
	ui._publish_world_frame("title", false)
	var missing_id_world_tree = JSON.parse_string(ui.context.get_world_object_tree_json())
	if _find_world_object(missing_id_world_tree, "door-a") == null \
			or missing_id_world_tree.get("frameSequence", 0) != 2:
		_fail("Gua removed an opted-in object with malformed ID metadata: %s" % missing_id_world_tree)
		return
	door.set_meta(&"gua_world_id", "door-a")
	ui._publish_world_frame("title")
	door.set_meta(&"gua_world_state", {"code": 9007199254740993})
	ui._publish_world_frame("title", false)
	var imprecise_integer_world_tree = JSON.parse_string(ui.context.get_world_object_tree_json())
	if _find_world_object(imprecise_integer_world_tree, "door-a") == null \
			or imprecise_integer_world_tree.get("frameSequence", 0) != 3:
		_fail("Gua accepted a world state integer that loses precision in the C ABI: %s" % imprecise_integer_world_tree)
		return
	door.set_meta(&"gua_world_state", {"open": false, "locked": true})
	await get_tree().process_frame
	var smoke_image := Image.create(2, 2, false, Image.FORMAT_RGBA8)
	smoke_image.fill(Color(0.2, 0.4, 0.6, 1.0))
	var capture := ui.capture_viewport_screenshot(smoke_image)
	if not capture.get("ok", false) or not ui.context.get_screenshot_json().contains("data:image/png;base64,"):
		_fail("Gua smoke did not publish an opt-in Godot viewport PNG: %s" % capture)
		return

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
	if not tree_json.contains("\"selectedIndex\":1") or not tree_json.contains("\"rangeMin\":0.000000"):
		_fail("Gua smoke did not publish detailed selected/range state: %s" % tree_json)
		return
	if not tree_json.contains("\"scrollY\":0.000000") or not tree_json.contains("\"scrollMaxY\""):
		_fail("Gua smoke did not publish detailed scroll state: %s" % tree_json)
		return
	if not tree_json.contains("servers$item:0") or not tree_json.contains("tabs$tab:1"):
		_fail("Gua smoke did not publish stable ItemList/TabContainer semantic children: %s" % tree_json)
		return
	var locked_spin_node = _find_node(tree, "locked_count")
	if locked_spin_node == null or locked_spin_node.get("enabled", true):
		_fail("Gua smoke exposed a read-only SpinBox as enabled: %s" % tree_json)
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
	var action_click := ui.enqueue_action({"action": "click", "node_id": "start"})
	expected_click_request_id = action_click.get("request_id", 0)
	ui.update("title")
	var action_click_event := ui.poll_event_v2()
	if click_completed_before_handler:
		_fail("Gua completed a v2 click before the pressed handler returned.")
		return
	if pressed_count != 2 or action_click_event.get("request_id", 0) != expected_click_request_id or not action_click_event.get("succeeded", false):
		_fail("Gua smoke did not drain and apply a v2 click request: %s" % action_click_event)
		return
	var list_item_select := ui.enqueue_action({"action": "select", "node_id": "servers$item:1"})
	ui.update("title")
	var list_item_event := ui.poll_event_v2()
	if not item_list.is_selected(1) or list_item_event.get("request_id", 0) != list_item_select.get("request_id", 0) or not list_item_event.get("succeeded", false):
		_fail("Gua smoke did not route a synthetic list item selection: %s / %s" % [list_item_select, list_item_event])
		return
	var tab_item_select := ui.enqueue_action({"action": "select", "node_id": "tabs$tab:0"})
	ui.update("title")
	var tab_item_event := ui.poll_event_v2()
	if tabs.current_tab != 0 or tab_item_event.get("request_id", 0) != tab_item_select.get("request_id", 0) or not tab_item_event.get("succeeded", false):
		_fail("Gua smoke did not route a synthetic tab selection: %s" % tab_item_event)
		return
	var locked_spin_action := ui.enqueue_action({"action": "set_value", "node_id": "locked_count", "value": "3"})
	if locked_spin_action.get("error_code", 0) != -4:
		_fail("Gua smoke accepted set_value for a read-only SpinBox: %s" % locked_spin_action)
		return
	var invalid_focus := ui.enqueue_action({"action": "focus", "node_id": "nonfocus"})
	ui.update("title")
	var invalid_focus_event := ui.poll_event_v2()
	if invalid_focus_event.get("request_id", 0) != invalid_focus.get("request_id", 0) or invalid_focus_event.get("succeeded", true) or invalid_focus_event.get("error_code", 0) != -5:
		_fail("Gua smoke reported success for a non-focusable control: %s" % invalid_focus_event)
		return
	var invalid_spin_focus := ui.enqueue_action({"action": "focus", "node_id": "nonfocus_count"})
	ui.update("title")
	var invalid_spin_focus_event := ui.poll_event_v2()
	if invalid_spin_focus_event.get("request_id", 0) != invalid_spin_focus.get("request_id", 0) or invalid_spin_focus_event.get("succeeded", true) or invalid_spin_focus_event.get("error_code", 0) != -5:
		_fail("Gua focused a SpinBox whose parent disabled focus: %s" % invalid_spin_focus_event)
		return
	var action_checkbox := CheckBox.new()
	action_checkbox.name = "action_check"
	screen.add_child(action_checkbox)
	ui.update("title")
	# Keep the horizontal scrollbar range deterministic in headless mode, where
	# ItemList layout does not receive a rendered viewport pass.
	item_list.get_h_scroll_bar().max_value = 100.0
	item_list.get_h_scroll_bar().page = 0.0

	var action_cases := [
		[{"action": "focus", "node_id": "start"}, func(): return button.has_focus()],
		[{"action": "set_value", "node_id": "name", "value": "Codex"}, func(): return line_edit.text == "Codex"],
		[{"action": "set_value", "node_id": "notes", "value": "New"}, func(): return text_edit.text == "New"],
		[{"action": "set_value", "node_id": "volume", "value": "42"}, func(): return slider.value == 42],
		[{"action": "set_value", "node_id": "limit", "value": "12"}, func(): return spin_box.value == 12],
		[{"action": "set_checked", "node_id": "action_check", "bool_value": true}, func(): return action_checkbox.button_pressed],
		[{"action": "select", "node_id": "difficulty", "value": "Easy"}, func(): return option.selected == 0],
		[{"action": "select", "node_id": "servers", "value": "Osaka"}, func(): return item_list.is_selected(1)],
		[{"action": "select", "node_id": "tabs", "value": "General"}, func(): return tabs.current_tab == 0],
		[{"action": "scroll", "node_id": "scroll", "delta_x": 25.0, "delta_y": 30.0}, func(): return scroll.scroll_horizontal == 25 and scroll.scroll_vertical == 30],
		[{"action": "scroll", "node_id": "scroll", "delta_x": 1.0, "delta_y": 1.0, "scroll_unit": 1}, func(): return scroll.scroll_horizontal == 32 and scroll.scroll_vertical == 39],
		[{"action": "scroll", "node_id": "servers", "delta_x": 30.0}, func(): return item_list.get_h_scroll_bar().value > 0],
		[{"action": "press_key", "node_id": "name", "key": "A", "modifiers": 5}, func(): return key_events.size() == 2 and key_events[0].pressed and not key_events[1].pressed and key_events[0].shift_pressed and key_events[0].ctrl_pressed],
	]
	for action_case in action_cases:
		var accepted: Dictionary = ui.enqueue_action(action_case[0])
		if accepted.get("error_code", -1) != 0 or accepted.get("request_id", 0) == 0:
			_fail("Gua smoke rejected action %s: %s" % [action_case[0], accepted])
			return
		ui.update("title")
		if not action_case[1].call():
			_fail("Gua smoke did not apply host action: %s" % action_case[0])
			return
		var observed := ui.poll_event_v2()
		if observed.get("request_id", 0) != accepted.get("request_id", 0) or not observed.get("succeeded", false):
			_fail("Gua smoke did not correlate observed action event: %s / %s" % [accepted, observed])
			return
	var invalid_scroll := ui.enqueue_action({"action": "scroll", "node_id": "scroll", "delta_y": 1.0, "scroll_unit": 2})
	ui.update("title")
	var invalid_scroll_event := ui.poll_event_v2()
	if invalid_scroll_event.get("request_id", 0) != invalid_scroll.get("request_id", 0) \
			or invalid_scroll_event.get("succeeded", true) \
			or invalid_scroll_event.get("error_code", 0) != -6 \
			or scroll.scroll_vertical != 39:
		_fail("Gua accepted an unsupported semantic scroll unit: %s" % invalid_scroll_event)
		return
	var spin_focus := ui.enqueue_action({"action": "focus", "node_id": "limit"})
	ui.update("title")
	var spin_focus_event := ui.poll_event_v2()
	ui.update("title")
	var spin_node = _find_node(JSON.parse_string(ui.get_ui_tree_json()), "limit")
	if spin_focus_event.get("request_id", 0) != spin_focus.get("request_id", 0) or spin_node == null or not spin_node.get("state", {}).get("focused", false):
		_fail("Gua did not publish SpinBox focus from its inner editor: %s / %s" % [spin_focus_event, spin_node])
		return
	var disabled_click := ui.enqueue_action({"action": "click", "node_id": "start"})
	button.disabled = true
	ui.update("title")
	var disabled_click_event := ui.poll_event_v2()
	button.disabled = false
	if disabled_click_event.get("request_id", 0) != disabled_click.get("request_id", 0) or disabled_click_event.get("succeeded", true) or disabled_click_event.get("error_code", 0) != -4:
		_fail("Gua dropped an accepted click after the target became disabled: %s" % disabled_click_event)
		return
	var checkbox_click := ui.enqueue_action({"action": "click", "node_id": "action_check"})
	ui.update("title")
	var checkbox_click_event := ui.poll_event_v2()
	if checkbox_click.get("error_code", -1) != 0 or action_checkbox.button_pressed or checkbox_click_event.get("request_id", 0) != checkbox_click.get("request_id", 0):
		_fail("Gua click action did not toggle the checkbox: %s / %s" % [checkbox_click, checkbox_click_event])
		return
	var grouped_click := ui.enqueue_action({"action": "click", "node_id": "grouped_first"})
	ui.update("title")
	var grouped_click_event := ui.poll_event_v2()
	if not grouped_first.button_pressed or grouped_second.button_pressed or grouped_click_event.get("request_id", 0) != grouped_click.get("request_id", 0):
		_fail("Gua click action cleared an exclusive ButtonGroup selection: %s / %s" % [grouped_click, grouped_click_event])
		return
	var invalid_sensitive_range := ui.enqueue_action({"action": "set_value", "node_id": "volume", "value": "not-a-number", "sensitive": true})
	ui.update("title")
	var invalid_sensitive_range_event := ui.poll_event_v2()
	ui.update("title")
	var unchanged_range_node = _find_node(JSON.parse_string(ui.get_ui_tree_json()), "volume")
	if invalid_sensitive_range_event.get("request_id", 0) != invalid_sensitive_range.get("request_id", 0) \
			or invalid_sensitive_range_event.get("succeeded", true) \
			or invalid_sensitive_range_event.get("error_code", 0) != -6 \
			or slider.value != 42.0 \
			or unchanged_range_node == null \
			or not unchanged_range_node.has("value") \
			or float(unchanged_range_node.get("value", -1.0)) != 42.0 \
			or float(unchanged_range_node.get("state", {}).get("rangeValue", -1.0)) != 42.0:
		_fail("Gua marked a range sensitive after rejecting its value: %s / %s" % [invalid_sensitive_range_event, unchanged_range_node])
		return
	var sensitive := ui.enqueue_action({"action": "set_value", "node_id": "name", "value": "secret-marker", "sensitive": true})
	ui.update("title")
	var sensitive_event := ui.poll_event_v2()
	ui.update("title")
	if sensitive_event.get("request_id", 0) != sensitive.get("request_id", 0) or not sensitive_event.get("value", "").is_empty():
		_fail("Gua smoke leaked a sensitive value in its observed event: %s" % sensitive_event)
		return
	if ui.get_ui_tree_json().contains("secret-marker"):
		_fail("Gua smoke leaked a sensitive value in the semantic UI tree.")
		return
	var sensitive_range := ui.enqueue_action({"action": "set_value", "node_id": "volume", "value": "37", "sensitive": true})
	ui.update("title")
	var sensitive_range_event := ui.poll_event_v2()
	ui.update("title")
	var sensitive_range_node = _find_node(JSON.parse_string(ui.get_ui_tree_json()), "volume")
	if sensitive_range_event.get("request_id", 0) != sensitive_range.get("request_id", 0) \
			or sensitive_range_node == null or sensitive_range_node.get("state", {}).has("rangeValue"):
		_fail("Gua smoke leaked a sensitive range value: %s / %s" % [sensitive_range_event, sensitive_range_node])
		return

	var preinstall_schedule_count := [0]
	ui.clock_schedule(20.0, func(): preinstall_schedule_count[0] += 1)
	ui.last_clock_ticks_ms = 0
	var install_started_ticks := Time.get_ticks_msec()
	var installed_clock := ui.clock_install(0.0, 10.0)
	if installed_clock.get("result", 0) != 1 or ui.last_clock_ticks_ms < install_started_ticks:
		_fail("Gua Godot clock retained elapsed time from before installation: %s" % [installed_clock])
		return
	ui.clock_pause()
	var rejected_zero_step := ui.clock_run_for(10.0, 0.0)
	if rejected_zero_step.get("result", 0) != -1 or rejected_zero_step.get("error", "") != "invalid_duration" or float(ui.get_clock().get("now_ms", -1.0)) != 0.0:
		_fail("Gua Godot clock accepted an explicitly supplied zero step: %s" % rejected_zero_step)
		return
	var clock_order: Array[String] = []
	ui.clock_tick.connect(func(_delta: float): clock_order.append("tick:%d" % int(ui.get_clock().get("now_ms", -1.0))))
	ui.clock_schedule(20.0, func(): clock_order.append("schedule:%d" % int(ui.get_clock().get("now_ms", -1.0))))
	ui.clock_run_for(25.0)
	ui.update("title")
	if clock_order != ["tick:10", "schedule:20", "tick:20", "tick:25"]:
		_fail("Gua clock did not process schedules and ticks at each step boundary: %s" % [clock_order])
		return
	if preinstall_schedule_count[0] != 1:
		_fail("Gua dropped a game schedule that was registered before clock installation.")
		return
	var nested_schedule_order: Array[String] = []
	var later_schedule_id := ui.clock_schedule(20.0, func(): nested_schedule_order.append("later"))
	ui.clock_schedule(10.0, func():
		nested_schedule_order.append("parent")
		ui.clock_schedule(0.0, func(): nested_schedule_order.append("nested"))
	)
	ui.clock_run_for(10.0)
	ui.update("title")
	ui.clock_cancel(later_schedule_id)
	if nested_schedule_order != ["parent", "nested"]:
		_fail("Gua clock deferred a due schedule created by a running callback: %s" % [nested_schedule_order])
		return

	var interval_count := [0]
	var interval_id := [0]
	interval_id[0] = ui.clock_schedule(10.0, func():
		interval_count[0] += 1
		ui.clock_cancel(interval_id[0])
	, 10.0)
	ui.clock_run_for(30.0)
	ui.update("title")
	if interval_count[0] != 1:
		_fail("Gua clock rescheduled a running interval after it cancelled itself: %d" % interval_count[0])
		return

	var reset_interval_count := [0]
	var tick_count_before_reset := clock_order.size()
	ui.clock_schedule(1.0, func():
		reset_interval_count[0] += 1
		ui.reset_context()
	, 1.0)
	ui.clock_run_for(10.0, 10.0)
	ui.update("title")
	if clock_order.size() != tick_count_before_reset:
		_fail("Gua emitted a stale clock tick after a reset callback: %s" % [clock_order])
		return
	ui.clock_install(0.0, 10.0)
	ui.clock_pause()
	ui.clock_run_for(10.0, 10.0)
	ui.update("title")
	if reset_interval_count[0] != 1:
		_fail("Gua clock rescheduled an interval from a reset generation: %d" % reset_interval_count[0])
		return

	var stale_tick_subscriber_count := [0]
	ui.clock_tick.connect(func(_delta: float): ui.reset_context(), CONNECT_ONE_SHOT)
	ui.clock_tick.connect(func(_delta: float): stale_tick_subscriber_count[0] += 1, CONNECT_ONE_SHOT)
	ui.clock_run_for(10.0, 10.0)
	ui.update("title")
	if stale_tick_subscriber_count[0] != 0:
		_fail("Gua invoked a later clock_tick subscriber after an earlier subscriber reset the context.")
		return
	ui.clock_install(0.0, 10.0)
	ui.clock_pause()

	var limited_interval_count := [0]
	ui.clock_schedule(1.0, func(): limited_interval_count[0] += 1, 1.0)
	var limit_status := ui.get_clock()
	var limited_callbacks := ui._drain_clock_schedules(
		float(limit_status.get("now_ms", 0.0)) + 4.0, int(limit_status.get("generation", 0)), 2)
	if limited_callbacks != 2 or limited_interval_count[0] != 2 or not ui.clock_schedules.is_empty() or not ui.clock_execution_limit_reached:
		_fail("Gua clock callback budget did not cancel remaining interval work across the run.")
		return
	ui.clock_execution_limit_reached = false

	var stale_schedule_count := [0]
	ui.clock_schedule(100.0, func(): stale_schedule_count[0] += 1)
	var clock_reset: Dictionary = ui.context.reset_context({
		"expected_session_epoch": ui.get_context_status().get("session_epoch", 0),
		"flags": ui.RESET_CLOCK_FLAG,
	})
	if clock_reset.get("result", 0) != 1:
		_fail("Gua direct clock reset failed: %s" % clock_reset)
		return
	ui.update("title")
	if not ui.clock_schedules.is_empty():
		_fail("Gua retained schedules after observing a remote clock reset.")
		return
	var reinstalled_clock := ui.clock_install(0.0, 10.0)
	ui.clock_pause()
	ui.clock_run_for(100.0)
	ui.update("title")
	if stale_schedule_count[0] != 0:
		_fail("Gua invoked a schedule from a stale clock generation.")
		return
	var rejected_install := ui.clock_install(0.0, 10.0)
	if reinstalled_clock.get("result", 0) != 1 or rejected_install.get("result", 0) != -3 or rejected_install.get("error", "") != "invalid_state":
		_fail("Gua Godot clock controls did not surface native operation results: %s / %s" % [reinstalled_clock, rejected_install])
		return

	var precision_reset := ui.reset_context({
		"expected_session_epoch": ui.get_context_status().get("session_epoch", 0),
		"flags": ui.RESET_CLOCK_FLAG,
	})
	var invalid_preinstall_interval_count := [0]
	var pending_precision_schedule := ui.clock_schedule(
		0.0, func(): invalid_preinstall_interval_count[0] += 1, 1.0)
	var precision_install := ui.clock_install(1e16, 2.0)
	ui.clock_pause()
	ui.update("title")
	if precision_reset.get("result", 0) != 1 or pending_precision_schedule == 0 or precision_install.get("result", 0) != 1 \
			or invalid_preinstall_interval_count[0] != 0 or not ui.clock_schedules.is_empty():
		_fail("Gua retained a pre-install interval that cannot advance the installed timeline.")
		return
	if ui.clock_schedule(0.0, func(): invalid_preinstall_interval_count[0] += 1, 1.0) != 0:
		_fail("Gua accepted an interval that cannot advance its representable deadline.")
		return
	var precision_status := ui.get_clock()
	var precision_generation := int(precision_status.get("generation", 0))
	var precision_now_ms := float(precision_status.get("now_ms", 0.0))
	ui.clock_schedules.append({
		"id": ui.next_clock_schedule_id,
		"generation": precision_generation,
		"due_ms": precision_now_ms,
		"bind_on_install": false,
		"interval_ms": 1.0,
		"callback": func(): invalid_preinstall_interval_count[0] += 1,
	})
	ui.next_clock_schedule_id += 1
	var precision_callbacks := ui._drain_clock_schedules(precision_now_ms, precision_generation, 10)
	if precision_callbacks != 1 or invalid_preinstall_interval_count[0] != 1 \
			or not ui.clock_schedules.is_empty() or ui.clock_execution_limit_reached:
		_fail("Gua reinserted a repeating schedule whose deadline did not advance.")
		return

	var leaked := ui.enqueue_action({"action": "focus", "node_id": "start"})
	if leaked.get("request_id", 0) == 0:
		_fail("Gua smoke could not create a pending request for reset validation.")
		return
	var before_reset := ui.get_context_status()
	var strict_report := ui.reset_context({"strict": true, "expected_session_epoch": before_reset.get("session_epoch", 0)})
	if strict_report.get("result", 0) != -2 or ui.get_context_status().get("pending_request_count", 0) != 1:
		_fail("Gua strict reset discarded or missed a pending request: %s" % strict_report)
		return
	var reset_report := ui.reset_context({"expected_session_epoch": before_reset.get("session_epoch", 0)})
	var after_reset := ui.get_context_status()
	if reset_report.get("result", 0) != 1 or after_reset.get("session_epoch", 0) != before_reset.get("session_epoch", 0) + 1:
		_fail("Gua reset did not advance the session epoch: %s / %s" % [reset_report, after_reset])
		return
	if reset_report.get("discarded_world_object_count", 0) != 1 or after_reset.get("world_object_count", -1) != 0 \
			or after_reset.get("world_frame_sequence", -1) != 0 or after_reset.get("world_revision", -1) != 0:
		_fail("Gua Godot reset omitted World Object Tree metadata: %s / %s" % [reset_report, after_reset])
		return
	if after_reset.get("frame_sequence", -1) != 0 or after_reset.get("revision", -1) != 0:
		_fail("Gua reset did not initialize frame/revision metadata: %s" % after_reset)
		return
	if not ui.buttons_by_id.is_empty() or not ui.controls_by_id.is_empty() or not ui.suppressed_clicks.is_empty():
		_fail("Gua adapter temporary caches survived a successful reset.")
		return

	print("Gua GDScript smoke passed.")
	call_deferred("_finish", 0)


func _on_start_pressed() -> void:
	if expected_click_request_id != 0:
		var premature: Dictionary = adapter.poll_event_v2()
		click_completed_before_handler = premature.get("request_id", 0) == expected_click_request_id
	pressed_count += 1


func _find_node(tree: Dictionary, id: String) -> Variant:
	for node in tree.get("nodes", []):
		if node.get("id", "") == id:
			return node
	return null


func _find_world_object(tree: Dictionary, id: String) -> Variant:
	for object in tree.get("objects", []):
		if object.get("id", "") == id:
			return object
	return null


func _fail(message: String) -> void:
	push_error(message)
	call_deferred("_finish", 1)


func _finish(exit_code: int) -> void:
	if finishing:
		return
	finishing = true
	if adapter != null:
		adapter.dispose()
	adapter = null
	if is_instance_valid(smoke_root):
		smoke_root.queue_free()
	smoke_root = null
	await get_tree().process_frame
	get_tree().quit(exit_code)
