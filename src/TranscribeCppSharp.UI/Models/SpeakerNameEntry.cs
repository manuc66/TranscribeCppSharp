namespace TranscribeCppSharp.UI.Models;

/// <summary>
/// A speaker id and the name the reader gave it. A blank name is not a name: the
/// export falls back to "Speaker N" rather than writing an empty label.
/// </summary>
/// <remarks>
/// A plain settable property, not an observable one: the controls write into it
/// and the export reads it, and nothing displays the value anywhere else. If a
/// second view ever has to follow an edit, this is where the notification goes.
/// </remarks>
public sealed class SpeakerNameEntry
{
    public SpeakerNameEntry(int speakerId, string name = "")
    {
        SpeakerId = speakerId;
        Name = name;
    }

    public int SpeakerId { get; }

    /// <summary>What the reader typed. Empty means "use Speaker N".</summary>
    public string Name { get; set; }

    /// <summary>The id as it appears in the results grid.</summary>
    public string Label => $"Speaker {SpeakerId}";
}
