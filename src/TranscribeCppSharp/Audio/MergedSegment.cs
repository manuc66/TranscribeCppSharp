#nullable enable

using System;

namespace TranscribeCppSharp.Audio;

/// <summary>A segment of the merged transcript, timed against the whole file.</summary>
/// <param name="Start">Start of the segment in the original file.</param>
/// <param name="End">End of the segment in the original file.</param>
/// <param name="Text">Transcribed text.</param>
/// <param name="SpeakerId">Speaker index, 0 when diarization did not run.</param>
public sealed record MergedSegment(TimeSpan Start, TimeSpan End, string Text, int SpeakerId);
