using System.Runtime.InteropServices;
using TranscribeCppSharp;
using TranscribeCppSharp.Interop;

Console.WriteLine("=== TranscribeCppSharp Smoke Test ===");

// Test 1: Version (raw P/Invoke — no safe wrapper needed)
var versionPtr = NativeMethods.Version();
var version = Marshal.PtrToStringUTF8(versionPtr)!;
Console.WriteLine($"Version: {version}");

var commitPtr = NativeMethods.VersionCommit();
var commit = Marshal.PtrToStringUTF8(commitPtr)!;
Console.WriteLine($"Commit:  {commit}");

// Test 2: ABI struct sizes
foreach (AbiStruct s in Enum.GetValues<AbiStruct>())
{
    var size = NativeMethods.AbiStructSize(s);
    Console.WriteLine($"  ABI {s}: size={size}");
}

// Test 3: Init backends
Console.WriteLine("Initializing backends...");
var status = NativeMethods.InitBackendsDefault();
Console.WriteLine($"InitBackendsDefault: {status}");

if (status != Status.Ok)
{
    Console.WriteLine("Backend init failed (expected without GPU), continuing...");
}

// Test 4: Backend device count
var deviceCount = NativeMethods.BackendDeviceCount();
Console.WriteLine($"Backend devices: {deviceCount}");

// Test 5: Full transcription using safe wrapper (if model provided)
if (args.Length > 0)
{
    var modelPath = args[0];
    var wavPath = args.Length > 1 ? args[1] : null;

    Console.WriteLine($"\n=== Full Transcription Test (Safe API) ===");
    Console.WriteLine($"Model: {modelPath}");

    try
    {
        using var model = TranscribeCppSharp.Model.Load(modelPath);
        Console.WriteLine($"Model loaded: {model}");

        using var session = model.CreateSession();
        Console.WriteLine("Session created");

        if (wavPath != null && File.Exists(wavPath))
        {
            var pcm = TranscribeCppSharp.PcmExtensions.ReadWavToPcm(wavPath);
            Console.WriteLine($"Audio: {pcm.Length} samples ({(double)pcm.Length / 16000:F1}s)");

            var transcript = session.Run(pcm);
            Console.WriteLine($"\n--- Transcription ---");
            Console.WriteLine(transcript.FullText);
            Console.WriteLine($"--- End ---");
            Console.WriteLine($"Language: {transcript.DetectedLanguage}");
            Console.WriteLine($"Segments: {transcript.Segments.Count}");
            Console.WriteLine($"Words: {transcript.Words.Count}");

            if (transcript.Timing != null)
            {
                var t = transcript.Timing;
                Console.WriteLine($"Timings: load={t.LoadMs:F1}ms mel={t.MelMs:F1}ms encode={t.EncodeMs:F1}ms decode={t.DecodeMs:F1}ms");
            }
        }
        else
        {
            Console.WriteLine("No WAV file provided, skipping transcription.");
            Console.WriteLine("Usage: SmokeTest <model.gguf> [audio.wav]");
        }
    }
    catch (TranscribeException ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

Console.WriteLine("\nSmoke test passed!");
return 0;
