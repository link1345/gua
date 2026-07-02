extends Control

@export var start_inspector_bridge_on_ready := true
@export var inspector_bridge_port := 8765

var ui := GuaContext.new()
var loading := false
var title_label: Label
var start_button: Button
var settings_button: Button
var loading_label: Label


func _ready() -> void:
	_build_ui()
	_rebuild_gua_frame()

	if start_inspector_bridge_on_ready:
		_start_inspector_bridge()


func _process(_delta: float) -> void:
	_rebuild_gua_frame()
	_poll_gua_events()


func _build_ui() -> void:
	title_label = Label.new()
	title_label.text = "Gua GDScript Sample"
	title_label.position = Vector2(420, 220)
	title_label.size = Vector2(440, 56)
	title_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	add_child(title_label)

	start_button = Button.new()
	start_button.text = "Start Game"
	start_button.position = Vector2(512, 312)
	start_button.size = Vector2(256, 56)
	start_button.pressed.connect(func() -> void:
		ui.enqueue_click("start")
	)
	add_child(start_button)

	settings_button = Button.new()
	settings_button.text = "Settings"
	settings_button.position = Vector2(512, 384)
	settings_button.size = Vector2(256, 56)
	settings_button.pressed.connect(func() -> void:
		ui.enqueue_click("settings")
	)
	add_child(settings_button)

	loading_label = Label.new()
	loading_label.text = "Loading..."
	loading_label.position = Vector2(544, 328)
	loading_label.size = Vector2(192, 48)
	loading_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	loading_label.visible = false
	add_child(loading_label)


func _rebuild_gua_frame() -> void:
	ui.begin_frame("loading" if loading else "title")
	ui.register_node(
		"root",
		"screen",
		"Loading Screen" if loading else "Title Screen",
		Rect2(Vector2.ZERO, size),
		true,
		false
	)

	if loading:
		_register_control_node("loading", "text", "Loading...", loading_label, false)
	else:
		_register_control_node("title", "text", "Gua GDScript Sample", title_label, false)
		_register_control_node("start", "button", "Start Game", start_button, true)
		_register_control_node("settings", "button", "Settings", settings_button, true)

	ui.end_frame()


func _register_control_node(id: String, role: String, label: String, control: Control, enabled: bool) -> void:
	var disabled := false
	if control is BaseButton:
		disabled = (control as BaseButton).disabled

	ui.register_node(
		id,
		role,
		label,
		Rect2(control.global_position, control.size),
		control.visible,
		enabled and not disabled
	)


func _poll_gua_events() -> void:
	while true:
		var event := ui.poll_event()
		if event.is_empty():
			return

		if event.get("type") == "click" and event.get("node_id") == "start":
			_show_loading()


func _show_loading() -> void:
	loading = true
	title_label.visible = false
	start_button.visible = false
	start_button.disabled = true
	settings_button.visible = false
	settings_button.disabled = true
	loading_label.visible = true
	_rebuild_gua_frame()


func _start_inspector_bridge() -> void:
	if ui.start_inspector_bridge(inspector_bridge_port):
		print("Gua Inspector bridge listening on %s" % ui.inspector_bridge_url())
		return

	push_warning("Failed to start Gua Inspector bridge on ws://127.0.0.1:%d" % inspector_bridge_port)
