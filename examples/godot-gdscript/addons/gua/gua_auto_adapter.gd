class_name GuaAutoAdapter
extends RefCounted

const META_ID := "gua_id"
const CONTEXT_CLASS := "GuaContext"
const GDEXTENSION_RESOURCE := "res://addons/gua/gua.gdextension"
const REBUILD_COMMAND := "cmake --build --preset windows-msvc-debug --target gua-godot"
const REQUIRED_CONTEXT_METHODS := [
	"begin_frame",
	"register_node",
	"register_node_v2",
	"end_frame",
	"get_ui_tree_json",
	"enqueue_click",
	"consume_click_request",
	"emit_click",
	"poll_event",
	"start_inspector_bridge",
	"inspector_bridge_url",
]

var context: Object
var root: Control
var buttons_by_id: Dictionary = {}
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
	_collect_control(root, "")
	context.end_frame()
	_dispatch_click_requests()


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
		"focused": control.has_focus(),
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
		context.register_node_v2({
			"id": "%s$item:%d" % [parent_id, index],
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
		context.register_node_v2({
			"id": "%s$tab:%d" % [parent_id, index],
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
		while context.consume_click_request(id):
			if button.disabled or not button.is_visible_in_tree():
				continue

			context.emit_click(id)
			suppressed_clicks[id] = true
			button.emit_signal("pressed")


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
	return "panel"


func _control_label(control: Control) -> String:
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
	if control is ItemList:
		return true
	if control is TabContainer:
		return true
	if control is Slider:
		return (control as Slider).editable
	return control.mouse_filter != Control.MOUSE_FILTER_IGNORE
