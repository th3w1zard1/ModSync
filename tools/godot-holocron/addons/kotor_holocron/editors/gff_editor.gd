@tool
extends KotorResourceEditorBase

@onready var _json_edit: TextEdit = %JsonEdit
@onready var _status: Label = %StatusLabel

var _format: String = "gff"


func _apply_bridge_data(data: Dictionary) -> void:
	var payload: Dictionary = data.get("payload", {})
	_format = str(payload.get("format", "gff"))
	var body: Variant = payload.get("data", payload)
	_json_edit.text = JSON.stringify(body, "\t")
	_status.text = "Format: %s" % _format


func build_write_payload() -> Dictionary:
	var parsed: Variant = JSON.parse_string(_json_edit.text)
	if parsed == null:
		push_error("Invalid JSON in editor")
		return {}
	if _format == "twoda":
		return {"format": "twoda", "data": parsed}
	if _format == "text":
		return {"format": "text", "text": str(parsed)}
	return {"format": _format, "data": parsed}


func _on_save_pressed() -> void:
	var payload := build_write_payload()
	if payload.is_empty():
		_status.text = "Invalid JSON — fix before saving"
		return
	var result := FormatBridge.write_file(resource_path, payload)
	if result.get("ok", false):
		_dirty = false
		_status.text = "Saved"
		saved.emit(resource_path)
	else:
		_status.text = "Save failed: %s" % str(result.get("error", "unknown"))


func _on_text_changed() -> void:
	mark_dirty()
