class_name GuaAutoAdapter
extends RefCounted

const META_ID := "gua_id"
const META_SENSITIVE := "gua_sensitive"
const CONTEXT_CLASS := "GuaContext"
const GDEXTENSION_RESOURCE := "res://addons/gua/gua.gdextension"
const REBUILD_COMMAND := "cmake --build --preset windows-msvc-debug --target gua-godot"
const REQUIRED_CONTEXT_METHODS := [
	"begin_frame",
	"register_node",
	"register_node_v2",
	"end_frame",
	"get_ui_tree_json",
	"set_screenshot",
	"get_screenshot_json",
	"enqueue_click",
	"consume_click_request",
	"emit_click",
	"poll_event",
	"enqueue_action",
	"consume_action_request",
	"emit_action_result",
	"poll_event_v2",
	"get_context_status",
	"reset_context",
	"start_inspector_bridge",
	"inspector_bridge_url",
]

var context: Object
var root: Control
var buttons_by_id: Dictionary = {}
var tabs_by_id: Dictionary = {}
var list_items_by_id: Dictionary = {}
var controls_by_id: Dictionary = {}
var connected_buttons: Dictionary = {}
var suppressed_clicks: Dictionary = {}
var gdextension_resource: Resource
var unavailable := false


func attach(root_control: Control) -> void:
	root = root_control
	_ensure_context()


func update(screen: String) -> void:
	if not _ensure_context():
		return

	if root == null:
		push_error("GuaAutoAdapter.update called before attach.")
		return

	context.begin_frame(screen)
	buttons_by_id.clear()
	tabs_by_id.clear()
	list_items_by_id.clear()
	controls_by_id.clear()
	_collect_control(root, "")
	context.end_frame()
	_dispatch_click_requests()
	_dispatch_action_requests()


func capture_viewport_screenshot(image_override: Image = null) -> Dictionary:
	if not _ensure_context() or root == null:
		return {"ok": false, "error": "Gua adapter is not attached."}
	var image := image_override
	if image == null:
		image = root.get_viewport().get_texture().get_image()
	if image == null or image.is_empty():
		return {"ok": false, "error": "Godot viewport capture returned an empty image."}
	var png := image.save_png_to_buffer()
	context.set_screenshot("data:image/png;base64," + Marshalls.raw_to_base64(png), image.get_width(), image.get_height())
	return {"ok": true, "width": image.get_width(), "height": image.get_height()}


func start_inspector_bridge(port: int = 8765) -> bool:
	if not _ensure_context():
		return false

	return context.start_inspector_bridge(port)


func inspector_bridge_url() -> String:
	if not _ensure_context():
		return ""

	return context.inspector_bridge_url()


func get_ui_tree_json() -> String:
	if not _ensure_context():
		return ""

	return context.get_ui_tree_json()


func enqueue_click(id: String) -> bool:
	if not _ensure_context():
		return false

	return context.enqueue_click(id)


func poll_event() -> Dictionary:
	if not _ensure_context():
		return {}

	return context.poll_event()


func enqueue_action(request: Dictionary) -> Dictionary:
	if not _ensure_context():
		return {"error_code": -1, "request_id": 0}
	return context.enqueue_action(request)


func poll_event_v2() -> Dictionary:
	if not _ensure_context():
		return {}
	return context.poll_event_v2()


func get_context_status() -> Dictionary:
	if not _ensure_context():
		return {}
	return context.get_context_status()


func reset_context(options: Dictionary = {}) -> Dictionary:
	if not _ensure_context():
		return {"result": -1}
	var resolved := options.duplicate()
	if not resolved.has("expected_session_epoch"):
		resolved["expected_session_epoch"] = context.get_context_status().get("session_epoch", 0)
	var report: Dictionary = context.reset_context(resolved)
	if report.get("result", -1) == 1:
		buttons_by_id.clear()
		tabs_by_id.clear()
		list_items_by_id.clear()
		controls_by_id.clear()
		suppressed_clicks.clear()
	return report


