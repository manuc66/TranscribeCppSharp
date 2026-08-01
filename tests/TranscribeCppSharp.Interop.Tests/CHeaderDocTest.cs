using System.IO;
using TranscribeCppSharp.Generator;
using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

public class CHeaderDocTest
{
    private static readonly CHeaderDoc Doc =
        CHeaderDoc.FromFile(Path.Combine(TestConfig.RepoRoot, "c", "transcribe.h"));

    [Fact]
    public void FunctionDoc_IsFound()
    {
        var doc = Doc.GetFunctionDoc("transcribe_run");
        Assert.NotNull(doc);
        Assert.Contains("Run one batch transcription.", doc);
    }

    [Fact]
    public void FunctionDoc_UnknownReturnsNull()
    {
        Assert.Null(Doc.GetFunctionDoc("transcribe_does_not_exist"));
    }

    [Fact]
    public void EnumDoc_IsFound()
    {
        var doc = Doc.GetEnumDoc("transcribe_log_level");
        Assert.NotNull(doc);
        Assert.Contains("source of truth", doc);
    }

    [Fact]
    public void EnumDoc_UndocumentedEnumReturnsNull()
    {
        // transcribe_status has no dedicated doc block in the header (only a
        // section separator banner), so the parser must return null.
        Assert.Null(Doc.GetEnumDoc("transcribe_status"));
    }

    [Fact]
    public void EnumValueDoc_IsFound()
    {
        var doc = Doc.GetEnumValueDoc("transcribe_status", "TRANSCRIBE_ERR_ABORTED");
        Assert.NotNull(doc);
        Assert.Contains("abort", doc, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnumValueDoc_UndocumentedValueReturnsNull()
    {
        // log_level values carry no per-value comments in the header (CONT has
        // a trailing, not preceding, comment); the parser must return null.
        Assert.Null(Doc.GetEnumValueDoc("transcribe_log_level", "TRANSCRIBE_LOG_LEVEL_DEBUG"));
    }

    [Fact]
    public void StructDoc_IsFound()
    {
        var doc = Doc.GetStructDoc("transcribe_model_load_params");
        Assert.NotNull(doc);
        Assert.Contains("model", doc);
    }

    [Fact]
    public void StructFieldDoc_UndocumentedFieldReturnsNull()
    {
        // No struct field carries a preceding comment block in this header;
        // docs live at struct level. The parser must return null rather than
        // inventing per-field docs.
        Assert.Null(Doc.GetStructFieldDoc("transcribe_backend_device", "name"));
    }
}
