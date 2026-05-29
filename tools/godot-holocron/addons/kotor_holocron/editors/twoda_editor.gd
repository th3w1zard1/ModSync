@tool
extends KotorResourceEditorBase

@onready var _table: Tree = %DataTree
@onready var _status: Label = %StatusLabel

var _headers: PackedStringArray = PackedStringArray()
var _rows: Array = []


func _ready() -> void:
	_table.set_column_titles_visible(true)
	_table.columns = 1
	_table.column_titles_visible = true


func _apply_bridge_data(data: Dictionary) -> void:
	var payload: Dictionary = data.get("payload", {})
	_headers = PackedStringArray(payload.get("data", {}).get("headers", []))
	_rows = payload.get("data", {}).get("rows", []).duplicate(true)
	_rebuild_tree()
	_status.text = "%d columns, %d rows" % [_headers.size(), _rows.size()]


func build_write_payload() -> Dictionary:
	return {"format": "twoda", "data": {"headers": Array(_headers), "rows": _rows}}


func _rebuild_tree() -> void:
	_table.clear()
	_table.columns = max(1, _headers.size() + 1)
	_table.set_column_title(0, "label")
	for i in _headers.size():
		_table.set_column_title(i + 1, _headers[i])
	for row in _rows:
		var item := _table.create_item()
		item.set_text(0, str(row.get("label", "")))
		var cells: Array = row.get("cells", [])
		for i in _headers.size():
			var value := ""
			if i < cells.size():
				value = str(cells[i])
			item.set_text(i + 1, value)


func _on_add_row_pressed() -> void:
	var cells: Array = []
	cells.resize(_headers.size())
	for i in _headers.size():
		cells[i] = ""
	_rows.append({"label": str(_rows.size()), "cells": cells})
	mark_dirty()
	_rebuild_tree()


func _on_save_pressed() -> void:
	var result := FormatBridge.write_file(resource_path, build_write_payload())
	if result.get("ok", false):
		_dirty = false
		_status.text = "Saved (%d bytes)" % int(result.get("bytes", 0))
		saved.emit(resource_path)
	else:
		_status.text = "Save failed: %s" % str(result.get("error", "unknown"))