func _ensure_context() -> bool:
	if context != null:
		return not unavailable
	if unavailable:
		return false

	if gdextension_resource == null:
		gdextension_resource = load(GDEXTENSION_RESOURCE)
		if gdextension_resource == null:
			_mark_unavailable(
				"Failed to load %s. Ensure the Gua addon files are installed and rebuild the Godot GDExtension DLL with: %s"
				% [GDEXTENSION_RESOURCE, REBUILD_COMMAND]
			)
			return false

	if not ClassDB.class_exists(CONTEXT_CLASS) or not ClassDB.can_instantiate(CONTEXT_CLASS):
		_mark_unavailable(
			"%s is not available. Ensure addons/gua/gua.gdextension is enabled and rebuild the Godot GDExtension DLL with: %s"
			% [CONTEXT_CLASS, REBUILD_COMMAND]
		)
		return false

	context = ClassDB.instantiate(CONTEXT_CLASS)
	if context == null:
		_mark_unavailable(
			"Failed to instantiate %s. Ensure addons/gua/gua.gdextension loaded successfully and rebuild with: %s"
			% [CONTEXT_CLASS, REBUILD_COMMAND]
		)
		return false

	var missing_methods := _missing_context_methods(context)
	if not missing_methods.is_empty():
		_mark_unavailable(
			"%s is missing required method '%s'. The vendored gua_godot Windows debug DLL is stale. Rebuild it with: %s"
			% [CONTEXT_CLASS, missing_methods[0], REBUILD_COMMAND]
		)
		return false

	return true


func _missing_context_methods(candidate: Object) -> Array:
	var missing_methods := []
	for method in REQUIRED_CONTEXT_METHODS:
		if not candidate.has_method(method):
			missing_methods.append(method)
	return missing_methods


func _mark_unavailable(message: String) -> void:
	unavailable = true
	context = null
	push_error(message)


func _collect_control(control: Control, parent_id: String) -> void:
	var id := _control_id(control)
	var role := _control_role(control)
	var label := _control_label(control)
	var descriptor := {
		"id": id,
		"role": role,
		"label": label,
		"bounds": Rect2(control.global_position, control.size),
		"visible": control.is_visible_in_tree(),
		"enabled": _control_enabled(control),
		"focused": _control_focused(control),
	}
	if not parent_id.is_empty():
		descriptor["parent_id"] = parent_id
	var text := _control_text(control)
	if text != null:
		descriptor["text"] = text
	var value := _control_value(control)
	if value != null:
		descriptor["value"] = value
	if control is CheckBox:
		descriptor["checked"] = (control as CheckBox).button_pressed
	context.register_node_v2(descriptor)
	controls_by_id[id] = control

	if control is BaseButton:
		buttons_by_id[id] = control
		_connect_button(control as BaseButton, id)
	if control is ItemList:
		_collect_item_list_items(control as ItemList, id)
	if control is TabContainer:
		_collect_tab_items(control as TabContainer, id)

	for child in control.get_children():
		if child is Control:
			_collect_control(child as Control, id)


func _collect_item_list_items(item_list: ItemList, parent_id: String) -> void:
	for index in range(item_list.item_count):
		var label := item_list.get_item_text(index)
		var id := "%s$item:%d" % [parent_id, index]
		list_items_by_id[id] = {"list": item_list, "index": index}
		context.register_node_v2({
			"id": id,
			"parent_id": parent_id,
			"role": "listitem",
			"label": label,
			"text": label,
			"bounds": Rect2(item_list.global_position, item_list.size),
			"visible": item_list.is_visible_in_tree(),
			"enabled": not item_list.is_item_disabled(index),
			"selected": item_list.is_selected(index),
		})


func _collect_tab_items(tab_container: TabContainer, parent_id: String) -> void:
	for index in range(tab_container.get_tab_count()):
		var label := tab_container.get_tab_title(index)
		var id := "%s$tab:%d" % [parent_id, index]
		tabs_by_id[id] = {"container": tab_container, "index": index}
		context.register_node_v2({
			"id": id,
			"parent_id": parent_id,
			"role": "tab",
			"label": label,
			"text": label,
			"bounds": Rect2(tab_container.global_position, tab_container.size),
			"visible": tab_container.is_visible_in_tree(),
			"enabled": not tab_container.is_tab_disabled(index),
			"selected": tab_container.current_tab == index,
		})


func _dispatch_click_requests() -> void:
	for id in buttons_by_id.keys():
		var button := buttons_by_id[id] as BaseButton
		while true:
			var request: Dictionary = context.consume_action_request("click", id)
			if request.is_empty():
				break
			var error_code := -3 if not button.is_visible_in_tree() else (-4 if button.disabled else 0)
			if error_code != 0:
				_emit_click_result(request, id, error_code)
				continue

			var group := button.button_group
			if button.toggle_mode and not (button.button_pressed and group != null and not group.allow_unpress):
				button.button_pressed = not button.button_pressed
			_emit_click_result(request, id, 0)
			suppressed_clicks[id] = true
			button.emit_signal("pressed")

	for id in tabs_by_id.keys():
		var target: Dictionary = tabs_by_id[id]
		var tab_container := target["container"] as TabContainer
		var index := int(target["index"])
		while true:
			var request: Dictionary = context.consume_action_request("click", id)
			if request.is_empty():
				break
			var error_code := -3 if not tab_container.is_visible_in_tree() else (-4 if tab_container.is_tab_disabled(index) else 0)
			if error_code != 0:
				_emit_click_result(request, id, error_code)
				continue

			tab_container.current_tab = index
			_emit_click_result(request, id, 0)


