class_name FormatBridge
extends RefCounted

const DEFAULT_BRIDGE_REL := "../../../bridge/kotor_format_bridge.py"


static func bridge_script_path() -> String:
	var env := OS.get_environment("KOTOR_FORMAT_BRIDGE")
	if env != "" and FileAccess.file_exists(env):
		return env
	var plugin_relative := ProjectSettings.globalize_path("res://addons/kotor_holocron/")
	var candidate := plugin_relative.path_join(DEFAULT_BRIDGE_REL)
	if FileAccess.file_exists(candidate):
		return candidate
	return candidate


static func python_executable() -> String:
	var env := OS.get_environment("KOTOR_PYTHON")
	if env != "":
		return env
	return "python3"


static func run_command(args: PackedStringArray) -> Dictionary:
	var output: Array = []
	var exit_code := OS.execute(python_executable(), [bridge_script_path()] + args, output, true, true)
	var text := "".join(output)
	if exit_code != 0 and text == "":
		return {
			"ok": false,
			"error": "Bridge process exited with code %d (no output). Is PyKotor installed?" % exit_code,
		}
	var parsed: Variant = _parse_bridge_json(text)
	if parsed == null:
		return {"ok": false, "error": "Bridge returned non-JSON: %s" % text.substr(0, 500)}
	if parsed is Dictionary:
		return parsed
	return {"ok": false, "error": "Bridge returned unexpected JSON type"}


static func probe(path: String) -> Dictionary:
	return run_command(["probe", path])


static func read_file(path: String, game: String = "") -> Dictionary:
	var args := PackedStringArray(["read", path])
	if game != "":
		args.append("--game")
		args.append(game)
	return run_command(args)


static func write_file(path: String, payload: Dictionary) -> Dictionary:
	var json_text := JSON.stringify(payload)
	var tmp := OS.get_cache_dir().path_join("kotor_bridge_payload_%d.json" % Time.get_ticks_msec())
	var file := FileAccess.open(tmp, FileAccess.WRITE)
	if file == null:
		return {"ok": false, "error": "Could not write temp payload file"}
	file.store_string(json_text)
	file.close()
	return run_command(["write", path, "--payload", "@" + tmp])


static func list_installations() -> Dictionary:
	return run_command(["installations"])


static func supported_types() -> Dictionary:
	return run_command(["supported-types"])


static func _parse_bridge_json(text: String) -> Variant:
	var trimmed := text.strip_edges()
	if trimmed.is_empty():
		return null
	var direct: Variant = JSON.parse_string(trimmed)
	if direct != null:
		return direct
	# PyKotor may print advisory lines before the JSON payload.
	var lines := trimmed.split("\n")
	for i in range(lines.size() - 1, -1, -1):
		var line := lines[i].strip_edges()
		if line.begins_with("{"):
			return JSON.parse_string(line)
	return null
