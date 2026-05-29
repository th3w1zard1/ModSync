@tool
extends Control

@onready var _path_edit: LineEdit = %PathEdit
@onready var _status: Label = %StatusLabel
@onready var _editor_host: Control = %EditorHost
@onready var _install_list: ItemList = %InstallList

var _current_editor: KotorResourceEditorBase


func _ready() -> void:
	_refresh_installations()


func _refresh_installations() -> void:
	_install_list.clear()
	var result := FormatBridge.list_installations()
	if not result.get("ok", false):
		_status.text = "Bridge: %s" % str(result.get("error", "unavailable"))
		return
	for item in result.get("installations", []):
		var label := "%s — %s" % [item.get("game", "?"), item.get("path", "")]
		_install_list.add_item(label)


func _on_browse_pressed() -> void:
	var dialog := EditorFileDialog.new()
	dialog.file_mode = EditorFileDialog.FILE_MODE_OPEN_FILE
	dialog.access = EditorFileDialog.ACCESS_FILESYSTEM
	dialog.title = "Open KOTOR Resource"
	add_child(dialog)
	dialog.popup_centered_ratio(0.6)
	dialog.file_selected.connect(func(path: String) -> void:
		_path_edit.text = path
		_open_path(path)
		dialog.queue_free()
	)
	dialog.canceled.connect(func() -> void: dialog.queue_free())


func _on_open_pressed() -> void:
	var path := _path_edit.text.strip_edges()
	if path == "":
		_status.text = "Enter a file path"
		return
	_open_path(path)


func _open_path(path: String) -> void:
	var probe := FormatBridge.probe(path)
	if not probe.get("ok", false):
		_status.text = "Probe failed: %s" % str(probe.get("error", ""))
		return
	var ext := str(probe.get("extension", "")).to_lower()
	var kind := KotorResourceTypes.kind_for_extension(ext)
	var read_result := FormatBridge.read_file(path)
	if not read_result.get("ok", false):
		_status.text = "Read failed: %s" % str(read_result.get("error", ""))
		return
	_clear_editor()
	_current_editor = EditorRegistry.create_editor(kind)
	if _current_editor == null:
		_status.text = "No editor for .%s" % ext
		return
	_editor_host.add_child(_current_editor)
	_current_editor.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_current_editor.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_current_editor.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_current_editor.load_resource(path, read_result)
	_status.text = "%s — %s" % [
		KotorResourceTypes.kind_label(kind),
		path.get_file(),
	]


func _clear_editor() -> void:
	if _current_editor:
		_current_editor.queue_free()
		_current_editor = null
	for child in _editor_host.get_children():
		child.queue_free()