func _emit_click_result(request: Dictionary, id: String, error_code: int) -> void:
	context.emit_action_result({
		"request_id": request.get("request_id", 0),
		"action": "click",
		"node_id": id,
		"succeeded": error_code == 0,
		"error_code": error_code,
	})


func _dispatch_action_requests() -> void:
	for id in controls_by_id.keys():
		var control := controls_by_id[id] as Control
		for action in ["focus", "set_value", "set_checked", "select", "scroll", "press_key"]:
			while true:
				var request: Dictionary = context.consume_action_request(action, id)
				if request.is_empty():
					break
				var error_code := _apply_action(control, action, request)
				context.emit_action_result({
					"request_id": request.get("request_id", 0),
					"action": action,
					"node_id": id,
					"succeeded": error_code == 0,
					"error_code": error_code,
					"value": request.get("value", ""),
					"sensitive": request.get("sensitive", false),
				})
	for id in list_items_by_id.keys():
		_dispatch_derived_select_requests(id, list_items_by_id[id])
	for id in tabs_by_id.keys():
		_dispatch_derived_select_requests(id, tabs_by_id[id])
	while true:
		var request: Dictionary = context.consume_action_request("press_key", "")
		if request.is_empty():
			break
		var focused := root.get_viewport().gui_get_focus_owner()
		var error_code := _apply_action(focused, "press_key", request) if focused is Control else -2
		context.emit_action_result({
			"request_id": request.get("request_id", 0), "action": "press_key", "node_id": "",
			"succeeded": error_code == 0, "error_code": error_code,
		})


func _apply_action(control: Control, action: String, request: Dictionary) -> int:
	if not control.is_visible_in_tree():
		return -3
	if not _control_enabled(control):
		return -4
	match action:
		"focus":
			if control.focus_mode == Control.FOCUS_NONE:
				return -5
			var focus_target: Control = (control as SpinBox).get_line_edit() if control is SpinBox else control
			if focus_target.focus_mode == Control.FOCUS_NONE:
				return -5
			focus_target.grab_focus()
			if not focus_target.has_focus():
				return -5
		"set_value":
			var value = request.get("value", "")
			if request.get("sensitive", false):
				control.set_meta(META_SENSITIVE, true)
			if control is LineEdit:
				(control as LineEdit).text = value
			elif control is TextEdit:
				(control as TextEdit).text = value
			elif control is Range and str(value).is_valid_float():
				(control as Range).value = float(value)
			else:
				return -6
		"set_checked":
			if control is BaseButton:
				(control as BaseButton).button_pressed = request.get("bool_value", false)
			else:
				return -5
		"select":
			if not _select_value(control, str(request.get("value", ""))):
				return -6
		"scroll":
			if control is ScrollContainer:
				var scroll := control as ScrollContainer
				scroll.scroll_horizontal += int(request.get("delta_x", 0.0))
				scroll.scroll_vertical += int(request.get("delta_y", 0.0))
			elif control is ItemList:
				var item_list := control as ItemList
				item_list.get_h_scroll_bar().value += float(request.get("delta_x", 0.0))
				item_list.get_v_scroll_bar().value += float(request.get("delta_y", 0.0))
			else:
				return -5
		"press_key":
			if control.focus_mode == Control.FOCUS_NONE:
				return -5
			if not control.has_focus():
				control.grab_focus()
			if not control.has_focus():
				return -5
			var event := InputEventKey.new()
			event.keycode = OS.find_keycode_from_string(str(request.get("key", "")))
			if event.keycode == KEY_NONE:
				return -6
			var modifiers := int(request.get("modifiers", 0))
			event.shift_pressed = (modifiers & 1) != 0
			event.alt_pressed = (modifiers & 2) != 0
			event.ctrl_pressed = (modifiers & 4) != 0
			event.meta_pressed = (modifiers & 8) != 0
			event.pressed = true
			control.get_viewport().push_input(event, true)
			var release := event.duplicate() as InputEventKey
			release.pressed = false
			control.get_viewport().push_input(release, true)
		_:
			return -5
	return 0


