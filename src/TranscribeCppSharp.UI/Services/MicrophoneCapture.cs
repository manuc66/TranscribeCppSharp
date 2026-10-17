using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace TranscribeCppSharp.UI.Services;

/// <summary>
/// Captures the microphone as 16 kHz mono float PCM.
/// </summary>
/// <remarks>
/// ffmpeg does the capture, run as a child process that writes raw f32le to its
/// stdout. It is the same tool the wrapper already shells out to for decoding,
/// so the app keeps one audio dependency and needs no capture library. The
/// previous implementation used NAudio's WinMM recorder, which is
/// Windows-only: the app compiled everywhere but only ever captured on Windows.
///
/// The input is platform-specific and defaults to the system microphone:
/// <list type="bullet">
///   <item>Linux: <c>-f pulse -i default</c>.</item>
///   <item>macOS: <c>-f avfoundation -i :0</c> (the first audio device).</item>
///   <item>Windows: DirectShow, which has no "default" input, so
///   <c>TRANSCRIBE_MIC_DEVICE</c> must name one, e.g.
///   <c>audio="Microphone (Realtek(R) Audio)"</c>.</item>
/// </list>
/// Both <c>TRANSCRIBE_MIC_FORMAT</c> and <c>TRANSCRIBE_MIC_DEVICE</c> override
/// the defaults; list what ffmpeg can open with
/// <c>ffmpeg -f &lt;format&gt; -list_devices true -i ""</c>.
///
/// Capture at runtime is unverified: CI and the development container have no
/// audio device. Only the build and the process plumbing are checked.
/// </remarks>
public sealed class MicrophoneCapture : IDisposable
{
    /// <summary>The sample rate the native streaming session expects.</summary>
    private const int SampleRate = 16000;

    /// <summary>Drop the oldest audio past this, so a stalled reader cannot grow without bound.</summary>
    private const int MaxPendingBytes = 1 << 20;

    private readonly object _gate = new();
    private readonly List<byte> _pending = [];
    private readonly StringBuilder _errors = new();
    private Process? _ffmpeg;
    private CancellationTokenSource? _pumpCts;
    private bool _disposed;

    /// <summary>
    /// Starts capturing from the configured device. A second call while already
    /// capturing does nothing.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_ffmpeg is not null)
        {
            return;
        }

        lock (_gate)
        {
            _pending.Clear();
            _errors.Clear();
        }

        var psi = new ProcessStartInfo
        {
            FileName = ResolveFfmpeg(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };
        foreach (string argument in Arguments())
        {
            psi.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(psi)
                ?? throw new InvalidOperationException("Could not start ffmpeg for microphone capture.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Could not start ffmpeg for microphone capture: {ex.Message}", ex);
        }

        var cts = new CancellationTokenSource();
        _ffmpeg = process;
        _pumpCts = cts;

        // Both pumps end when the process ends or the token is cancelled; their
        // failures are reported from ReadAvailable via the exit code and stderr.
        _ = PumpAsync(process.StandardOutput.BaseStream, cts.Token);
        _ = ReadErrorsAsync(process.StandardError, cts.Token);
    }

    /// <summary>Stops capturing and ends the ffmpeg process.</summary>
    public void Stop()
    {
        Process? process = _ffmpeg;
        _ffmpeg = null;
        if (process is null)
        {
            return;
        }

        _pumpCts?.Cancel();
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process ended between the check and the kill.
        }

        process.WaitForExit((int)TimeSpan.FromSeconds(2).TotalMilliseconds);
        process.Dispose();
        _pumpCts?.Dispose();
        _pumpCts = null;

