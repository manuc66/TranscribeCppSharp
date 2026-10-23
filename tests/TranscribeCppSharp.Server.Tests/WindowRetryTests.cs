namespace TranscribeCppSharp.Server.Tests;

using TranscribeCppSharp.Interop;
using TranscribeCppSharp.SharedSource;
using Xunit;

/// <summary>
/// The retry policy around a native out-of-memory failure. No model and no
/// native call: the attempts are faked and only the window sequence is asserted.
/// </summary>
public sealed class WindowRetryTests
{
    private static readonly int[] OneAttempt = [300];
    private static readonly int[] Halving = [300, 150, 75];
    private static readonly (int Failed, int Next)[] TwoRetries = [(300, 150), (150, 75)];

    [Fact]
    public void The_first_attempt_is_returned_without_a_retry()
    {
        var seen = new List<int>();

        int result = WindowRetry.Run(300, seconds =>
        {
            seen.Add(seconds);
            return seconds;
        });

        Assert.Equal(300, result);
        Assert.Equal(OneAttempt, seen);
    }

    [Fact]
    public void An_out_of_memory_failure_halves_the_window_and_reports_it()
    {
        var seen = new List<int>();
        var retries = new List<(int Failed, int Next)>();

        int result = WindowRetry.Run(
            300,
            seconds =>
            {
                seen.Add(seconds);
                return seconds > 75 ? throw new TranscribeException(Status.ErrOom) : seconds;
            },
            (failed, next) => retries.Add((failed, next)));

        Assert.Equal(75, result);
        Assert.Equal(Halving, seen);
        Assert.Equal(TwoRetries, retries);
    }

    [Fact]
    public void Another_native_status_is_not_retried()
    {
        int calls = 0;

        Assert.Throws<TranscribeException>(() => WindowRetry.Run<int>(300, _ =>
        {
            calls++;
            throw new TranscribeException(Status.ErrGguf);
        }));

        Assert.Equal(1, calls);
    }

    [Fact]
    public void The_floor_is_not_tried_below()
    {
        int calls = 0;

        Assert.Throws<TranscribeException>(() => WindowRetry.Run<int>(WindowAdjustment.MinimumSeconds, _ =>
        {
            calls++;
            throw new TranscribeException(Status.ErrOom);
        }));

        Assert.Equal(1, calls);
    }

    [Fact]
    public void The_attempt_budget_bounds_the_retries()
    {
        int calls = 0;

        Assert.Throws<TranscribeException>(() => WindowRetry.Run<int>(3600, _ =>
        {
            calls++;
            throw new TranscribeException(Status.ErrOom);
        }));

        Assert.Equal(WindowAdjustment.MaximumAttempts, calls);
    }

    [Fact]
    public void A_cancelled_attempt_is_not_retried()
    {
        int calls = 0;

        Assert.Throws<OperationCanceledException>(() => WindowRetry.Run<int>(300, _ =>
        {
            calls++;
            throw new OperationCanceledException();
        }));

        Assert.Equal(1, calls);
    }
}
