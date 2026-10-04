using System;

namespace TranscribeCppSharp.Audio;

/// <summary>
/// The input audio could not be read. Carries a message meant for the user.
/// </summary>
public sealed class AudioLoadException : Exception
{
    /// <summary>Creates the exception with a message meant for the user.</summary>
    public AudioLoadException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with the underlying cause attached.</summary>
    public AudioLoadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
