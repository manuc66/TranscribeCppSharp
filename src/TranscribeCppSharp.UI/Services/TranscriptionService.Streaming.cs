using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TranscribeCppSharp;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.SharedSource;
using TranscribeCppSharp.UI.Models;
using System.Runtime.CompilerServices;

namespace TranscribeCppSharp.UI.Services;

public partial class TranscriptionService
{
    public async IAsyncEnumerable<Models.StreamUpdate> StreamTranscribeAsync(
        string modelPath,
        StreamOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var model = LoadModel(modelPath, options.BackendRequest, options.Device);

        try
        {
            using var session = model.CreateSession();
            using var stream = session.CreateStream();
            using var microphone = new MicrophoneCapture();

            // One utterance may be closed and a new one begun every
            // options.UtteranceSeconds of audio; zero leaves a single utterance
            // for the whole session. See UtteranceRotation for why.
            var rotation = new UtteranceRotation(options.UtteranceSeconds);

            await System.Threading.Tasks.Task.Run(() => stream.Begin(null, streamConfig => Configure(streamConfig, options)), cancellationToken);

            microphone.Start();

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var samples = microphone.ReadAvailable();
                    if (samples.Length > 0)
                    {
                        await System.Threading.Tasks.Task.Run(() => stream.Feed(samples), cancellationToken);
                    }

                    var text = stream.GetCurrentText();
                    yield return Compose(rotation, text, isFinal: false);

                    if (samples.Length > 0 && rotation.AddFed(samples.Length))
                    {
                        await RotateUtterance(stream, rotation, options, cancellationToken);
                    }

                    await System.Threading.Tasks.Task.Delay(50, cancellationToken);
                }

                var finalText = stream.GetCurrentText();
                yield return Compose(rotation, finalText, isFinal: true);

                stream.Complete();
            }
            finally
            {
                microphone.Stop();
            }
        }
        finally
        {
            model.Dispose();
        }
    }

    /// <summary>
    /// Closes the current utterance and starts a new one on the same stream.
    /// </summary>
    /// <remarks>
    /// Finalize commits the utterance's remaining tentative text, reset clears
    /// the family's per-utterance state (the part that grows with the audio) and
    /// begin starts the next utterance reusing the stream's buffers. All three
    /// run off the UI thread, like <c>Feed</c>. The committed text is read
    /// between finalize and reset, while it is still there, and is then held by
    /// <paramref name="rotation"/> to prepend to the new utterance.
    /// </remarks>
    private static async System.Threading.Tasks.Task RotateUtterance(
        StreamSession stream,
        UtteranceRotation rotation,
        StreamOptions options,
        CancellationToken cancellationToken)
    {
        await System.Threading.Tasks.Task.Run(
            () =>
            {
                stream.Complete();
                rotation.Close(stream.GetCurrentText().CommittedText);
                stream.Reset();
                stream.Begin(null, streamConfig => Configure(streamConfig, options));
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// One update, with the text of the utterances already closed prepended so
    /// the display stays a single growing transcript across a rotation.
    /// </summary>
    private static Models.StreamUpdate Compose(
        UtteranceRotation rotation,
        StreamTextResult text,
        bool isFinal)
    {
        string closed = rotation.ClosedCommittedText;
        return new Models.StreamUpdate
        {
            FullText = closed + text.FullText,
            CommittedText = closed + text.CommittedText,
            TentativeText = text.TentativeText,
            IsFinal = isFinal,
        };
    }
}
