using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;
using TranscribeCppSharp.UI.ViewModels;
using TranscribeCppSharp.UI.Views;
using Xunit;

namespace TranscribeCppSharp.UI.Tests;

/// <summary>
/// Speaker labels are one intent, not two unrelated pickers. These tests pin what
/// the tab offers for each kind of model, what the run passes, and what happens
/// when the intent cannot be honoured.
/// </summary>
[Collection("UiGlobalState")]
public class DiarizerPickerTests
{
    private const string Sortformer = "diar_streaming_sortformer_4spk-v2.1";

    private static ModelCapabilitySnapshot Snapshot(
        TranscribeRole roles,
        bool diarizeFeature = false,
        TimestampKind maxTimestamps = TimestampKind.TimestampsSegment)
        => new(
            Architecture: "test",
            Variant: "test",
            Roles: roles,
            SupportsTranslate: false,
            SupportsLanguageDetect: false,
            SupportsStreaming: false,
            SupportsSpecDecode: false,
            Features: diarizeFeature ? new HashSet<Feature> { Feature.FeatureDiarization } : new HashSet<Feature>(),
            Languages: Array.Empty<string>(),
            TranslateTargetLanguages: Array.Empty<string>(),
            MaxTimestampKind: maxTimestamps);

    private static void Plant(string alias)
    {
        ModelDescriptor descriptor = ModelStore.Catalog.Single(m => m.Alias == alias);
        string path = ModelStore.CachedPath(descriptor);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "placeholder");
    }

    /// <summary>Runs the body with a temporary cache root, then restores it.</summary>
    private static void InCache(Action body)
    {
        string previous = ModelStore.CacheRootOverride ?? string.Empty;
        bool hadOverride = ModelStore.CacheRootOverride is not null;
        ModelStore.CacheRootOverride = Path.Combine(
            Path.GetTempPath(), "tcsharp-diarizer-picker-" + Guid.NewGuid().ToString("N"));
        try
        {
            body();
        }
        finally
        {
            ModelStore.CacheRootOverride = hadOverride ? previous : null;
        }
    }

    [Fact]
    public void ADedicatedDiarizerIsOfferedOnlyWhereItIsNeeded()
    {
        InCache(() =>
        {
            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());

            // A plain transcriber needs a second model, and the picker only appears
            // once the reader asks for speaker labels.
            viewModel.ApplyCapabilities(Snapshot(TranscribeRole.Asr));
            Assert.True(viewModel.CanUseDedicatedDiarizer);
            Assert.False(viewModel.ShowDiarizerPicker);

            // The picker follows the intent, but there is no diarizer on disk yet:
            // the tab offers the route to Models instead of a one-entry picker.
            viewModel.LabelSpeakers = true;
            Assert.True(viewModel.NeedsDedicatedDiarizerDownload);
            Assert.False(viewModel.ShowDiarizerPicker);

            // The model diarizes through its own feature: no second one needed.
            viewModel.ApplyCapabilities(Snapshot(TranscribeRole.Asr, diarizeFeature: true));
            Assert.True(viewModel.CanModelDiarize);
            Assert.False(viewModel.CanUseDedicatedDiarizer);
            Assert.False(viewModel.ShowDiarizerPicker);
            Assert.Equal(DiarizeMode.DiarizeModeOn, viewModel.DiarizeMode);

            // The model reports the DIARIZE role as well.
            viewModel.ApplyCapabilities(Snapshot(TranscribeRole.Asr | TranscribeRole.Diarize));
            Assert.True(viewModel.CanModelDiarize);
            Assert.False(viewModel.ShowDiarizerPicker);

            // A model that only diarizes has nothing to pair with, and no speaker
            // checkbox either: its whole result is speaker turns.
            viewModel.ApplyCapabilities(Snapshot(TranscribeRole.Diarize));
            Assert.False(viewModel.ShowSpeakerSection);
            Assert.False(viewModel.CanAskForSpeakerLabels);
            Assert.False(viewModel.LabelSpeakers);
            Assert.False(viewModel.ShowDiarizerPicker);
        });
    }

    [Fact]
    public void ADownloadedDiarizerIsOfferedAndTheIntentStartsOff()
    {
        InCache(() =>
        {
            Plant(Sortformer);

            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());

            Assert.Contains(viewModel.DiarizerOptions, o => o.Alias == Sortformer);
            Assert.Contains(viewModel.DiarizerOptions, o => o.Alias is null);
            Assert.Null(viewModel.SelectedDiarizer?.Alias);
            Assert.False(viewModel.LabelSpeakers);
            Assert.Equal(DiarizeMode.DiarizeModeOff, viewModel.DiarizeMode);
        });
    }

    [Fact]
    public void WithNothingDownloadedThePickerOffersOnlyNone()
    {
        InCache(() =>
        {
            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());

            Assert.Single(viewModel.DiarizerOptions);
            Assert.Null(viewModel.DiarizerOptions[0].Alias);
            // Nothing to describe yet: the speaker group waits for a model.
            Assert.False(viewModel.ShowSpeakerSection);
        });
    }

    [Fact]
    public void ThePickerNeedsSegmentTimingsAndSaysWhyWhenTheyAreMissing()
    {
        // Without timings there is nothing to overlap a speaker turn with, so the
        // join cannot work. Offering the intent anyway would promise an attribution
        // the app cannot produce.
        InCache(() =>
        {
            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());

            viewModel.ApplyCapabilities(
                Snapshot(TranscribeRole.Asr, maxTimestamps: TimestampKind.TimestampsNone));
            Assert.False(viewModel.CanAskForSpeakerLabels);
            Assert.True(viewModel.ShowDiarizerUnavailableHint);
            Assert.Contains("timings", viewModel.DiarizerUnavailableHint, StringComparison.OrdinalIgnoreCase);

            // Asking anyway is refused before the run, not silently ignored.
            viewModel.LabelSpeakers = true;
            Assert.False(viewModel.LabelSpeakers);
            Assert.Equal(DiarizeMode.DiarizeModeOff, viewModel.DiarizeMode);

            viewModel.ApplyCapabilities(Snapshot(TranscribeRole.Asr));
            Assert.True(viewModel.CanAskForSpeakerLabels);
            Assert.False(viewModel.ShowDiarizerUnavailableHint);
            Assert.True(viewModel.CanUseDedicatedDiarizer);
        });
    }

    [Fact]
    public void TurningTheIntentOnSelectsTheOnlyDiarizerAndNamesBothModels()
    {
        InCache(() =>
        {
            Plant(Sortformer);
            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());
            // The alias first: changing it starts a probe that clears the snapshot
            // when the model is not on disk, which would undo the one below.
            viewModel.ModelAlias = "whisper-tiny";
            viewModel.ApplyCapabilities(Snapshot(TranscribeRole.Asr));

            // Nothing asked for: say what that means rather than leaving the reader
            // to assume the model attributes speakers.
            Assert.Contains("Speaker 0", viewModel.DiarizerPlan);

            viewModel.LabelSpeakers = true;

            // The only downloaded diarizer is chosen for the reader, so the plan can
            // name what will actually run instead of saying "pick one".
            Assert.Equal(Sortformer, viewModel.SelectedDiarizer?.Alias);
            Assert.True(viewModel.ShowDiarizerPicker);
            Assert.DoesNotContain(viewModel.DiarizerChoices, o => o.Alias is null);
            Assert.True(viewModel.SpeakerLabelsSatisfied);
            Assert.Contains("whisper-tiny", viewModel.DiarizerPlan);
            Assert.Contains(Sortformer, viewModel.DiarizerPlan);
            Assert.Contains("two passes", viewModel.DiarizerPlan, StringComparison.OrdinalIgnoreCase);

            TranscriptionOptions options = viewModel.BuildTranscriptionOptions();
            Assert.Equal(Sortformer, options.DiarizerModel);
            Assert.Equal(DiarizeMode.DiarizeModeOff, options.DiarizeMode);
        });
    }

    [Fact]
    public void TheRunAsksForAutoTimestampsEvenWhenTheMaximumLooksFiner()
    {
        // granite-speech-4.1-2b-plus reports WORD (numerically finer than
        // SEGMENT) and still rejects an explicit SEGMENT with
        // ErrUnsupportedTimestamps. AUTO is never rejected and resolves to what
        // the family actually produces, so the run must not infer SEGMENT from
        // the capability's ordering.
        InCache(() =>
        {
            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());
            viewModel.ApplyCapabilities(
                Snapshot(TranscribeRole.Asr, maxTimestamps: TimestampKind.TimestampsWord));

            Assert.Equal(
                TimestampKind.TimestampsAuto,
                viewModel.BuildTranscriptionOptions().TimestampKind);
        });
    }

    [Fact]
    public void NoDiarizerDownloadedBlocksTheRunInsteadOfProducingSpeakerZero()
    {
        InCache(() =>
        {
            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());
            // The picker is what matters here, not a file on disk: seed the list
            // directly so the only gate left is the missing diarizer.
            viewModel.AvailableModels.Add("whisper-tiny");
            viewModel.ApplyCapabilities(Snapshot(TranscribeRole.Asr));
            string audio = Path.Combine(
                Path.GetTempPath(), "tcsharp-diarizer-" + Guid.NewGuid().ToString("N") + ".wav");
            File.WriteAllText(audio, string.Empty);
            try
            {
                viewModel.AudioPath = audio;
                viewModel.LabelSpeakers = true;

                Assert.True(viewModel.NeedsDedicatedDiarizerDownload);
                Assert.False(viewModel.ShowDiarizerPicker);
                Assert.False(viewModel.SpeakerLabelsSatisfied);
                Assert.False(viewModel.CanTranscribe);
                Assert.Contains("diarizer", viewModel.RunBlockedReason, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Models", viewModel.RunBlockedReason, StringComparison.Ordinal);

                // Turning the intent off restores the ordinary transcript run, and
                // the remembered diarizer alias is not passed anyway.
                viewModel.LabelSpeakers = false;
                Assert.True(viewModel.SpeakerLabelsSatisfied);
                Assert.Null(viewModel.BuildTranscriptionOptions().DiarizerModel);
            }
            finally
            {
                File.Delete(audio);
            }
        });
    }

    [Fact]
    public void AModelThatDiarizesItselfGetsTheOnRequestWithoutASecondModel()
    {
        InCache(() =>
        {
            Plant(Sortformer);
            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());
            viewModel.ModelAlias = "moss-transcribe-diarize";
            viewModel.ApplyCapabilities(Snapshot(TranscribeRole.Asr, diarizeFeature: true));
            viewModel.SelectedDiarizer = viewModel.DiarizerOptions.Single(o => o.Alias == Sortformer);

            viewModel.LabelSpeakers = true;

            Assert.Equal(DiarizeMode.DiarizeModeOn, viewModel.DiarizeMode);
            Assert.False(viewModel.ShowDiarizerPicker);
            Assert.Contains("attributes speakers itself", viewModel.DiarizerPlan, StringComparison.OrdinalIgnoreCase);

            TranscriptionOptions options = viewModel.BuildTranscriptionOptions();
            Assert.Equal(DiarizeMode.DiarizeModeOn, options.DiarizeMode);
            Assert.Null(options.DiarizerModel);
        });
    }

    [Fact]
    public void ARestoredResultWithSpeakersSaysTheControlsAreForTheNextRun()
    {
        InCache(() =>
        {
            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());
            viewModel.ApplyCapabilities(Snapshot(TranscribeRole.Asr));
            viewModel.Result = new TranscriptionResult
            {
                SpeakerSegments =
                [
                    new SpeakerSegmentResult(
                        TimeSpan.Zero, TimeSpan.FromSeconds(2), 1, 0.9f),
                ],
            };
            viewModel.HasResult = true;

            Assert.False(viewModel.LabelSpeakers);
            Assert.True(viewModel.HasResultSpeakersNote);
            Assert.Contains("next run", viewModel.ResultSpeakersNote, StringComparison.Ordinal);

            // Once the intent is on, the plan line already says what will happen;
            // the note about the old result would only add noise.
            viewModel.LabelSpeakers = true;
            Assert.False(viewModel.HasResultSpeakersNote);
        });
    }

    [AvaloniaFact]
    public void TheSpeakerCheckboxFollowsTheSettingsLock()
    {
        InCache(() =>
        {
            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());
            viewModel.ApplyCapabilities(Snapshot(TranscribeRole.Asr));

            var view = new TranscriptionView { DataContext = viewModel };
            var window = new Window { Content = view, Width = 1100, Height = 900 };
            window.Show();

            CheckBox box = view.GetLogicalDescendants()
                .OfType<CheckBox>()
                .First(c => AutomationProperties.GetAutomationId(c) == "transcription-diarize");

            Assert.True(box.IsEffectivelyEnabled);

            // A run works from its snapshot; toggling the intent during one would
            // change what the reader believes the run did.
            viewModel.IsTranscribing = true;
            Assert.False(box.IsEffectivelyEnabled);

            viewModel.IsTranscribing = false;
            Assert.True(box.IsEffectivelyEnabled);
        });
    }

    [Fact]
    public void TheGetADiarizerButtonOpensTheCatalogueFilteredToDiarization()
    {
        InCache(() =>
        {
            var transcription = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());
            var root = new MainWindowViewModel(
                transcription,
                new StreamingViewModel(
                    new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache()),
                new BatchViewModel(new StubTranscriptionService(), new SettingsViewModel()),
                new ModelManagerViewModel(),
                new SettingsViewModel(),
                new HistoryViewModel());

            transcription.OpenDiarizerModelsCommand.Execute(null);

            Assert.Equal(MainWindowViewModel.ModelTabIndex, root.SelectedTabIndex);
            Assert.Equal("diarize", root.ModelManager.FeatureFilter);
        });
    }

    [Fact]
    public void ADiarizerThatDeclaresOneLanguageWarnsWhenAnotherIsSelected()
    {
        // Sortformer's catalog record declares English only. Pairing it with a
        // French transcription is possible, and the app must say so instead of
        // letting the reader believe the attribution is language-agnostic.
        InCache(() =>
        {
            Plant(Sortformer);
            var viewModel = new TranscriptionViewModel(
                new StubTranscriptionService(), new SettingsViewModel(), new ModelCapabilityCache());
            viewModel.ApplyCapabilities(Snapshot(TranscribeRole.Asr));
            viewModel.LabelSpeakers = true;
            Assert.Equal(Sortformer, viewModel.SelectedDiarizer?.Alias);

            viewModel.Language = "en";
            Assert.False(viewModel.HasDiarizerLanguageWarning);

            viewModel.Language = "fr";
            Assert.True(viewModel.HasDiarizerLanguageWarning);
            Assert.Contains(Sortformer, viewModel.DiarizerLanguageWarning);
            Assert.Contains("fr", viewModel.DiarizerLanguageWarning);
            Assert.Contains("en", viewModel.DiarizerLanguageWarning);

            // Auto-detection has not chosen a language yet, so there is nothing to
            // contradict.
            viewModel.Language = "auto";
            Assert.False(viewModel.HasDiarizerLanguageWarning);

            // With speaker labels off, the diarizer is not part of the run and its
            // declared languages are not a caveat on it.
            viewModel.LabelSpeakers = false;
            viewModel.Language = "fr";
            Assert.False(viewModel.HasDiarizerLanguageWarning);
        });
    }
}
