namespace TranscribeCppSharp.SharedSource;

/// <summary>
/// How far a run may lower its window after the first one does not fit in memory.
/// </summary>
/// <remarks>
/// The native API reports a device's free memory but not the size of the compute
/// graph a window builds, so the only honest way to find a window that fits is to
/// try one. Halving keeps the number of attempts small and never goes below
/// <see cref="MinimumSeconds"/>: a shorter window would leave too little audio in
/// one call to be worth transcribing. The values are a policy, not a measurement;
/// the measurement is the run that succeeded.
/// </remarks>
public static class WindowAdjustment
{
    /// <summary>The smallest window an automatic retry will try.</summary>
    public const int MinimumSeconds = 5;

    /// <summary>How many values, including the first, an automatic retry may try.</summary>
    /// <remarks>
    /// Ten halvings reach the 5 s floor from the control's 3600 s maximum. The
    /// first failure is the expensive one; each retry is smaller and cheaper.
    /// </remarks>
    public const int MaximumAttempts = 10;

    /// <summary>
    /// The next window to try after <paramref name="seconds"/> failed, or null
    /// when there is no smaller value left to try.
    /// </summary>
    public static int? Smaller(int seconds)
    {
        if (seconds <= MinimumSeconds)
        {
            return null;
        }

        int next = Math.Max(MinimumSeconds, seconds / 2);
        return next < seconds ? next : null;
    }
}
