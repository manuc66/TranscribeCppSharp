#nullable enable

using System;
using System.Collections.Generic;
using TranscribeCppSharp.Interop;
using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Pure-managed tests for the result record types. These records mirror native
/// result accessors (transcribe_get_segment/_word/_token/_timings,
/// transcribe_get_backend_device, transcribe_stream_update/text), but they are
/// plain data holders: this test locks their positional semantics, value
/// equality, deconstruction, and with-expression behavior without needing the
/// native library.
/// </summary>
public class ResultRecordTests
{
    [Fact]
    public void SegmentResult_IsPositionalWithValueEquality()
    {
        var a = new SegmentResult(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "hello");
        var b = new SegmentResult(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "hello");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a == b);

        var (start, end, text) = a;
        Assert.Equal(TimeSpan.FromSeconds(1), start);
        Assert.Equal(TimeSpan.FromSeconds(2), end);
        Assert.Equal("hello", text);
    }

    [Fact]
    public void WordResult_IsPositionalWithValueEquality()
    {
        var a = new WordResult(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(900), "world");
        var b = new WordResult(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(900), "world");

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal("world", a.Text);
        Assert.Equal(TimeSpan.FromMilliseconds(500), a.Start);
    }

    [Fact]
    public void TokenResult_IsPositionalWithValueEquality()
    {
        var a = new TokenResult(42, 0.95f, TimeSpan.Zero, TimeSpan.FromMilliseconds(100), "token");
        var b = new TokenResult(42, 0.95f, TimeSpan.Zero, TimeSpan.FromMilliseconds(100), "token");

        Assert.Equal(a, b);
        Assert.Equal(42, a.Id);
        Assert.Equal(0.95f, a.Probability);
        Assert.Equal("token", a.Text);
    }

    [Fact]
    public void TimingsResult_IsPositionalWithValueEquality()
    {
        var a = new TimingsResult(10f, 20f, 30f, 40f);
        var b = new TimingsResult(10f, 20f, 30f, 40f);

        Assert.Equal(a, b);
        Assert.Equal(10f, a.LoadMs);
        Assert.Equal(20f, a.MelMs);
        Assert.Equal(30f, a.EncodeMs);
        Assert.Equal(40f, a.DecodeMs);
    }

    [Fact]
    public void BackendDevice_IsPositionalWithValueEquality()
    {
        var a = new TranscribeCppSharp.BackendDevice("cpu", "CPU", "cpu", "0", 1024UL, 512UL, DeviceType.DeviceTypeCpu);
        var b = new TranscribeCppSharp.BackendDevice("cpu", "CPU", "cpu", "0", 1024UL, 512UL, DeviceType.DeviceTypeCpu);

        Assert.Equal(a, b);
        Assert.Equal("cpu", a.Name);
        Assert.Equal("CPU", a.Description);
        Assert.Equal("cpu", a.Kind);
        Assert.Equal("0", a.DeviceId);
        Assert.Equal(1024UL, a.MemoryTotal);
        Assert.Equal(512UL, a.MemoryFree);
        Assert.Equal(DeviceType.DeviceTypeCpu, a.DeviceType);
    }

    [Fact]
    public void BatchResult_IsPositionalWithValueEquality()
    {
        var segment = new SegmentResult(TimeSpan.Zero, TimeSpan.FromSeconds(1), "seg");
        var word = new WordResult(TimeSpan.Zero, TimeSpan.FromSeconds(1), "word");
        var token = new TokenResult(1, 0.5f, TimeSpan.Zero, TimeSpan.FromSeconds(1), "tok");
        var timing = new TimingsResult(1f, 2f, 3f, 4f);
        IReadOnlyList<SegmentResult> segments = [segment];
        IReadOnlyList<WordResult> words = [word];
        IReadOnlyList<TokenResult> tokens = [token];

        var a = new BatchResult(0, "full", "en", Status.Ok, segments, words, tokens, timing);
        var b = new BatchResult(0, "full", "en", Status.Ok, segments, words, tokens, timing);

        Assert.Equal(a, b);
        Assert.Equal(0, a.Index);
        Assert.Equal("full", a.FullText);
        Assert.Equal("en", a.DetectedLanguage);
        Assert.Equal(Status.Ok, a.Status);
        Assert.Equal(segments, a.Segments);
        Assert.Equal(words, a.Words);
        Assert.Equal(tokens, a.Tokens);
        Assert.Equal(timing, a.Timing);
    }

    [Fact]
    public void StreamUpdateResult_IsPositionalWithValueEquality()
    {
        var a = new StreamUpdateResult(
            ResultChanged: true,
            IsFinal: false,
            Revision: 3,
            InputReceived: TimeSpan.FromSeconds(1),
            AudioCommitted: TimeSpan.FromSeconds(1),
            Buffered: TimeSpan.FromSeconds(2),
            CommittedChanged: true,
            TentativeChanged: false);
        var b = new StreamUpdateResult(
            ResultChanged: true,
            IsFinal: false,
            Revision: 3,
            InputReceived: TimeSpan.FromSeconds(1),
            AudioCommitted: TimeSpan.FromSeconds(1),
            Buffered: TimeSpan.FromSeconds(2),
            CommittedChanged: true,
            TentativeChanged: false);

        Assert.Equal(a, b);
        Assert.True(a.ResultChanged);
        Assert.False(a.IsFinal);
        Assert.Equal(3, a.Revision);
        Assert.Equal(TimeSpan.FromSeconds(1), a.InputReceived);
        Assert.Equal(TimeSpan.FromSeconds(2), a.Buffered);
        Assert.True(a.CommittedChanged);
        Assert.False(a.TentativeChanged);
    }

    [Fact]
    public void StreamTextResult_IsPositionalWithValueEquality()
    {
        var a = new StreamTextResult("full", "committed", "tentative");
        var b = new StreamTextResult("full", "committed", "tentative");

        Assert.Equal(a, b);
        Assert.Equal("full", a.FullText);
        Assert.Equal("committed", a.CommittedText);
        Assert.Equal("tentative", a.TentativeText);
    }

    [Fact]
    public void WithExpression_ProducesNewValueWithChangedMember()
    {
        var original = new StreamTextResult("full", "committed", "tentative");
        var updated = original with { TentativeText = "new tentative" };

        Assert.NotEqual(original, updated);
        Assert.Equal("full", updated.FullText);
        Assert.Equal("committed", updated.CommittedText);
        Assert.Equal("new tentative", updated.TentativeText);
    }

    [Fact]
    public void NullableTiming_IsStoredAsIs()
    {
        var withTiming = new BatchResult(0, "a", "en", Status.Ok, [], [], [], new TimingsResult(1, 2, 3, 4));
        var withoutTiming = new BatchResult(0, "a", "en", Status.Ok, [], [], [], null);

        Assert.NotNull(withTiming.Timing);
        Assert.Null(withoutTiming.Timing);
    }

    [Fact]
    public void TranscribeException_ExposesStatusCodeAndFailedMethod()
    {
        var ex = new TranscribeException(Status.ErrInvalidArg, "SomeNativeCall");

        Assert.Equal(Status.ErrInvalidArg, ex.StatusCode);
        Assert.Equal((int)Status.ErrInvalidArg, ex.ErrorCode);
        Assert.Equal("SomeNativeCall", ex.FailedMethod);
        Assert.Contains("SomeNativeCall", ex.Message);
        Assert.Contains("transcribe native error", ex.Message);
    }

    [Fact]
    public void TranscribeException_WithoutFailedMethod_HasEmptyMethodSuffix()
    {
        var ex = new TranscribeException(Status.Ok);

        Assert.Equal(Status.Ok, ex.StatusCode);
        Assert.Null(ex.FailedMethod);
        Assert.DoesNotContain(" in ", ex.Message);
    }
}
