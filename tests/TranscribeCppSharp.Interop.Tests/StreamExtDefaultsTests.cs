using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// The value each streaming extension's native <c>*_ext_init</c> sets.
/// </summary>
/// <remarks>
/// The defaults are sentinels, not settings: -1 means "use the model's or the
/// family's default", which is also what omitting the extension asks for. They are
/// read from the initialized structs rather than written here, so this pins what
/// upstream actually ships — and would catch the day one of them stops being -1,
/// which would make the UI's "(model default)" wording wrong.
/// </remarks>
public class StreamExtDefaultsTests
{
    [Fact]
    public void MoonshineDecodeInterval_DefaultsToTheFamilyCadence()
    {
        Assert.Equal(-1, MoonshineExtBuilder.DefaultMinDecodeIntervalMs);
    }

    [Fact]
    public void ParakeetAttContextRight_DefaultsToTheModelsOwnEntry()
    {
        Assert.Equal(-1, ParakeetStreamExtBuilder.DefaultAttContextRight);
    }

    [Fact]
    public void ParakeetBufferedContexts_DefaultPerField()
    {
        Assert.Equal(-1, ParakeetBufferedStreamExtBuilder.DefaultLeftMs);
        Assert.Equal(-1, ParakeetBufferedStreamExtBuilder.DefaultChunkMs);
        Assert.Equal(-1, ParakeetBufferedStreamExtBuilder.DefaultRightMs);
    }

    [Fact]
    public void VoxtralKnobs_DefaultToTheModelAndFamilyDefaults()
    {
        Assert.Equal(-1, VoxtralExtBuilder.DefaultNumDelayTokens);
        Assert.Equal(-1, VoxtralExtBuilder.DefaultMinDecodeIntervalMs);
    }
}
