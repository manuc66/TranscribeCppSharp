#nullable enable

using TranscribeCppSharp.Cli;
using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Tests for the windowing math. The regression that matters: a window that is
/// not longer than the overlap leaves the loop unable to advance, which used to
/// hang the tool forever on the same window (reachable with --chunk 1).
/// </summary>
public class WindowPlannerTests
{
    private const int OneSecond = WindowPlanner.SampleRate; // 16 000 samples

    [Fact]
    public void TryPlan_WithAudioShorterThanTheWindow_ProducesOneWindow()
    {
        Assert.True(WindowPlanner.TryPlan(OneSecond, 300_000, out var windows, out var error));

        Assert.Null(error);
        var window = Assert.Single(windows);
        Assert.Equal(0, window.OffsetSamples);
        Assert.Equal(OneSecond, window.LengthSamples);
        Assert.Equal(0, window.StartMs);
    }

    [Fact]
    public void TryPlan_WithAudioExactlyOneWindowLong_ProducesOneWindow()
    {
        int samples = 300_000;

        Assert.True(WindowPlanner.TryPlan(samples, 300_000, out var windows, out _));

        var window = Assert.Single(windows);
        Assert.Equal(samples, window.LengthSamples);
    }

    [Fact]
    public void TryPlan_WithLongAudio_OverlapsByExactlyOneSecond()
    {
        // 5 s of audio in 2 s windows: 0, 1, 2, 3 -> the last window is the tail.
        Assert.True(WindowPlanner.TryPlan(5 * OneSecond, 2000, out var windows, out _));

        Assert.Equal([0, OneSecond, 2 * OneSecond, 3 * OneSecond], windows.Select(w => w.OffsetSamples));
        // Windows are 2 s long each; here the audio divides evenly, so the last
        // one is not a shorter tail.
        Assert.Equal([2, 2, 2, 2], windows.Select(w => w.LengthSamples / OneSecond));
    }

    [Fact]
    public void TryPlan_CoversEverySampleAndNeverStalls()
    {
        const int totalSamples = 7 * OneSecond + 4321;

        Assert.True(WindowPlanner.TryPlan(totalSamples, 2000, out var windows, out _));

        Assert.NotEmpty(windows);
        Assert.Equal(0, windows[0].OffsetSamples);
        // The union of the windows reaches the end of the audio.
        AudioWindow last = windows[^1];
        Assert.Equal(totalSamples, last.OffsetSamples + last.LengthSamples);
        // Each window starts after the previous one: no stall, no overlap backwards.
        for (int i = 1; i < windows.Count; i++)
        {
            Assert.True(windows[i].OffsetSamples > windows[i - 1].OffsetSamples);
        }
    }

    [Fact]
    public void StartMs_IsTheOffsetInMilliseconds()
    {
        Assert.True(WindowPlanner.TryPlan(10 * OneSecond, 2000, out var windows, out _));

        Assert.Equal([0L, 1000L, 2000L, 3000L, 4000L, 5000L, 6000L, 7000L, 8000L], windows.Select(w => w.StartMs));
    }

    [Theory]
    [InlineData(1000)] // --chunk 1: the window is exactly the overlap
    [InlineData(500)]
    [InlineData(0)]
    public void TryPlan_RejectsAWindowThatLeavesNoRoomForTheOverlap(int windowMs)
    {
        Assert.False(WindowPlanner.TryPlan(5 * OneSecond, windowMs, out var windows, out var error));

        Assert.Empty(windows);
        Assert.Contains("overlap", error);
        Assert.Contains("--chunk 2", error);
    }

    [Fact]
    public void TryPlan_RejectsAnEmptyAudioInsteadOfCallingTheNativeLayerWithNothing()
    {
        Assert.False(WindowPlanner.TryPlan(0, 300_000, out var windows, out var error));

        Assert.Empty(windows);
        Assert.Contains("empty", error);
    }

    [Fact]
    public void OverlapIsOneSecondAtSixteenKilohertz()
    {
        Assert.Equal(1000, WindowPlanner.OverlapMs);
        Assert.Equal(16_000, WindowPlanner.OverlapSamples);
        Assert.Equal(16_000, WindowPlanner.SampleRate);
    }
}
