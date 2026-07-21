#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
    /// </summary>
    /// <param name="session">The session to use for transcription.</param>
    /// <param name="pcmBuffers">Array of PCM buffers (16 kHz mono f32).</param>
    /// <param name="configure">Optional configuration for run parameters.</param>
    /// <returns>Array of results, one per input buffer.</returns>
    public static unsafe IReadOnlyList<BatchResult> Run(
        Session session,
        float[][] pcmBuffers,
        Action<RunParamsBuilder>? configure = null)
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

                var status = NativeMethods.RunBatch(session.Handle, pcmPtrArray, sampleCountArray, n, runParams.Handle);
                if (status != Status.Ok)
                    throw new TranscribeException(status, nameof(NativeMethods.RunBatch));

                // Read results
                var resultCount = NativeMethods.BatchNResults(session.Handle);
                var results = new List<BatchResult>(resultCount);

                for (int i = 0; i < resultCount; i++)
                {
                    var batchStatus = NativeMethods.BatchStatus(session.Handle, i);
                    var fullTextPtr = NativeMethods.BatchFullText(session.Handle, i);
                    var langPtr = NativeMethods.BatchDetectedLanguage(session.Handle, i);

                    var fullText = fullTextPtr != IntPtr.Zero
                        ? Marshal.PtrToStringUTF8(fullTextPtr) ?? ""
                        : "";
                    var lang = langPtr != IntPtr.Zero
                        ? Marshal.PtrToStringUTF8(langPtr) ?? ""
                        : "";

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
