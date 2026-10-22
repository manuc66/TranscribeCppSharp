namespace TranscribeCppSharp.Interop.Tests;

using TranscribeCppSharp.Audio;
using TranscribeCppSharp.SharedSource;
using Xunit;

/// <summary>
/// The speech-to-window planning used by <c>--vad</c>. Pure arithmetic: no
/// model, no native call, so it runs everywhere.
/// </summary>
public sealed class SpeechWindowsTests
{
    private const int Second = WindowPlanner.SampleRate;

    // Padding and merging are exercised with their defaults unless a test says
    // otherwise; these keep the expected numbers readable.
    private static IReadOnlyList<AudioWindow> Plan(
        (long Start, long End)[] intervals,
        long totalSamples,
        int maxWindowMs = 300_000,
        int paddingMs = 0,
        int mergeGapMs = 500,
        int minSpeechMs = 250)
        => SpeechWindows.Plan(
            intervals.Select(i => new SpeechInterval(i.Start, i.End)).ToArray(),
            totalSamples,
            maxWindowMs,
            paddingMs,
            mergeGapMs,
            minSpeechMs);

    [Fact]
    public void One_interval_becomes_one_window()
    {
        IReadOnlyList<AudioWindow> windows = Plan([(Second, 2 * Second)], 10 * Second);

        AudioWindow window = Assert.Single(windows);
        Assert.Equal(Second, window.OffsetSamples);
        Assert.Equal(Second, window.LengthSamples);
    }

    [Fact]
    public void Padding_is_added_on_both_sides()
    {
        IReadOnlyList<AudioWindow> windows = Plan([(2 * Second, 3 * Second)], 10 * Second, paddingMs: 200);

        AudioWindow window = Assert.Single(windows);
        Assert.Equal(2 * Second - 3200, window.OffsetSamples);
        Assert.Equal(Second + 6400, window.LengthSamples);
    }

    [Fact]
    public void Padding_does_not_go_below_zero()
    {
        IReadOnlyList<AudioWindow> windows = Plan([(1000, 2 * Second)], 10 * Second, paddingMs: 200);

        Assert.Equal(0, Assert.Single(windows).OffsetSamples);
    }

    [Fact]
    public void Intervals_closer_than_the_merge_gap_become_one_window()
    {
        IReadOnlyList<AudioWindow> windows = Plan(
            [(Second, 2 * Second), (2 * Second + 4000, 3 * Second)],
            10 * Second,
            mergeGapMs: 500);

        AudioWindow window = Assert.Single(windows);
        Assert.Equal(Second, window.OffsetSamples);
        Assert.Equal(2 * Second, window.LengthSamples);
    }

    [Fact]
    public void Intervals_farther_apart_than_the_merge_gap_stay_separate()
    {
        IReadOnlyList<AudioWindow> windows = Plan(
            [(Second, 2 * Second), (4 * Second, 5 * Second)],
            10 * Second,
            mergeGapMs: 500);

        Assert.Equal(2, windows.Count);
        Assert.Equal(Second, windows[0].OffsetSamples);
        Assert.Equal(4 * Second, windows[1].OffsetSamples);
    }

    [Fact]
    public void Intervals_shorter_than_the_minimum_are_dropped_before_padding()
    {
        // 1000 samples (62 ms) of speech, with 200 ms of padding on each side:
        // the padded length would pass the 250 ms minimum, the speech did not.
        IReadOnlyList<AudioWindow> windows = Plan(
            [(2 * Second, 2 * Second + 1000)],
            10 * Second,
            paddingMs: 200,
            minSpeechMs: 250);

        Assert.Empty(windows);
    }

    [Fact]
    public void An_interval_longer_than_the_window_is_split_with_the_asr_overlap()
    {
        IReadOnlyList<AudioWindow> windows = Plan([(0, 3 * Second)], 3 * Second, maxWindowMs: 2000);

        Assert.Equal(2, windows.Count);
        Assert.Equal(0, windows[0].OffsetSamples);
        Assert.Equal(2 * Second, windows[0].LengthSamples);
        Assert.Equal(Second, windows[1].OffsetSamples);
        Assert.Equal(2 * Second, windows[1].LengthSamples);
        Assert.Equal(
            windows[0].OffsetSamples + windows[0].LengthSamples - WindowPlanner.OverlapSamples,
            windows[1].OffsetSamples);
    }

    [Fact]
    public void Intervals_are_clamped_to_the_file()
    {
        IReadOnlyList<AudioWindow> windows = Plan([(Second, 20 * Second)], 10 * Second);

        AudioWindow window = Assert.Single(windows);
        Assert.Equal(Second, window.OffsetSamples);
        Assert.Equal(9 * Second, window.LengthSamples);
    }

    [Fact]
    public void Unsorted_input_is_sorted_by_start()
    {
        IReadOnlyList<AudioWindow> windows = Plan(
            [(5 * Second, 6 * Second), (Second, 2 * Second)],
            10 * Second);

        Assert.Equal(new[] { Second, 5 * Second }, windows.Select(w => w.OffsetSamples).ToArray());
    }

    [Fact]
    public void No_speech_plans_no_windows()
    {
        Assert.Empty(Plan([], 10 * Second));
    }

    [Fact]
    public void Intervals_outside_the_file_are_ignored()
    {
        Assert.Empty(Plan([(20 * Second, 25 * Second)], 10 * Second));
    }

    [Fact]
    public void A_window_that_cannot_hold_the_overlap_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Plan([(Second, 2 * Second)], 10 * Second, maxWindowMs: WindowPlanner.OverlapMs));
    }

    [Fact]
    public void Window_starts_and_lengths_never_exceed_the_interval()
    {
        IReadOnlyList<AudioWindow> windows = Plan(
            [(Second, 2 * Second), (4 * Second, 9 * Second)],
            10 * Second,
            maxWindowMs: 3000,
            paddingMs: 100);

        foreach (AudioWindow window in windows)
        {
            Assert.True(window.LengthSamples > 0);
            Assert.True(window.OffsetSamples + window.LengthSamples <= 10 * Second);
        }

        Assert.Equal(windows.OrderBy(w => w.OffsetSamples).Select(w => w.OffsetSamples), windows.Select(w => w.OffsetSamples));
    }
}
