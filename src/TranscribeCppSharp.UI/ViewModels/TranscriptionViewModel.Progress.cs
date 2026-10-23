using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Interop;
using Avalonia.Input.Platform;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.SharedSource;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;

namespace TranscribeCppSharp.UI.ViewModels;

public partial class TranscriptionViewModel
{
    /// <summary>
    /// Puts a phase on screen: its label, whether the bar can fill, and the
    /// sentence that says why when it cannot.
    /// </summary>
    private void ApplyPhase(TranscriptionPhase phase)
    {
        _phase = phase;
        switch (phase)
        {
            case TranscriptionPhase.Preparing:
                ProgressIsIndeterminate = true;
                ProgressNote = "Loading the model. The native library reports no progress for a load.";
                ProgressStage = $"Loading {ModelAlias}…";
                StatusMessage = "Loading the model…";
                break;

            case TranscriptionPhase.ReadingAudio:
                ProgressIsIndeterminate = true;
                ProgressNote = "Reading and decoding the audio. The decoder reports no progress.";
                ProgressStage = "Reading the audio…";
                StatusMessage = "Reading the audio…";
                break;

            case TranscriptionPhase.DetectingSpeech:
                ProgressIsIndeterminate = true;
                ProgressNote = "The VAD model is finding the speech intervals. It reports no fraction; "
                    + "the transcriber then runs only over the speech it found.";
                ProgressStage = "Detecting speech…";
                StatusMessage = "Detecting speech…";
                break;

            case TranscriptionPhase.Diarizing:
                ProgressIsIndeterminate = true;
                // A chained diarizer follows a finished transcript; a model whose
                // whole role is diarization has no transcript to speak of.
                bool chained = LabelSpeakers && !CanModelDiarize;
                ProgressNote = chained
                    ? "The transcript is complete. The diarizer runs the whole file in one native call "
                        + "and reports no progress; this pass adds speaker turns."
                    : "Diarizing runs the whole file in one native call and reports no progress.";
                ProgressStage = "Diarizing — attributing speakers";
                StatusMessage = chained
                    ? "Diarizing… the transcript is complete; a separate diarizer is attributing speakers."
                    : "Diarizing…";
                break;

            default:
                ProgressIsIndeterminate = !PhaseReportsFraction(phase);
                ProgressNote = !ProgressIsIndeterminate
                    ? string.Empty
                    : _selectedCapabilities is null
                        ? "No fraction has been reported yet."
                        : _selectedCapabilities.Roles.HasFlag(TranscribeRole.Vad)
                            ? "Voice activity is scored one window at a time; the bar fills after the first window returns."
                            : "This role runs the whole file in one native call and reports no progress.";
                if (ProgressIsIndeterminate)
                {
                    ProgressStage = $"{RunButtonText}…";
                }

                StatusMessage = "Transcribing...";
                break;
        }

        UpdateProgressTiming();
    }

    /// <summary>
    /// Whether a phase reports a measured fraction. Windows fill the ASR and VAD
    /// paths; the other roles answer in one call.
    /// </summary>
    private bool PhaseReportsFraction(TranscriptionPhase phase)
    {
        if (phase != TranscriptionPhase.Transcribing)
        {
            return false;
        }

        // Not probed yet: nothing is known, so the bar does not claim a fraction.
        // The first window step or the first progress report replaces that as soon
        // as one arrives, so an ASR or VAD run corrects itself immediately. VAD
        // reports a fraction per window but no step, so it starts indeterminate and
        // turns determinate on its first progress report.
        return _selectedCapabilities is { } caps
            && caps.Roles.HasFlag(TranscribeRole.Asr);
    }

    /// <summary>
    /// Recomputes the elapsed line and the estimate from the windows finished so
    /// far. Called once a second and on every step, so a long run never looks
    /// frozen between windows.
    /// </summary>
    private void UpdateProgressTiming()
    {
        if (!IsTranscribing)
        {
            ElapsedText = string.Empty;
            RemainingText = string.Empty;
            return;
        }

        ElapsedText = FormatDuration(DateTime.UtcNow - _runStartedAt);

        // The estimate starts after a second window: the first one's time includes
        // whatever warm-up the model does, and using it would overstate the rest.
        if (_phase == TranscriptionPhase.Transcribing
            && _firstWindowFinishedAt is { } first
            && _windowsCompleted > 1
            && _windowsTotal > _windowsCompleted)
        {
            double secondsPerWindow =
                (DateTime.UtcNow - first).TotalSeconds / (_windowsCompleted - 1);
            TimeSpan remaining = TimeSpan.FromSeconds(
                secondsPerWindow * (_windowsTotal - _windowsCompleted));
            RemainingText = "about " + FormatDuration(remaining) + " left (estimate)";
        }
        else
        {
            RemainingText = string.Empty;
        }
    }

    /// <summary>mm:ss, or h:mm:ss past an hour. Never a rounded-up claim.</summary>
    private static string FormatDuration(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes:00}:{value.Seconds:00}";
    }
}
