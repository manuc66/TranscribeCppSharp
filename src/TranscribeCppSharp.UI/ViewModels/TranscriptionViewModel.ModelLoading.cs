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
    /// Rebuilds the picker from what is on disk and repairs the selection.
    /// </summary>
    /// <remarks>
    /// The selection has to be repaired rather than left alone: the picker only
    /// offers downloaded models, so an alias that was deleted (or never fetched)
    /// would show as no selection while still letting a run start against a file
    /// that is not there. With nothing on disk the alias is emptied, which is what
    /// disables the button.
    /// <para>
    /// The repaired selection is the model this tab last ran when it is still
    /// offered, so reopening the app lands on the model you were using; see
    /// <see cref="DownloadedModelCatalog.Selection"/>.
    /// </para>
    /// </remarks>
    private void LoadModels()
    {
        // Capture before clearing: the ComboBox bound to AvailableModels is
        // TwoWay on SelectedItem, so clearing the collection can null ModelAlias
        // through the binding before we get to rebuild it. Keeping the value
        // here is what lets the selection survive a refresh (and a tab switch).
        string currentModelAlias = ModelAlias;

        AvailableModels.Clear();
        IReadOnlyList<string> downloaded = DownloadedModelCatalog.Aliases();
        _downloadedModelCount = downloaded.Count;

        // The language is chosen first (task-oriented): a model is offered only when
        // its catalogue declares that it serves the chosen language — or declares
        // nothing, which means "unknown" rather than "no". The probe remains the
        // authority at run time and re-validates below.
        foreach (string alias in downloaded)
        {
            if (!string.Equals(ModelRole.KeyOf(alias), SelectedGoal.RoleKey, StringComparison.Ordinal))
            {
                continue;
            }
            // The language only filters models for ASR goals: a diarizer or a VAD
            // model takes no input language, so applying it would empty their list.
            if (GoalIsAsr && !ModelServesLanguage(alias, Language))
            {
                continue;
            }

            // The "speech to text + speakers" goal needs a transcript that can be
            // joined to speaker turns: either a model that attributes speakers
            // itself or one that reports segment timings. A model that does neither
            // (e.g. canary) is offered for the text-only goal instead, so the
            // picker never advertises an outcome it cannot produce.
            if (Goal == TranscriptionGoal.TranscribeWithSpeakers
                && !ModelServesSpeakers(alias))
            {
                continue;
            }
            AvailableModels.Add(alias);
        }

        // The global language list is rebuilt on goal change and on refresh, not
        // on every language change: rebuilding it here would clear the ComboBox
        // and drop its selection.
        ModelAlias = DownloadedModelCatalog.Selection(
            currentModelAlias, AvailableModels, UiSettings.LastModel(UiSettings.TranscriptionTab));

        LoadDiarizers();
        LoadVadModels();

        OnPropertyChanged(nameof(ModelPickerHint));
        OnPropertyChanged(nameof(HasDownloadedModels));
        OnPropertyChanged(nameof(HasAnyModelOnDisk));
        OnPropertyChanged(nameof(GoalModelHint));
        OnPropertyChanged(nameof(HasGoalModelHint));
        OnPropertyChanged(nameof(RunSummary));
        OnPropertyChanged(nameof(PipelineSummary));
        OnPropertyChanged(nameof(ShowTranscriptSection));
        OnPropertyChanged(nameof(ShowAdvanced));
        OnPropertyChanged(nameof(ShowLanguage));
        OnPropertyChanged(nameof(ShowLanguageColumn));
        OnPropertyChanged(nameof(ModelColumnSpan));
        OnPropertyChanged(nameof(ShowWindow));
        OnPropertyChanged(nameof(ShowAllowedLanguages));
        OnPropertyChanged(nameof(CanEditModel));
        OnPropertyChanged(nameof(CanEditLanguage));
        OnPropertyChanged(nameof(CanProbeThisRole));
        OnPropertyChanged(nameof(ShowSpeakerSection));
        OnPropertyChanged(nameof(CanAskForSpeakerLabels));
        OnPropertyChanged(nameof(ShowDiarizerPicker));
        OnPropertyChanged(nameof(DiarizerPlan));
        OnPropertyChanged(nameof(CanTranscribe));
        OnPropertyChanged(nameof(CanProbeMemory));
        OnPropertyChanged(nameof(RunBlockedReason));
        OnPropertyChanged(nameof(HasRunBlockedReason));
    }

    /// <summary>
    /// Rebuilds the diarizer picker from what is on disk, keeping the choice when it
    /// is still offered.
    /// </summary>
    private void LoadDiarizers()
    {
        DiarizerOption? keep = SelectedDiarizer;

        DiarizerOptions.Clear();
        DiarizerChoices.Clear();
        DiarizerOptions.Add(NoDiarizer);
        foreach (string alias in DownloadedModelCatalog.DiarizerAliases())
        {
            IReadOnlyList<string> languages = UpstreamCatalog.Read(alias)?.Languages ?? [];
            var option = new DiarizerOption(alias, alias, languages);
            DiarizerOptions.Add(option);
            DiarizerChoices.Add(option);
        }

        // A remembered alias that is no longer offered — deleted — falls back to
        // "none" rather than leaving the picker on something that is not there.
        SelectedDiarizer = keep is not null
            ? DiarizerOptions.FirstOrDefault(o => o.Alias == keep.Alias) ?? NoDiarizer
            : NoDiarizer;

        OnPropertyChanged(nameof(HasDedicatedDiarizer));
        OnPropertyChanged(nameof(NeedsDedicatedDiarizerDownload));
        OnPropertyChanged(nameof(DiarizerPlan));
        OnPropertyChanged(nameof(RunSummary));
        OnPropertyChanged(nameof(CanTranscribe));
        OnPropertyChanged(nameof(CanProbeMemory));
        OnPropertyChanged(nameof(RunBlockedReason));
        OnPropertyChanged(nameof(HasRunBlockedReason));
        SyncSpeakerIntent();
    }

    /// <summary>
    /// Rebuilds the VAD-model picker from what is on disk, keeping the choice when
    /// it is still offered. The option itself stays off; it is opt-in.
    /// </summary>
    private void LoadVadModels()
    {
        VadOption? keep = SelectedVadModel;

        VadOptions.Clear();
        foreach (string alias in DownloadedModelCatalog.Aliases())
        {
            if (string.Equals(ModelRole.KeyOf(alias), ModelRole.Vad, StringComparison.Ordinal))
            {
                VadOptions.Add(new VadOption(alias, alias));
            }
        }

        SelectedVadModel = keep is not null
            ? VadOptions.FirstOrDefault(o => o.Alias == keep.Alias)
            : VadOptions.FirstOrDefault();

        OnPropertyChanged(nameof(HasVadModels));
        OnPropertyChanged(nameof(NeedsVadModelDownload));
        OnPropertyChanged(nameof(VadSatisfied));
        OnPropertyChanged(nameof(PipelineSummary));
        OnPropertyChanged(nameof(CanTranscribe));
        OnPropertyChanged(nameof(CanProbeMemory));
        OnPropertyChanged(nameof(RunBlockedReason));
        OnPropertyChanged(nameof(HasRunBlockedReason));
    }

    /// <summary>
    /// Re-reads the cache. Called when the reader comes back from the Models tab,
    /// which is where a download or a delete happens.
    /// </summary>
    public void RefreshModels()
    {
        RebuildGlobalLanguages();
        LoadModels();
    }
}
