namespace TranscribeCppSharp.Audio;

/// <summary>One window of audio to transcribe, in samples at <see cref="WindowPlanner.SampleRate"/>.</summary>
public readonly record struct AudioWindow(int OffsetSamples, int LengthSamples)
{
    /// <summary>Start of the window in the original timeline, in milliseconds.</summary>
    public long StartMs => (long)OffsetSamples * 1000 / WindowPlanner.SampleRate;
}
