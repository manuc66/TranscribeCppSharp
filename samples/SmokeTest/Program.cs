using System.Runtime.InteropServices;
using TranscribeCppSharp.Interop;

Console.WriteLine("=== TranscribeCppSharp Smoke Test ===");

// Test 1: Version
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

// Test 5: Full transcription (if model provided)
if (args.Length > 0)
{
    var modelPath = args[0];
    var wavPath = args.Length > 1 ? args[1] : null;

    Console.WriteLine($"\n=== Full Transcription Test ===");
    Console.WriteLine($"Model: {modelPath}");

    // Load model
    var loadParams = Marshal.AllocHGlobal(256);
    NativeMethods.ModelLoadParamsInit(loadParams);

    var modelHandle = Marshal.AllocHGlobal(IntPtr.Size);
    status = NativeMethods.ModelLoadFile(modelPath, loadParams, modelHandle);
    Console.WriteLine($"ModelLoadFile: {status}");

    if (status != Status.Ok)
    {
        Console.WriteLine($"Failed to load model: {NativeMethods.StatusString((int)status)}");
        return;
    }

    var model = Marshal.PtrToStructure<ModelHandle>(modelHandle);
    Console.WriteLine($"Model loaded: {model}");

    // Get capabilities
    var caps = Marshal.AllocHGlobal(256);
    NativeMethods.CapabilitiesInit(caps);
    status = NativeMethods.ModelGetCapabilities(model, caps);
    Console.WriteLine($"Capabilities: {status}");

    if (wavPath != null && File.Exists(wavPath))
    {
        // Read WAV file (16kHz mono PCM f32)
        var pcm = ReadWavFile(wavPath, out var nSamples);
        Console.WriteLine($"Audio: {nSamples} samples ({(double)nSamples / 16000:F1}s)");

        // Create session
        var sessionParams = Marshal.AllocHGlobal(256);
        NativeMethods.SessionParamsInit(sessionParams);

        var sessionHandle = Marshal.AllocHGlobal(IntPtr.Size);
        status = NativeMethods.SessionInit(model, sessionParams, sessionHandle);
        Console.WriteLine($"SessionInit: {status}");

        if (status == Status.Ok)
        {
            var session = Marshal.PtrToStructure<SessionHandle>(sessionHandle);

            // Run transcription
            var runParams = Marshal.AllocHGlobal(256);
            NativeMethods.RunParamsInit(runParams);

            var pcmPtr = Marshal.AllocHGlobal(nSamples * sizeof(float));
            Marshal.Copy(pcm, 0, pcmPtr, nSamples);

            status = NativeMethods.Run(session, pcmPtr, nSamples, runParams);
            Console.WriteLine($"Run: {status}");

            if (status == Status.Ok)
            {
                var textPtr = NativeMethods.FullText(session);
                var text = Marshal.PtrToStringUTF8(textPtr)!;
                Console.WriteLine($"\n--- Transcription ---");
                Console.WriteLine(text);
                Console.WriteLine($"--- End ---");
            }

            NativeMethods.PrintTimings(session);
            NativeMethods.SessionFree(session);
        }
    }
    else
    {
        Console.WriteLine("No WAV file provided, skipping transcription.");
        Console.WriteLine("Usage: SmokeTest <model.gguf> [audio.wav]");
    }

    NativeMethods.ModelFree(model);
}

Console.WriteLine("\nSmoke test passed!");

static float[] ReadWavFile(string path, out int nSamples)
{
    using var fs = File.OpenRead(path);
    using var br = new BinaryReader(fs);

    // Read RIFF header
    var riff = new string(br.ReadChars(4));
    var fileSize = br.ReadInt32();
    var wave = new string(br.ReadChars(4));

    if (riff != "RIFF" || wave != "WAVE")
        throw new InvalidDataException("Not a WAV file");

    int sampleRate = 0, bitsPerSample = 0, numChannels = 0;
    int dataSize = 0;
    long dataStart = 0;

    while (fs.Position < fs.Length)
    {
        var chunkId = new string(br.ReadChars(4));
        var chunkSize = br.ReadInt32();

        if (chunkId == "fmt ")
        {
            var audioFormat = br.ReadInt16();
            numChannels = br.ReadInt16();
            sampleRate = br.ReadInt32();
            br.ReadInt32(); // byte rate
            br.ReadInt16(); // block align
            bitsPerSample = br.ReadInt16();
            fs.Position += chunkSize - 16;
        }
        else if (chunkId == "data")
        {
            dataSize = chunkSize;
            dataStart = fs.Position;
            break;
        }
        else
        {
            fs.Position += chunkSize;
        }
    }

    if (sampleRate != 16000)
        throw new InvalidDataException($"Expected 16kHz, got {sampleRate}Hz");
    if (bitsPerSample != 16)
        throw new InvalidDataException($"Expected 16-bit, got {bitsPerSample}-bit");

    fs.Position = dataStart;
    var samples16 = new short[dataSize / 2];
    for (int i = 0; i < samples16.Length; i++)
        samples16[i] = br.ReadInt16();

    nSamples = samples16.Length / numChannels;
    var pcm = new float[nSamples];
    for (int i = 0; i < nSamples; i++)
    {
        // Mix to mono, normalize to [-1, 1]
        float sum = 0;
        for (int ch = 0; ch < numChannels; ch++)
            sum += samples16[i * numChannels + ch];
        pcm[i] = sum / numChannels / 32768f;
    }

    return pcm;
}
