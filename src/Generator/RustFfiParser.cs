using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TranscribeCppSharp.Generator;

/// <summary>
/// Parses transcribe_sys.rs (bindgen output) into structured declarations.
/// </summary>
public partial class RustFfiParser
{
    private readonly string content;

    private static readonly HashSet<string> OpaqueHandleNames =
    [
        "transcribe_model",
        "transcribe_session",
    ];

    private static readonly HashSet<string> PrimitiveNames =
    [
        "bool", "void", "i32", "u32", "i64", "u64", "usize", "f32", "f64",
        "c_int", "c_uint", "c_char", "c_void",
    ];

    private const string ValueGroup = "value";

    public RustFfiParser(string content) => this.content = content;

    public static RustFfiParser FromFile(string path) => new(File.ReadAllText(path));

    // ── Public parse methods ───────────────────────────────────────
    public List<RustEnumDecl> ParseEnums()
    {
        return EnumStructRegex().Matches(content)
            .Select(m => new
            {
                TypeName = m.Groups["type"].Value,
                Values = EnumValueRegex().Matches(m.Groups["body"].Value)
                    .Select(v => new RustEnumValue(v.Groups["name"].Value, v.Groups[ValueGroup].Value))
                    .ToList(),
            })
            .Where(e => e.Values.Count > 0)
            .Select(e => new RustEnumDecl(e.TypeName, e.Values))
            .ToList();
    }

    public List<RustFunction> ParseFunctions()
    {
        return ExternFuncRegex().Matches(content)
            .Select(m => new RustFunction(
                m.Groups["name"].Value,
                ParseRustType(NormalizeType(m.Groups["ret"].Value.Trim())),
                ParseParams(m.Groups["params"].Value.Trim())))
            .ToList();
    }

    public List<RustStruct> ParseStructs()
    {
        return NamedStructRegex().Matches(content)
            .Select(m => new RustStruct(
                m.Groups["name"].Value,
                StructFieldRegex().Matches(m.Groups["body"].Value)
                    .Select(f => new RustStructField(
                        ParseRustType(NormalizeType(f.Groups["type"].Value.Trim())),
                        f.Groups["name"].Value.Trim()))
                    .ToList()))
            .ToList();
    }

    /// <summary>
    /// Parse the compile-time layout checks bindgen embeds in transcribe_sys.rs
    /// (const _: () = { ["Size of X"][size_of::&lt;X&gt;() - N]; ... }). These constants are
    /// verified by the Rust compiler against the real repr(C) layout, so they are the
    /// ground truth for the C# struct declarations (total size, alignment, field offsets).
    /// </summary>
    public List<RustStructLayout> ParseAbiLayouts()
    {
        var results = new List<RustStructLayout>();
        foreach (Match m in AbiLayoutBlockRegex().Matches(content))
        {
            var body = m.Groups["body"].Value;

            var sizeMatch = AbiLayoutSizeRegex().Match(body);
            if (!sizeMatch.Success)
            {
                continue; // not a layout block
            }

            var name = sizeMatch.Groups["name"].Value;
            var size = ulong.Parse(sizeMatch.Groups[ValueGroup].Value, CultureInfo.InvariantCulture);

            var alignMatch = AbiLayoutAlignRegex().Match(body);
            var align = alignMatch.Success ? ulong.Parse(alignMatch.Groups[ValueGroup].Value, CultureInfo.InvariantCulture) : 0UL;

            var fields = AbiLayoutOffsetRegex().Matches(body)
                .Select(f => new RustStructLayoutField(
                    f.Groups["field"].Value,
                    ulong.Parse(f.Groups[ValueGroup].Value, CultureInfo.InvariantCulture)))
                .ToList();

            results.Add(new RustStructLayout(name, size, align, fields));
        }

        return results;
    }