        lock (_gate)
        {
            _pending.Clear();
            _errors.Clear();
        }
    }

    /// <summary>
    /// Drains the audio captured since the last call. Empty when nothing has
    /// arrived yet; throws if ffmpeg has exited with an error.
    /// </summary>
    public float[] ReadAvailable()
    {
        ThrowIfCaptureFailed();

        lock (_gate)
        {
            // A read can split a float; keep the remainder for the next call.
            int usable = _pending.Count - (_pending.Count % sizeof(float));
            if (usable == 0)
            {
                return [];
            }

            var bytes = new byte[usable];
            _pending.CopyTo(0, bytes, 0, usable);
            _pending.RemoveRange(0, usable);

            var samples = new float[usable / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, samples, 0, usable);
            return samples;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }

    private static string[] Arguments()
    {
        string format = Environment.GetEnvironmentVariable("TRANSCRIBE_MIC_FORMAT") ?? DefaultFormat();
        string? device = Environment.GetEnvironmentVariable("TRANSCRIBE_MIC_DEVICE") ?? DefaultDevice();

        if (string.IsNullOrWhiteSpace(device))
        {
            throw new InvalidOperationException("no microphone device is configured for ffmpeg. " + DeviceHint());
        }

        return
        [
            "-nostdin", "-hide_banner", "-v", "error",
            "-f", format, "-i", device,
            "-ac", "1", "-ar", SampleRate.ToString(CultureInfo.InvariantCulture),
            "-f", "f32le", "-",
        ];
    }

    private static string DefaultFormat()
    {
        if (OperatingSystem.IsWindows())
        {
            return "dshow";
        }

        return OperatingSystem.IsMacOS() ? "avfoundation" : "pulse";
    }

    private static string? DefaultDevice()
    {
        if (OperatingSystem.IsWindows())
        {
            return null; // DirectShow has no "default" input: it must be named.
        }

        return OperatingSystem.IsMacOS() ? ":0" : "default";
    }

    private static string DeviceHint()
        => OperatingSystem.IsWindows()
            ? "Set TRANSCRIBE_MIC_DEVICE to a DirectShow input name; list them with: ffmpeg -f dshow -list_devices true -i \"\"."
            : "Set TRANSCRIBE_MIC_DEVICE (and TRANSCRIBE_MIC_FORMAT) to an input ffmpeg can open; list them with: ffmpeg -f <format> -list_devices true -i \"\".";

    private static string ResolveFfmpeg()
    {
        string exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(dir, exe);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "'ffmpeg' was not found in PATH. Microphone capture needs it, as does decoding non-WAV audio.");
    }

    private async Task PumpAsync(Stream stdout, CancellationToken token)
    {
        var buffer = new byte[8192];
        try
        {
            while (true)
            {
                int read = await stdout.ReadAsync(buffer, token).ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                lock (_gate)
                {
                    _pending.AddRange(buffer.AsSpan(0, read));
                    int excess = _pending.Count - MaxPendingBytes;
                    if (excess > 0)
                    {
                        _pending.RemoveRange(0, excess);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Stop cancelled the pump, or ffmpeg ended and the process this
            // stream belongs to was disposed. ThrowIfCaptureFailed reports the
            // reason for a nonzero exit, from the exit code and stderr.
        }
    }

    private async Task ReadErrorsAsync(StreamReader stderr, CancellationToken token)
    {
        try
        {
            string text = await stderr.ReadToEndAsync(token).ConfigureAwait(false);
            lock (_gate)
            {
                _errors.Append(text);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Stop cancelled the read, or ffmpeg ended and the reader was disposed.
        }
    }

    private void ThrowIfCaptureFailed()
    {
        Process? process = _ffmpeg;
        if (process is null)
        {
            return;
        }

        bool exited;
        try
        {
            exited = process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return;
        }

        if (!exited || process.ExitCode == 0)
        {
            return;
        }

        string stderr;
        lock (_gate)
        {
            stderr = _errors.ToString().Trim();
        }

        throw new InvalidOperationException(
            $"microphone capture stopped: ffmpeg exited with code {process.ExitCode}"
            + (stderr.Length == 0 ? string.Empty : $" ({stderr})")
            + ". " + DeviceHint());
    }
}
