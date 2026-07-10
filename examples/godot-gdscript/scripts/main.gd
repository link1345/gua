extends Control

const GuaAutoAdapterScript := preload("res://addons/gua/gua_auto_adapter.gd")

@export var start_inspector_bridge_on_ready := true
@export var inspector_bridge_port := 8765

var ui := GuaAutoAdapterScript.new()
var loading := false
var title_label: Label
var start_button: Button
var settings_button: Button
var loading_label: Label
var visual_e2e := false
var visual_e2e_update_elapsed := 0.0


func _ready() -> void:
	visual_e2e = OS.get_environment("GUA_VISUAL_E2E") == "1"
	if OS.has_environment("GUA_BRIDGE_PORT"):
		inspector_bridge_port = int(OS.get_environment("GUA_BRIDGE_PORT"))
	_build_ui()
	ui.attach(self)
	ui.update(_current_screen())

	if start_inspector_bridge_on_ready:
		_start_inspector_bridge()
	if visual_e2e:
		_capture_visual_e2e.call_deferred()


func _process(delta: float) -> void:
	if visual_e2e:
		visual_e2e_update_elapsed += delta
		if visual_e2e_update_elapsed < 0.1:
			return
		visual_e2e_update_elapsed = 0.0
	ui.update(_current_screen())


func _build_ui() -> void:
	title_label = Label.new()
	title_label.name = "title"
	title_label.text = "Gua GDScript Sample"
	title_label.position = Vector2(420, 220)
	title_label.size = Vector2(440, 56)
	title_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	add_child(title_label)

	start_button = Button.new()
	start_button.name = "start"
	start_button.text = "Start Game"
	start_button.position = Vector2(512, 312)
	start_button.size = Vector2(256, 56)
	start_button.pressed.connect(_show_loading)
	add_child(start_button)

	settings_button = Button.new()
	settings_button.name = "settings"
	settings_button.text = "Settings"
	settings_button.position = Vector2(512, 384)
	settings_button.size = Vector2(256, 56)
	add_child(settings_button)

	loading_label = Label.new()
	loading_label.name = "loading"
	loading_label.text = "Loading..."
	loading_label.position = Vector2(544, 328)
	loading_label.size = Vector2(192, 48)
	loading_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	loading_label.visible = false
	add_child(loading_label)

	if visual_e2e:
		_build_visual_e2e_controls()


func _build_visual_e2e_controls() -> void:
	if OS.get_environment("GUA_VISUAL_E2E_DIFF") == "1":
		title_label.text = "Gua Visual Difference"

	var input := LineEdit.new()
	input.name = "visual_password"
	input.set_meta("gua_id", "visual-password")
	input.set_meta("gua_sensitive", true)
	input.secret = true
	input.placeholder_text = "Password"
	input.position = Vector2(512, 456)
	input.size = Vector2(256, 40)
	add_child(input)

	var checkbox := CheckBox.new()
	checkbox.name = "visual_checkbox"
	checkbox.set_meta("gua_id", "visual-checkbox")
	checkbox.text = "Enable visual mode"
	checkbox.position = Vector2(512, 512)
	checkbox.size = Vector2(256, 40)
	add_child(checkbox)

	var select := OptionButton.new()
	select.name = "visual_select"
	select.set_meta("gua_id", "visual-select")
	select.add_item("Alpha")
	select.add_item("Beta")
	select.position = Vector2(512, 568)
	select.size = Vector2(256, 40)
	add_child(select)


func _capture_visual_e2e() -> void:
	await get_tree().process_frame
	await get_tree().process_frame
	await RenderingServer.frame_post_draw
	ui.update(_current_screen())
	var result := ui.capture_viewport_screenshot()
	if not result.get("ok", false):
		push_error("Gua real-renderer visual E2E capture failed: %s" % result)
	else:
		print("GUA_VISUAL_E2E_CAPTURED %sx%s" % [result.get("width", 0), result.get("height", 0)])


func _show_loading() -> void:
	loading = true
	title_label.visible = false
	start_button.visible = false
	start_button.disabled = true
	settings_button.visible = false
	settings_button.disabled = true
	loading_label.visible = true
	ui.update(_current_screen())


func _start_inspector_bridge() -> void:
	if ui.start_inspector_bridge(inspector_bridge_port):
		print("Gua Inspector bridge listening on %s" % ui.inspector_bridge_url())
		return

	push_warning("Failed to start Gua Inspector bridge on ws://127.0.0.1:%d" % inspector_bridge_port)


func _current_screen() -> String:
	return "loading" if loading else "title"
