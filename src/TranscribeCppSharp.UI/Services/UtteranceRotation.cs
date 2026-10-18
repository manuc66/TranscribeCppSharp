using System.Text;
using TranscribeCppSharp.Audio;

namespace TranscribeCppSharp.UI.Services;

/// <summary>
/// Splits a long live stream into utterances of bounded length, and keeps the
/// committed text of the utterances closed so far.
/// </summary>
/// <remarks>
/// The native stream works on one utterance at a time and keeps that
/// utterance's state; for a family that re-decodes the stream from the start on
/// every feed (moonshine_streaming) its working set grows with the utterance's
/// audio. The streaming API exposes no context cap the wrapper can set — the
/// family reports <c>effective_n_ctx = 0</c> and no KV cache — so a microphone
/// fed forever grows the process without bound. Measured on one machine and
/// model at roughly 2.8 MiB per second of audio
/// (<c>docs/streaming-bench.md</c>).
/// <para>
/// Closing the utterance (finalize, reset, begin) lets the stream reuse its
/// buffers, so the working set is bounded by the longest utterance rather than
/// by the whole session. The cost is the model's audio context across the
/// boundary; the text is not lost, because what the native stream had committed
/// is accumulated here and prepended to the next utterance's text.
/// </para>
/// <para>
/// This is the decision half only: it counts fed samples and holds the running
/// text. The native calls that actually close an utterance live in
/// <see cref="TranscriptionService"/>, where the stream is.
/// </para>
/// </remarks>
internal sealed class UtteranceRotation
{
    private readonly int _samplesPerUtterance;
    private readonly StringBuilder _closed = new();
    private string _closedText = string.Empty;
    private int _fed;

    /// <summary>
    /// Creates a rotation of <paramref name="utteranceSeconds"/> seconds per
    /// utterance. Zero or less disables it: one utterance lasts the whole
    /// session, which is what the tab did before this existed.
    /// </summary>
    public UtteranceRotation(int utteranceSeconds)
        => _samplesPerUtterance = utteranceSeconds > 0
            ? utteranceSeconds * WindowPlanner.SampleRate
            : 0;

    /// <summary>Whether an utterance is ever closed. False keeps the old behaviour.</summary>
    public bool IsEnabled => _samplesPerUtterance > 0;

    /// <summary>
    /// The committed text of every utterance closed so far, to prepend to the
    /// current utterance's text so the display stays one growing transcript.
    /// Cached rather than rebuilt on read: it is read on every stream update,
    /// while it changes only when an utterance closes.
    /// </summary>
    public string ClosedCommittedText => _closedText;

    /// <summary>
    /// Counts <paramref name="samples"/> fed into the current utterance and
    /// reports whether it has reached its length and should be closed.
    /// </summary>
    /// <remarks>
    /// The surplus is carried over rather than dropped, so a feed larger than
    /// the remaining space does not shift later boundaries.
    /// </remarks>
    public bool AddFed(int samples)
    {
        if (!IsEnabled || samples <= 0)
        {
            return false;
        }

        _fed += samples;
        if (_fed < _samplesPerUtterance)
        {
            return false;
        }

        _fed -= _samplesPerUtterance;
        return true;
    }

    /// <summary>
    /// Appends the committed text of an utterance that has just been closed.
    /// </summary>
    /// <remarks>
    /// A single space joins two non-empty chunks when neither side already has
    /// whitespace at the seam, so a boundary cannot glue the last word of one
    /// utterance to the first word of the next. The native committed text is
    /// otherwise passed through unchanged.
    /// </remarks>
    public void Close(string committedText)
    {
        if (string.IsNullOrEmpty(committedText))
        {
            return;
        }

        if (_closed.Length > 0
            && !char.IsWhiteSpace(_closed[^1])
            && !char.IsWhiteSpace(committedText[0]))
        {
            _closed.Append(' ');
        }

        _closed.Append(committedText);
        _closedText = _closed.ToString();
    }
}
