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
    /// <summary>Lets the goal set the speaker checkbox without re-entering itself.</summary>
    private void SyncGoalAndSpeakers()
    {
        _syncingGoal = true;
        try
        {
            LabelSpeakers = Goal == TranscriptionGoal.TranscribeWithSpeakers;
        }
        finally
        {
            _syncingGoal = false;
        }
    }

    partial void OnModelAliasChanged(string value)
    {
        RequestCapabilityProbe(value);
        ApplyRememberedWindow();
        OnPropertyChanged(nameof(DiarizerPlan));
        OnPropertyChanged(nameof(CanTranscribe));
        OnPropertyChanged(nameof(CanProbeMemory));
        OnPropertyChanged(nameof(RunBlockedReason));
        OnPropertyChanged(nameof(HasRunBlockedReason));
        OnPropertyChanged(nameof(RunSummary));
        UpdateMemoryWarning();
    }

    partial void OnSelectedTaskChanged(TranscriptionTask? value)
    {
        OnPropertyChanged(nameof(ShowTargetLanguage));
        OnPropertyChanged(nameof(RunSummary));
    }

    partial void OnWindowSecondsChanged(int value)
    {
        OnPropertyChanged(nameof(RunSummary));
        OnPropertyChanged(nameof(PipelineSummary));
        UpdateMemoryWarning();
    }

    partial void OnAutoAdjustWindowChanged(bool value)
        => UiSettings.SaveAutoAdjustWindow(value);

    partial void OnUseVadChanged(bool value)
    {
        // Turning the option on with a downloaded VAD model selects it, so the
        // common case is one click. With none, the run is refused and the tab
        // offers the route to Models; the option stays on so the intent is visible.
        if (value && SelectedVadModel is null)
        {
            SelectedVadModel = VadOptions.FirstOrDefault();
        }

        if (!_restoringSettings)
        {
            UiSettings.SaveTranscriptionUseVad(value);
            if (value)
            {
                UiSettings.SaveTranscriptionVadModel(SelectedVadModel?.Alias);
            }
        }

        OnPropertyChanged(nameof(NeedsVadModelDownload));
        OnPropertyChanged(nameof(VadSatisfied));
        OnPropertyChanged(nameof(PipelineSummary));
    }

    partial void OnSelectedVadModelChanged(VadOption? value)
    {
        if (!_restoringSettings && UseVad && GoalIsAsr)
        {
            UiSettings.SaveTranscriptionVadModel(value?.Alias);
        }

        OnPropertyChanged(nameof(NeedsVadModelDownload));
        OnPropertyChanged(nameof(VadSatisfied));
        OnPropertyChanged(nameof(PipelineSummary));
        OnPropertyChanged(nameof(CanTranscribe));
        OnPropertyChanged(nameof(CanProbeMemory));
        OnPropertyChanged(nameof(RunBlockedReason));
        OnPropertyChanged(nameof(HasRunBlockedReason));
    }

    partial void OnSelectedDiarizerChanged(DiarizerOption? value)
    {
        OnPropertyChanged(nameof(DiarizerPlan));
        OnPropertyChanged(nameof(DiarizerLanguageWarning));
        OnPropertyChanged(nameof(HasDiarizerLanguageWarning));
        OnPropertyChanged(nameof(CanTranscribe));
        OnPropertyChanged(nameof(CanProbeMemory));
        OnPropertyChanged(nameof(RunBlockedReason));
        OnPropertyChanged(nameof(HasRunBlockedReason));
        OnPropertyChanged(nameof(NeedsDedicatedDiarizerDownload));
        OnPropertyChanged(nameof(RunSummary));
        OnPropertyChanged(nameof(PipelineSummary));
    }

    /// <summary>
    /// Keeps the run's request aligned with the reader's intent: the model's own
    /// diarization when it has one, a downloaded dedicated diarizer otherwise, and
    /// never "on" for a model that cannot answer it.
    /// </summary>
    private void SyncSpeakerIntent()
    {
        // A model that can do neither cannot be asked for labels. A text-only goal
        // clears the box; an explicit speaker goal keeps the intent so the tab can
        // say why it cannot be honoured instead of silently dropping it.
        bool canHonour = CanAskForSpeakerLabels;
        bool asrModel = _selectedCapabilities is null
            || _selectedCapabilities.Roles.HasFlag(TranscribeRole.Asr);
        if (!canHonour && LabelSpeakers
            && !(Goal == TranscriptionGoal.TranscribeWithSpeakers && asrModel))
        {
            LabelSpeakers = false;
            return;
        }

        DiarizeMode = LabelSpeakers && CanModelDiarize
            ? DiarizeMode.DiarizeModeOn
            : DiarizeMode.DiarizeModeOff;

        if (LabelSpeakers && canHonour && !CanModelDiarize && SelectedDiarizer?.Alias is null)
        {
            // Picking the only (or first) downloaded diarizer saves a step and makes
            // the plan line name the model that will actually run. The picker stays
            // visible, so the choice can still be changed.
            SelectedDiarizer = DiarizerOptions.FirstOrDefault(o => o.Alias is not null) ?? NoDiarizer;
        }

        OnPropertyChanged(nameof(ShowDiarizerPicker));
        OnPropertyChanged(nameof(ShowDiarizerUnavailableHint));
        OnPropertyChanged(nameof(DiarizerPlan));
        OnPropertyChanged(nameof(DiarizerLanguageWarning));
        OnPropertyChanged(nameof(HasDiarizerLanguageWarning));
        OnPropertyChanged(nameof(NeedsDedicatedDiarizerDownload));
        OnPropertyChanged(nameof(SpeakerLabelsSatisfied));
        OnPropertyChanged(nameof(PipelineSummary));
        OnPropertyChanged(nameof(RunSummary));
        OnPropertyChanged(nameof(RunBlockedReason));
        OnPropertyChanged(nameof(HasRunBlockedReason));
    }
}
