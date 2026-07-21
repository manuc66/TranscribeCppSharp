#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp;

/// <summary>
/// Result of a single item in a batch transcription.
/// </summary>
public record BatchResult(
    int Index,
    string FullText,
    string DetectedLanguage,
    Status Status);

/// <summary>
/// Batch transcription API. Process multiple audio clips in a single call.
/// </summary>
public static class Batch
{
    /// <summary>
    /// Transcribe multiple PCM buffers in a single batch.
    /// Supports cancellation via <paramref name="ct"/>.
    /// </summary>
    /// <param name="session">The session to use for transcription.</param>
    /// <param name="pcmBuffers">Array of PCM buffers (16 kHz mono f32).</param>
    /// <param name="configure">Optional configuration for run parameters.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>Array of results, one per input buffer.</returns>
    public static unsafe IReadOnlyList<BatchResult> Run(
        Session session,
        float[][] pcmBuffers,
        Action<RunParamsBuilder>? configure = null,
        CancellationToken ct = default)
    {
        return RunInternal(session, pcmBuffers, configure, ct);
    }

    private static unsafe IReadOnlyList<BatchResult> RunInternal(
        Session session,
        float[][] pcmBuffers,
        Action<RunParamsBuilder>? configure,
        CancellationToken ct)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));
        if (pcmBuffers.Length == 0)
            return [];

        var n = pcmBuffers.Length;
        var pcmPtrs = new IntPtr[n];
        var sampleCounts = new int[n];

        // Pin all PCM buffers
        var handles = new GCHandle[n];
        try
        {
            for (int i = 0; i < n; i++)
            {
                handles[i] = GCHandle.Alloc(pcmBuffers[i], GCHandleType.Pinned);
                pcmPtrs[i] = handles[i].AddrOfPinnedObject();
                sampleCounts[i] = pcmBuffers[i].Length;
            }

            // Allocate arrays for native call
            var pcmPtrArray = Marshal.AllocHGlobal(n * IntPtr.Size);
            var sampleCountArray = Marshal.AllocHGlobal(n * sizeof(int));

            try
            {
                Marshal.Copy(pcmPtrs, 0, pcmPtrArray, n);
                Marshal.Copy(sampleCounts, 0, sampleCountArray, n);

                using var runParams = new RunParamsBuilder();
                configure?.Invoke(runParams);

                if (ct.CanBeCanceled)
                {
                    session.SetAbortCallback(_ => ct.IsCancellationRequested);
                }

                try
                {
                    var status = session.RunBatchInternal(pcmPtrArray, sampleCountArray, n, runParams.Build());
                    if (status != Status.Ok)
                        throw new TranscribeException(status, nameof(NativeMethods.RunBatch));
                }
                finally
                {
                    if (ct.CanBeCanceled)
                    {
                        session.ClearAbortCallback();
                    }
                }

                // Read results
                var resultCount = session.GetBatchResultCount();
                var results = new List<BatchResult>(resultCount);

                for (int i = 0; i < resultCount; i++)
                {
                    var batchStatus = session.GetBatchResultStatus(i);
                    var fullText = session.GetBatchResultFullText(i);
                    var lang = session.GetBatchResultDetectedLanguage(i);

                    results.Add(new BatchResult(
                        Index: i,
                        FullText: fullText,
                        DetectedLanguage: lang,
                        Status: batchStatus));
                }

                return results;
            }
            finally
            {
                Marshal.FreeHGlobal(pcmPtrArray);
                Marshal.FreeHGlobal(sampleCountArray);
            }
        }
        finally
        {
            for (int i = 0; i < n; i++)
            {
                if (handles[i].IsAllocated)
                    handles[i].Free();
            }
        }
    }
}