func _dispatch_derived_select_requests(id: String, target: Dictionary) -> void:
	while true:
		var request: Dictionary = context.consume_action_request("select", id)
		if request.is_empty():
			break
		var error_code := _select_derived_item(target)
		context.emit_action_result({
			"request_id": request.get("request_id", 0),
			"action": "select",
			"node_id": id,
			"succeeded": error_code == 0,
			"error_code": error_code,
		})


func _select_derived_item(target: Dictionary) -> int:
	var index := int(target["index"])
	if target.has("list"):
		var item_list := target["list"] as ItemList
		if not item_list.is_visible_in_tree():
			return -3
		if item_list.is_item_disabled(index):
			return -4
		item_list.select(index)
		item_list.item_selected.emit(index)
		return 0
	var tab_container := target["container"] as TabContainer
	if not tab_container.is_visible_in_tree():
		return -3
	if tab_container.is_tab_disabled(index):
		return -4
	tab_container.current_tab = index
	return 0


func _select_value(control: Control, value: String) -> bool:
	if control is OptionButton:
		var option := control as OptionButton
		for index in range(option.item_count):
			var semantic_value := str(option.get_item_metadata(index)) if option.get_item_metadata(index) != null else option.get_item_text(index)
			if semantic_value == value:
				option.select(index)
				option.item_selected.emit(index)
				return true
	if control is ItemList:
		var item_list := control as ItemList
		for index in range(item_list.item_count):
			if item_list.get_item_text(index) == value:
				item_list.select(index)
				item_list.item_selected.emit(index)
				return true
	if control is TabContainer:
		var tabs := control as TabContainer
		for index in range(tabs.get_tab_count()):
			if tabs.get_tab_title(index) == value:
				tabs.current_tab = index
				return true
	return false


func _connect_button(button: BaseButton, id: String) -> void:
	var instance_id := button.get_instance_id()
	if connected_buttons.has(instance_id):
		return

	connected_buttons[instance_id] = true
	button.pressed.connect(_on_button_pressed.bind(id))


func _on_button_pressed(id: String) -> void:
	if suppressed_clicks.erase(id):
		return

	context.emit_click(id)


func _control_id(control: Control) -> String:
	if control.has_meta(META_ID):
		return str(control.get_meta(META_ID))
	if control == root:
		return "root"
	return str(root.get_path_to(control))


func _control_role(control: Control) -> String:
	if control is OptionButton:
		return "combobox"
	if control is ItemList:
		return "list"
	if control is TabContainer:
		return "tablist"
	if control is CheckBox:
		return "checkbox"
	if control is BaseButton:
		return "button"
	if control is Label:
		return "text"
	if control is LineEdit or control is TextEdit:
		return "textbox"
	if control is Slider:
		return "slider"
	if control is SpinBox:
		return "slider"
	if control is ScrollContainer:
		return "scrollarea"
	return "panel"


func _control_label(control: Control) -> String:
	if control.has_meta(META_SENSITIVE) and control.get_meta(META_SENSITIVE):
		return control.name
	if control is OptionButton:
		return control.name
	if control is BaseButton:
		return (control as BaseButton).text
	if control is Label:
		return (control as Label).text
	if control is LineEdit:
		return (control as LineEdit).text
	if control is TextEdit:
		return (control as TextEdit).text
	return control.name


func _control_text(control: Control) -> Variant:
	if control.has_meta(META_SENSITIVE) and control.get_meta(META_SENSITIVE):
		return null
	if control is BaseButton:
		return (control as BaseButton).text
	if control is Label:
		return (control as Label).text
	if control is LineEdit:
		return (control as LineEdit).text
	if control is TextEdit:
		return (control as TextEdit).text
	return null


func _control_value(control: Control) -> Variant:
	if control.has_meta(META_SENSITIVE) and control.get_meta(META_SENSITIVE):
		return null
	if control is OptionButton:
		var option := control as OptionButton
		return option.get_item_text(option.selected) if option.selected >= 0 else ""
	if control is LineEdit:
		return (control as LineEdit).text
	if control is TextEdit:
		return (control as TextEdit).text
	if control is Range:
		return str((control as Range).value)
	return null


func _control_enabled(control: Control) -> bool:
	if control is BaseButton:
		return not (control as BaseButton).disabled
	if control is LineEdit:
		return (control as LineEdit).editable
	if control is TextEdit:
		return (control as TextEdit).editable
	if control is SpinBox:
		return (control as SpinBox).editable
	if control is ItemList:
		return true
	if control is TabContainer:
		return true
	if control is Slider:
		return (control as Slider).editable
	return control.mouse_filter != Control.MOUSE_FILTER_IGNORE


func _control_focused(control: Control) -> bool:
	if control is SpinBox:
		return (control as SpinBox).get_line_edit().has_focus()
	return control.has_focus()
