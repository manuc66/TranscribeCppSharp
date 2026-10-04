using System;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace TranscribeCppSharp.Ui.Services;

/// <summary>
/// Captures 16 kHz mono 16-bit PCM audio from the default microphone.
/// </summary>
public sealed class MicrophoneCapture : IDisposable
{
    private readonly WaveInEvent _waveIn;
    private readonly BufferedWaveProvider _buffer;
    private bool _disposed;

    public MicrophoneCapture()
    {
        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = 100,
        };
        _buffer = new BufferedWaveProvider(_waveIn.WaveFormat)
        {
            DiscardOnBufferOverflow = true,
        };
        _waveIn.DataAvailable += OnDataAvailable;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
    }

    /// <summary>
    /// Starts capturing audio from the microphone.
    /// </summary>
    public void Start()
    {
        _waveIn.StartRecording();
    }

    /// <summary>
    /// Stops capturing audio.
    /// </summary>
    public void Stop()
    {
        _waveIn.StopRecording();
    }

    /// <summary>
    /// Reads available audio samples as float PCM.
    /// </summary>
    public float[] ReadAvailable()
    {
        int bytesAvailable = _buffer.BufferedBytes;
        if (bytesAvailable == 0)
        {
            return [];
        }

        int sampleCount = bytesAvailable / 2;
        var samples = new float[sampleCount];
        var raw = new byte[bytesAvailable];
        _buffer.Read(raw, 0, bytesAvailable);

        for (int i = 0; i < sampleCount; i++)
        {
            short sample = BitConverter.ToInt16(raw, i * 2);
            samples[i] = sample / 32768f;
        }

        return samples;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _waveIn.Dispose();
        GC.SuppressFinalize(this);
    }
}
