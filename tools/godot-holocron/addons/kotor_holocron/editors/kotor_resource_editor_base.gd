class_name KotorResourceEditorBase
extends Control

signal saved(path: String)
signal closed()

var resource_path: String = ""
var bridge_result: Dictionary = {}
var _dirty: bool = false


func load_resource(path: String, data: Dictionary) -> void:
	resource_path = path
	bridge_result = data
	_dirty = false
	_apply_bridge_data(data)


func is_dirty() -> bool:
	return _dirty


func mark_dirty() -> void:
	_dirty = true


func build_write_payload() -> Dictionary:
	return {}


func _apply_bridge_data(_data: Dictionary) -> void:
	pass
