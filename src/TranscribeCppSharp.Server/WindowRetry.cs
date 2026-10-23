namespace TranscribeCppSharp.Server;

using TranscribeCppSharp.Interop;
using TranscribeCppSharp.SharedSource;

/// <summary>
/// Retries a transcription at a smaller window when the native layer reports
/// that one did not fit in memory.
/// </summary>
/// <remarks>
/// <para>
/// The native API reports a device's free memory but not the size of the compute
/// graph a window builds, so the only honest way to find a window that fits is to
/// try one. <see cref="WindowAdjustment"/> owns the halving policy, shared with
/// the desktop app.
/// </para>
/// <para>
/// Unlike the GUI, the server retries even when a later window failed: the
/// attempt's merged transcript is discarded and the file restarts from the first
/// sample, so there is nothing to duplicate. The cost is re-transcribing the
/// windows that had already succeeded, and the log says the file restarted.
/// </para>
/// </remarks>
internal static class WindowRetry
{
    /// <summary>
    /// Calls <paramref name="attempt"/> with a window in seconds, halving it on
    /// <see cref="Status.ErrOom"/> until it fits, the floor is reached, or the
    /// attempt budget is spent.
    /// </summary>
    /// <param name="initialSeconds">The window the caller asked for.</param>
    /// <param name="attempt">Runs the file at that window size.</param>
    /// <param name="onRetry">Told the failed and the next window, for the log.</param>
    internal static T Run<T>(int initialSeconds, Func<int, T> attempt, Action<int, int>? onRetry = null)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        int seconds = initialSeconds;
        int count = 1;

        while (true)
        {
            try
            {
                return attempt(seconds);
            }
            catch (TranscribeException ex) when (
                ex.StatusCode == Status.ErrOom
                && count < WindowAdjustment.MaximumAttempts
                && WindowAdjustment.Smaller(seconds) is int smaller)
            {
                onRetry?.Invoke(seconds, smaller);
                seconds = smaller;
                count++;
            }
        }
    }
}
