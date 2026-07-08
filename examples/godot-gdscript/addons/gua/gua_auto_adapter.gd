class_name GuaAutoAdapter
extends RefCounted

const META_ID := "gua_id"

var context := GuaContext.new()
var root: Control
var buttons_by_id: Dictionary = {}
var connected_buttons: Dictionary = {}
var suppressed_clicks: Dictionary = {}


func attach(root_control: Control) -> void:
	root = root_control


func update(screen: String) -> void:
	if root == null:
		push_error("GuaAutoAdapter.update called before attach.")
		return

	context.begin_frame(screen)
	buttons_by_id.clear()
	_collect_control(root)
	context.end_frame()
	_dispatch_click_requests()


func start_inspector_bridge(port: int = 8765) -> bool:
	return context.start_inspector_bridge(port)


func inspector_bridge_url() -> String:
	return context.inspector_bridge_url()


func get_ui_tree_json() -> String:
	return context.get_ui_tree_json()


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
