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
	var scroll_content := Control.new()
	scroll_content.custom_minimum_size = Vector2(1000, 1000)
	scroll.add_child(scroll_content)
	screen.add_child(scroll)

	await process_frame

	var ui := GuaAutoAdapterScript.new()
	var missing_methods := ui._missing_context_methods(RefCounted.new())
	if not missing_methods.has("consume_click_request"):
		_fail("Gua smoke did not detect missing consume_click_request on an incompatible context.")
		return

	ui.attach(screen)
	ui.update("title")
	await process_frame
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
	ui.update("title")
	var action_click_event := ui.poll_event_v2()
	if pressed_count != 2 or action_click_event.get("request_id", 0) != action_click.get("request_id", 0) or not action_click_event.get("succeeded", false):
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
	if after_reset.get("frame_sequence", -1) != 0 or after_reset.get("revision", -1) != 0:
		_fail("Gua reset did not initialize frame/revision metadata: %s" % after_reset)
		return
	if not ui.buttons_by_id.is_empty() or not ui.controls_by_id.is_empty() or not ui.suppressed_clicks.is_empty():
		_fail("Gua adapter temporary caches survived a successful reset.")
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
