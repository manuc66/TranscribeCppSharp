using System.Text;
using System.Text.RegularExpressions;

namespace TranscribeCppSharp.Generator;

/// <summary>
/// Parses transcribe_sys.rs (bindgen output) into structured declarations.
/// </summary>
public partial class RustFfiParser
{
    private readonly string _content;

    private static readonly HashSet<string> s_opaqueHandleNames =
    [
        "transcribe_model",
        "transcribe_session",
    ];

    private static readonly HashSet<string> s_primitiveNames =
    [
        "bool", "void", "i32", "u32", "i64", "u64", "usize", "f32", "f64",
        "c_int", "c_uint", "c_char", "c_void",
    ];

    public RustFfiParser(string content) => _content = content;

    public static RustFfiParser FromFile(string path) => new(File.ReadAllText(path));

    // ── Public parse methods ───────────────────────────────────────

    public List<RustEnum> ParseEnums()
    {
        var results = new List<RustEnum>();
        foreach (Match m in EnumStructRegex().Matches(_content))
        {
            var typeName = m.Groups["type"].Value;
            var body = m.Groups["body"].Value;
            var values = new List<RustEnumValue>();
            foreach (Match v in EnumValueRegex().Matches(body))
            {
                values.Add(new(v.Groups["name"].Value, v.Groups["value"].Value));
            }
            if (values.Count > 0)
                results.Add(new RustEnum(typeName, values));
        }
        return results;
    }

    public List<RustFunction> ParseFunctions()
    {
        var results = new List<RustFunction>();
        foreach (Match m in ExternFuncRegex().Matches(_content))
        {
            var name = m.Groups["name"].Value;
            var retTypeRaw = NormalizeType(m.Groups["ret"].Value.Trim());
            var paramsRaw = m.Groups["params"].Value.Trim();
            var retType = ParseRustType(retTypeRaw);
            var parameters = ParseParams(paramsRaw);
            results.Add(new RustFunction(name, retType, parameters));
        }
        return results;
    }

    public List<RustStruct> ParseStructs()
    {
        var results = new List<RustStruct>();
        foreach (Match m in NamedStructRegex().Matches(_content))
        {
            var name = m.Groups["name"].Value;
            var body = m.Groups["body"].Value;
            var fields = new List<RustStructField>();
            foreach (Match f in StructFieldRegex().Matches(body))
            {
                var fieldType = ParseRustType(NormalizeType(f.Groups["type"].Value.Trim()));
                var fieldName = f.Groups["name"].Value.Trim();
                fields.Add(new(fieldType, fieldName));
            }
            results.Add(new RustStruct(name, fields));
        }
        return results;
    }

    // ── Type parser ────────────────────────────────────────────────

    public static RustType ParseRustType(string raw)
    {
        raw = raw.Trim();

        if (string.IsNullOrEmpty(raw) || raw == "()" || raw == "void")
            return new VoidType();

        if (raw == "bool")
            return new BoolType();

        if (raw.StartsWith('*'))
        {
            var rest = raw[1..]; // "const X" or "mut X"
            if (rest.StartsWith("const "))
            {
                var inner = ParseRustType(rest["const ".Length..]);
                return new PointerType(PointerMutability.Const, inner);
            }
            if (rest.StartsWith("mut "))
            {
                var inner = ParseRustType(rest["mut ".Length..]);
                return new PointerType(PointerMutability.Mutable, inner);
            }
        }

        if (raw.StartsWith('[') && raw.Contains("u8"))
            return new SliceType(new PrimitiveType("u8"));

        if (s_primitiveNames.Contains(raw))
            return new PrimitiveType(raw);

        if (s_opaqueHandleNames.Contains(raw))
            return new OpaqueHandleType(raw);

        if (raw.StartsWith("transcribe_"))
            return new StructType(raw);

        return new UnknownType(raw);
    }

    // ── Param parser ───────────────────────────────────────────────

    private static List<RustParam> ParseParams(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "()")
            return [];

        var result = new List<RustParam>();
        var depth = 0;
        var current = new StringBuilder();
        foreach (var ch in raw)
        {
            if (ch is '(' or '<') depth++;
            else if (ch is ')' or '>') depth--;
            else if (ch == ',' && depth == 0)
            {
                ParseOneParam(current.ToString().Trim(), result);
                current.Clear();
                continue;
            }
            current.Append(ch);
        }
        if (current.Length > 0)
            ParseOneParam(current.ToString().Trim(), result);
        return result;
    }

    private static void ParseOneParam(string raw, List<RustParam> result)
    {
        var colonIdx = raw.IndexOf(':');
        if (colonIdx < 0) return;
        var typeStr = NormalizeType(raw[(colonIdx + 1)..].Trim());
        var name = raw[..colonIdx].Trim();
        var type = ParseRustType(typeStr);
        result.Add(new RustParam(type, name));
    }

    // ── Normalizer ─────────────────────────────────────────────────

    private static string NormalizeType(string rustType)
    {
        return rustType.Replace("::std::os::raw::", "");
    }

    // ── Regex ──────────────────────────────────────────────────────

    [GeneratedRegex(@"impl\s+(?<type>\w+)\s*\{(?<body>(?:\s*pub\s+const\s+\w+\s*:\s*\w+\s*=\s*\w+\([^)]*\);?\s*)+)\}")]
    private static partial Regex EnumStructRegex();

    [GeneratedRegex(@"pub\s+const\s+(?<name>\w+)\s*:\s*\w+\s*=\s*\w+\((?<value>[^)]*)\)")]
    private static partial Regex EnumValueRegex();

    [GeneratedRegex(@"pub\s+fn\s+(?<name>\w+)\s*\((?<params>[^)]*)\)\s*(?:->\s*(?<ret>[^;]+))?;", RegexOptions.Singleline)]
    private static partial Regex ExternFuncRegex();

    [GeneratedRegex(@"#\[repr\(C\)\].*?pub\s+struct\s+(?<name>\w+)\s*\{(?<body>[^}]+)\}", RegexOptions.Singleline)]
    private static partial Regex NamedStructRegex();

    [GeneratedRegex(@"pub\s+(?<name>\w+)\s*:\s*(?<type>[^,]+),?")]
    private static partial Regex StructFieldRegex();
}

// ── Data records ───────────────────────────────────────────────────

public record RustEnum(string TypeName, List<RustEnumValue> Values);
public record RustEnumValue(string Name, string Value);
public record RustFunction(string Name, RustType ReturnType, List<RustParam> Parameters);
public record RustStruct(string Name, List<RustStructField> Fields);
public record RustStructField(RustType Type, string Name);
public record RustParam(RustType Type, string Name);
