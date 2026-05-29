@tool
extends KotorResourceEditorBase

@onready var _editor: CodeEdit = %CodeEdit
@onready var _status: Label = %StatusLabel


func _apply_bridge_data(data: Dictionary) -> void:
	var payload: Dictionary = data.get("payload", {})
	if payload.get("format") == "text":
		_editor.text = str(payload.get("text", ""))
	elif payload.has("data"):
		_editor.text = JSON.stringify(payload.get("data"), "\t")
	else:
		_editor.text = JSON.stringify(payload, "\t")
	_status.text = resource_path.get_file()


func build_write_payload() -> Dictionary:
	return {"format": "text", "text": _editor.text}


func _on_save_pressed() -> void:
	var result := FormatBridge.write_file(resource_path, build_write_payload())
	if result.get("ok", false):
		_dirty = false
		_status.text = "Saved"
		saved.emit(resource_path)
	else:
		_status.text = "Save failed: %s" % str(result.get("error", "unknown"))


func _on_text_changed() -> void:
	mark_dirty()
