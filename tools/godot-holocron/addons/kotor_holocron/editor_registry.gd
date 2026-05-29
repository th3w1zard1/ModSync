class_name EditorRegistry
extends RefCounted

const SCENES := {
	KotorResourceTypes.EditorKind.TWODA: preload("res://addons/kotor_holocron/editors/twoda_editor.tscn"),
	KotorResourceTypes.EditorKind.GFF: preload("res://addons/kotor_holocron/editors/gff_editor.tscn"),
	KotorResourceTypes.EditorKind.TEXT: preload("res://addons/kotor_holocron/editors/text_editor.tscn"),
	KotorResourceTypes.EditorKind.NCS: preload("res://addons/kotor_holocron/editors/text_editor.tscn"),
	KotorResourceTypes.EditorKind.TLK: preload("res://addons/kotor_holocron/editors/gff_editor.tscn"),
	KotorResourceTypes.EditorKind.SSF: preload("res://addons/kotor_holocron/editors/gff_editor.tscn"),
	KotorResourceTypes.EditorKind.ERF: preload("res://addons/kotor_holocron/editors/gff_editor.tscn"),
	KotorResourceTypes.EditorKind.BINARY: preload("res://addons/kotor_holocron/editors/text_editor.tscn"),
}


static func scene_for_kind(kind: int) -> PackedScene:
	if SCENES.has(kind):
		return SCENES[kind]
	return preload("res://addons/kotor_holocron/editors/text_editor.tscn")


static func create_editor(kind: int) -> KotorResourceEditorBase:
	var scene: PackedScene = scene_for_kind(kind)
	var node := scene.instantiate()
	if node is KotorResourceEditorBase:
		return node
	push_warning("Editor scene for kind %d is not KotorResourceEditorBase" % kind)
	return null
