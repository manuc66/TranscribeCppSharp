#nullable enable

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// A cancellation token that fires a fixed delay after construction, cancelled
/// from a dedicated <see cref="Thread"/> instead of a
/// <see cref="CancellationTokenSource"/> timer.
/// </summary>
/// <remarks>
/// The three mid-run abort tests used
/// <c>new CancellationTokenSource(TimeSpan.FromMilliseconds(2))</c>. That timer
/// queues its callback on the thread pool, which the parallel test host can
/// starve: the callback then runs only after the native run has returned, the
/// token is never observed as cancelled, and the test failed with "no exception
/// was thrown" — intermittently, and on a different one of the three each run.
/// It is not the run finishing first: measured here, langid takes ~0.6 s, VAD
/// ~1.8 s and diarization ~28 s, so a 2 ms cancellation has ample margin. A
/// dedicated thread is scheduled by the OS and is not starved by the thread
/// pool, which is the point.
/// </remarks>
internal sealed class MidRunCancellation : IDisposable
{
    private readonly CancellationTokenSource cts = new();
    private readonly Thread thread;

    public MidRunCancellation(TimeSpan after)
    {
        thread = new Thread(() =>
        {
            Thread.Sleep(after);
            cts.Cancel();
        })
        {
            IsBackground = true,
            Name = "mid-run-cancel",
        };
        thread.Start();
    }

    public CancellationToken Token => cts.Token;

    public void Dispose()
    {
        thread.Join();
        cts.Dispose();
    }
}
