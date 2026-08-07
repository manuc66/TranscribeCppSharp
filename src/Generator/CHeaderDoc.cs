using System.Text;
using System.Text.RegularExpressions;

namespace TranscribeCppSharp.Generator;

/// <summary>
/// Extracts documentation from the upstream C header (ffi/c/transcribe.h).
/// Bindgen's transcribe_sys.rs strips all comments, so the C header is the
/// only authoritative source of human-readable semantics for the native API.
/// </summary>
public partial class CHeaderDoc
{
    private readonly string content;

    public CHeaderDoc(string content) => this.content = content;

    public static CHeaderDoc FromFile(string path) => new(File.ReadAllText(path));

    // ── Lookup API ─────────────────────────────────────────────────

    /// <summary>
    /// Returns the doc comment immediately preceding the declaration of a
    /// native function (TRANSCRIBE_API ... name(...);), or null when the
    /// function is not declared in this header.
    /// </summary>
    public string? GetFunctionDoc(string rustName)
    {
        var name = rustName.Trim();
        var match = FunctionDeclRegex().Matches(content).FirstOrDefault(m => NameOf(m) == name);
        return match is null ? null : GetPrecedingComment(match.Index);
    }

    /// <summary>
    /// Returns the doc comment immediately preceding a typedef/enum
    /// declaration (typedef enum { ... } name; or enum name { ... };).
    /// </summary>
    public string? GetEnumDoc(string rustName)
    {
        var name = rustName.Trim();
        var match = EnumDeclRegex().Matches(content).FirstOrDefault(m => NameOf(m) == name);
        return match is null ? null : GetPrecedingComment(match.Index);
    }

    /// <summary>
    /// Returns the doc comment immediately preceding a struct declaration
    /// (struct name { ... };), or null when the struct is not declared.
    /// </summary>
    public string? GetStructDoc(string rustName)
    {
        var name = rustName.Trim();
        var match = StructDeclRegex().Matches(content).FirstOrDefault(m => NameOf(m) == name);
        return match is null ? null : GetPrecedingComment(match.Index);
    }

    /// <summary>
    /// Returns the doc comment immediately preceding an enum value
    /// (TRANSCRIBE_... = N) inside the given enum body, or null when absent.
    /// </summary>
    public string? GetEnumValueDoc(string enumRustName, string valueRustName)
    {
        var body = GetEnumBody(enumRustName);
        if (body == null)
        {
            return null;
        }

        var bodyText = content.Substring(body.Value.Start, body.Value.Length);
        var baseIndex = body.Value.Start;
        var match = EnumValueRegex().Matches(bodyText).FirstOrDefault(m => NameOf(m) == valueRustName.Trim());
        return match is null ? null : GetPrecedingComment(baseIndex + match.Index);
    }

    /// <summary>
    /// Returns the doc comment immediately preceding a struct field, or null.
    /// Fields are matched by trailing name before the terminator.
    /// </summary>
    public string? GetStructFieldDoc(string structRustName, string fieldRustName)
    {
        var body = GetStructBody(structRustName);
        if (body == null)
        {
            return null;
        }

        var bodyText = content.Substring(body.Value.Start, body.Value.Length);
        var baseIndex = body.Value.Start;
        var match = StructFieldRegex().Matches(bodyText).FirstOrDefault(m => NameOf(m) == fieldRustName.Trim());
        return match is null ? null : GetPrecedingComment(baseIndex + match.Index);
    }

    // ── Body extraction ────────────────────────────────────────────
    private (int Start, int Length)? GetEnumBody(string rustName)
    {
        foreach (Match m in EnumDeclRegex().Matches(content))
        {
            if (NameOf(m) != rustName.Trim())
            {
                continue;
            }

            int open = content.IndexOf('{', m.Index);
            if (open < 0)
            {
                return null;
            }

            int close = FindBlockEnd(open);
            return (open, close - open + 1);
        }

        return null;
    }

    private (int Start, int Length)? GetStructBody(string rustName)
    {
        foreach (Match m in StructDeclRegex().Matches(content))
        {
            if (NameOf(m) != rustName.Trim())
            {
                continue;
            }

            int open = content.IndexOf('{', m.Index);
            if (open < 0)
            {
                return null;
            }

            int close = FindBlockEnd(open);
            return (open, close - open + 1);
        }

        return null;
    }

    private int FindBlockEnd(int openBraceIndex)
    {
        int depth = 0;
        for (int i = openBraceIndex; i < content.Length; i++)
        {
            if (content[i] == '{')
            {
                depth++;
            }
            else if (content[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return content.Length - 1;
    }

    // ── Comment extraction ─────────────────────────────────────────

    /// <summary>
    /// Returns the text of the block comment (/* ... */) immediately preceding
    /// the given position, with markers stripped and lines un-indented.
    /// Skips pure separator banners ("── ... ──").
    /// </summary>
    private string? GetPrecedingComment(int position)
    {
        int end = position;
        while (end > 0 && char.IsWhiteSpace(content[end - 1]))
        {
            end--;
        }

        if (end < 2 || content[end - 2] != '*' || content[end - 1] != '/')
        {
            return null;
        }

        int start = content.LastIndexOf("/*", end - 2, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        // Reject trailing comments ("code /* text */" on the same line): they
        // describe the field they follow, not the declaration we're looking for.
        int lineStart = content.LastIndexOf('\n', start) + 1;
        if (!string.IsNullOrWhiteSpace(content[lineStart..start]))
        {
            return null;
        }

        var raw = content[(start + 2)..(end - 2)];
        var text = StripCommentDecoration(raw);
        if (IsSeparatorComment(text))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string StripCommentDecoration(string raw)
    {
        var lines = raw.Split('\n');
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('*'))
            {
                trimmed = trimmed[1..];
            }

            sb.AppendLine(trimmed.TrimEnd());
        }

        return sb.ToString().TrimEnd();
    }

    private static bool IsSeparatorComment(string text)
    {
        var compact = text.Replace("-", string.Empty).Replace(" ", string.Empty).Replace("\n", string.Empty);
        return compact.Length == 0;
    }

    private static string NameOf(Match m)
    {
        var name = m.Groups["name"].Value;
        return string.IsNullOrEmpty(name) ? m.Groups["name2"].Value : name;
    }

    // ── Regex ──────────────────────────────────────────────────────
    [GeneratedRegex(@"TRANSCRIBE_API[^;{]*?\b(?<name>transcribe_\w+)\s*\(", RegexOptions.Singleline)]
    private static partial Regex FunctionDeclRegex();

    [GeneratedRegex(@"(?:typedef\s+enum\s*\{(?:[^{}]|\{[^{}]*\})*\}\s*(?<name>transcribe_\w+)\s*;|enum\s+(?<name2>transcribe_\w+)\s*\{)", RegexOptions.Singleline)]
    private static partial Regex EnumDeclRegex();

    [GeneratedRegex(@"struct\s+(?<name>transcribe_\w+)\s*\{", RegexOptions.Singleline)]
    private static partial Regex StructDeclRegex();

    [GeneratedRegex(@"\b(?<name>TRANSCRIBE_\w+)\s*=", RegexOptions.Singleline)]
    private static partial Regex EnumValueRegex();

    [GeneratedRegex(@"(?:const\s+char\s*\*\s*)?(?:const\s+)?(?<type>[\w\s_]+?)\s+(?<name>[\w]+)\s*(?:\[[^\]]*\])?\s*;", RegexOptions.Singleline)]
    private static partial Regex StructFieldRegex();
}
