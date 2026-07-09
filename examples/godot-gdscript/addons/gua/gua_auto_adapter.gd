class_name GuaAutoAdapter
extends RefCounted

const META_ID := "gua_id"
const CONTEXT_CLASS := "GuaContext"
const GDEXTENSION_RESOURCE := "res://addons/gua/gua.gdextension"
const REBUILD_COMMAND := "cmake --build --preset windows-msvc-debug --target gua-godot"
const REQUIRED_CONTEXT_METHODS := [
	"begin_frame",
	"register_node",
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
	_collect_control(root)
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


func _collect_control(control: Control) -> void:
	var id := _control_id(control)
	var role := _control_role(control)
	var label := _control_label(control)
	context.register_node(
		id,
		role,
		label,
		Rect2(control.global_position, control.size),
		control.is_visible_in_tree(),
		_control_enabled(control)
	)

	if control is BaseButton:
		buttons_by_id[id] = control
		_connect_button(control as BaseButton, id)

	for child in control.get_children():
		if child is Control:
			_collect_control(child as Control)


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
	if control is BaseButton:
		return (control as BaseButton).text
	if control is Label:
		return (control as Label).text
	if control is LineEdit:
		return (control as LineEdit).text
	if control is TextEdit:
		return (control as TextEdit).text
	return control.name


func _control_enabled(control: Control) -> bool:
	if control is BaseButton:
		return not (control as BaseButton).disabled
	if control is LineEdit:
		return (control as LineEdit).editable
	if control is TextEdit:
		return (control as TextEdit).editable
	return false
