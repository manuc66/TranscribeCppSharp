#nullable enable

using System;

namespace TranscribeCppSharp;

/// <summary>A transcribed text segment with timestamps.</summary>
public record SegmentResult(TimeSpan Start, TimeSpan End, string Text);
