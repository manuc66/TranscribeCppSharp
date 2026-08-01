#nullable enable

using System;

namespace TranscribeCppSharp;

/// <summary>A transcribed word with timestamps.</summary>
public record WordResult(TimeSpan Start, TimeSpan End, string Text);
