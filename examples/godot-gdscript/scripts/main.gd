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


func _ready() -> void:
	_build_ui()
	ui.attach(self)
	ui.update(_current_screen())

	if start_inspector_bridge_on_ready:
		_start_inspector_bridge()


func _process(_delta: float) -> void:
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
