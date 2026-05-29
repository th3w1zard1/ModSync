class_name KotorResourceTypes
extends RefCounted

## Editor kinds aligned with HolocronToolset coverage (phased implementation).
enum EditorKind {
	TWODA,
	GFF,
	TLK,
	SSF,
	ERF,
	TEXT,
	NCS,
	BINARY,
	UNSUPPORTED,
}

const EXTENSION_TO_KIND: Dictionary = {
	"2da": EditorKind.TWODA,
	"gff": EditorKind.GFF,
	"utc": EditorKind.GFF,
	"utd": EditorKind.GFF,
	"ute": EditorKind.GFF,
	"uti": EditorKind.GFF,
	"utm": EditorKind.GFF,
	"utp": EditorKind.GFF,
	"uts": EditorKind.GFF,
	"utt": EditorKind.GFF,
	"utw": EditorKind.GFF,
	"are": EditorKind.GFF,
	"dlg": EditorKind.GFF,
	"fac": EditorKind.GFF,
	"git": EditorKind.GFF,
	"ifo": EditorKind.GFF,
	"jrl": EditorKind.GFF,
	"gui": EditorKind.GFF,
	"pth": EditorKind.GFF,
	"tlk": EditorKind.TLK,
	"fmh": EditorKind.TLK,
	"fml": EditorKind.TLK,
	"ssf": EditorKind.SSF,
	"erf": EditorKind.ERF,
	"mod": EditorKind.ERF,
	"sav": EditorKind.ERF,
	"rim": EditorKind.ERF,
	"nss": EditorKind.TEXT,
	"txt": EditorKind.TEXT,
	"lyt": EditorKind.TEXT,
	"vis": EditorKind.TEXT,
	"txi": EditorKind.TEXT,
	"tx": EditorKind.TEXT,
	"ncs": EditorKind.NCS,
	"wav": EditorKind.BINARY,
	"bwm": EditorKind.BINARY,
	"mdl": EditorKind.BINARY,
	"mdx": EditorKind.BINARY,
	"tpc": EditorKind.BINARY,
	"tga": EditorKind.BINARY,
	"ltr": EditorKind.BINARY,
	"lip": EditorKind.BINARY,
}


static func kind_for_extension(ext: String) -> int:
	var key := ext.to_lower().strip_edges().trim_prefix(".")
	if EXTENSION_TO_KIND.has(key):
		return EXTENSION_TO_KIND[key]
	return EditorKind.UNSUPPORTED


static func kind_label(kind: int) -> String:
	match kind:
		EditorKind.TWODA:
			return "TwoDA"
		EditorKind.GFF:
			return "GFF"
		EditorKind.TLK:
			return "Talk Table"
		EditorKind.SSF:
			return "SSF"
		EditorKind.ERF:
			return "Archive"
		EditorKind.TEXT:
			return "Text"
		EditorKind.NCS:
			return "NCS"
		EditorKind.BINARY:
			return "Binary"
		_:
			return "Unsupported"
