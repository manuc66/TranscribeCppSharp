using System.Text.RegularExpressions;

namespace TranscribeCppSharp.Generator;

/// <summary>
/// Parses transcribe_sys.rs (bindgen output) into structured declarations.
/// </summary>
public partial class RustFfiParser
{
    private readonly string _content;

    public RustFfiParser(string content) => _content = content;

    public static RustFfiParser FromFile(string path) => new(File.ReadAllText(path));

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
            var retType = NormalizeType(m.Groups["ret"].Value.Trim());
            var paramsRaw = NormalizeType(m.Groups["params"].Value.Trim());
            results.Add(new RustFunction(name, retType, paramsRaw));
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
                fields.Add(new(NormalizeType(f.Groups["type"].Value.Trim()), f.Groups["name"].Value.Trim()));
            }
            results.Add(new RustStruct(name, fields));
        }
        return results;
    }

    private static string NormalizeType(string rustType)
    {
        return rustType.Replace("::std::os::raw::", "");
    }

    // Match: impl TypeName { pub const NAME: TypeName = TypeName(N); ... }
    [GeneratedRegex(@"impl\s+(?<type>\w+)\s*\{(?<body>(?:\s*pub\s+const\s+\w+\s*:\s*\w+\s*=\s*\w+\([^)]*\);?\s*)+)\}")]
    private static partial Regex EnumStructRegex();

    [GeneratedRegex(@"pub\s+const\s+(?<name>\w+)\s*:\s*\w+\s*=\s*\w+\((?<value>[^)]*)\)")]
    private static partial Regex EnumValueRegex();

    // Match: pub fn name(params) -> ret;
    [GeneratedRegex(@"pub\s+fn\s+(?<name>\w+)\s*\((?<params>[^)]*)\)\s*(?:->\s*(?<ret>[^;]+))?;", RegexOptions.Singleline)]
    private static partial Regex ExternFuncRegex();

    // Match named structs: pub struct name { ... }
    [GeneratedRegex(@"#\[repr\(C\)\].*?pub\s+struct\s+(?<name>\w+)\s*\{(?<body>[^}]+)\}", RegexOptions.Singleline)]
    private static partial Regex NamedStructRegex();

    // Match struct fields: pub field_name: type,
    [GeneratedRegex(@"pub\s+(?<name>\w+)\s*:\s*(?<type>[^,]+),?")]
    private static partial Regex StructFieldRegex();
}

public record RustEnum(string TypeName, List<RustEnumValue> Values);
public record RustEnumValue(string Name, string Value);
public record RustFunction(string Name, string ReturnType, string ParamsRaw);
public record RustStruct(string Name, List<RustStructField> Fields);
public record RustStructField(string Type, string Name);