    // ── Type parser ────────────────────────────────────────────────
    public static RustType ParseRustType(string raw)
    {
        raw = raw.Trim();

        if (string.IsNullOrEmpty(raw) || raw == "()" || raw == "void")
        {
            return new VoidType();
        }

        if (raw == "bool")
        {
            return new BoolType();
        }

        if (raw.StartsWith('*'))
        {
            var rest = raw[1..]; // "const X" or "mut X"
            if (rest.StartsWith("const ", StringComparison.Ordinal))
            {
                var inner = ParseRustType(rest["const ".Length..]);
                return new PointerType(PointerMutability.Const, inner);
            }

            if (rest.StartsWith("mut ", StringComparison.Ordinal))
            {
                var inner = ParseRustType(rest["mut ".Length..]);
                return new PointerType(PointerMutability.Mutable, inner);
            }
        }

        if (raw.StartsWith('[') && raw.Contains("u8"))
        {
            return new SliceType(new PrimitiveType("u8"));
        }

        if (PrimitiveNames.Contains(raw))
        {
            return new PrimitiveType(raw);
        }

        if (OpaqueHandleNames.Contains(raw))
        {
            return new OpaqueHandleType(raw);
        }

        if (raw.StartsWith("transcribe_", StringComparison.Ordinal))
        {
            return new StructType(raw);
        }

        return new UnknownType(raw);
    }

    // ── Param parser ───────────────────────────────────────────────
    private static List<RustParam> ParseParams(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "()")
        {
            return [];
        }

        var result = new List<RustParam>();
        var depth = 0;
        var current = new StringBuilder();
        foreach (var ch in raw)
        {
            if (ch is '(' or '<')
            {
                depth++;
            }
            else if (ch is ')' or '>')
            {
                depth--;
            }
            else if (ch == ',' && depth == 0)
            {
                ParseOneParam(current.ToString().Trim(), result);
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            ParseOneParam(current.ToString().Trim(), result);
        }

        return result;
    }

    private static void ParseOneParam(string raw, List<RustParam> result)
    {
        var colonIdx = raw.IndexOf(':');
        if (colonIdx < 0)
        {
            return;
        }

        var typeStr = NormalizeType(raw[(colonIdx + 1)..].Trim());
        var name = raw[..colonIdx].Trim();
        var type = ParseRustType(typeStr);
        result.Add(new RustParam(type, name));
    }

    // ── Normalizer ─────────────────────────────────────────────────
    private static string NormalizeType(string rustType)
    {
        return rustType.Replace("::std::os::raw::", string.Empty);
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

    [GeneratedRegex(@"const _: \(\) = \{(?<body>[^}]*)\};", RegexOptions.Singleline)]
    private static partial Regex AbiLayoutBlockRegex();

    [GeneratedRegex(@"\[""Size of (?<name>\w+)""\]\s*\[[^\]]*\s*-\s*(?<value>\d+)usize\]")]
    private static partial Regex AbiLayoutSizeRegex();

    [GeneratedRegex(@"\[""Alignment of (?<name>\w+)""\]\s*\[[^\]]*\s*-\s*(?<value>\d+)usize\]")]
    private static partial Regex AbiLayoutAlignRegex();

    [GeneratedRegex(@"\[""Offset of field: \w+::(?<field>\w+)""\]\s*\[[^\]]*\s*-\s*(?<value>\d+)usize\]")]
    private static partial Regex AbiLayoutOffsetRegex();

    [GeneratedRegex(@"pub\s+type\s+(?<name>\w+)\s*=\s*::std::option::Option<")]
    private static partial Regex CallbackTypeAliasRegex();

    /// <summary>
    /// Parse callback type aliases (e.g. <c>pub type transcribe_log_callback = Option&lt;...&gt;</c>).
    /// Used by the generator to verify all callbacks have hand-written delegates.
    /// </summary>
    public List<string> ParseCallbackTypeAliases()
    {
        var results = new List<string>();
        foreach (Match m in CallbackTypeAliasRegex().Matches(content))
        {
            results.Add(m.Groups["name"].Value);
        }

        return results;
    }
}
