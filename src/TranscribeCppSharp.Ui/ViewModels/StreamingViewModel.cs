using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TranscribeCppSharp;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.Ui.Models;
using TranscribeCppSharp.Ui.Services;

namespace TranscribeCppSharp.Ui.ViewModels;

public partial class StreamingViewModel : ObservableObject, IDisposable
{
    private readonly ITranscriptionService _transcriptionService;

    private readonly SettingsViewModel _settings;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    private readonly Dictionary<string, ModelCapabilitySnapshot> _capabilityCache = new(StringComparer.Ordinal);
    private ModelCapabilitySnapshot? _selectedCapabilities;
    private bool _probing;
    private string? _pendingProbeAlias;

    [ObservableProperty]
    private string _modelAlias = "moss-transcribe-diarize";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartStreaming))]
    private bool _isStreaming;

    [ObservableProperty]
    private string _committedText = string.Empty;

    [ObservableProperty]
    private string _tentativeText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>True while the selected model is being loaded to read its capabilities.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartStreaming))]
    private bool _isCheckingCapabilities;

    /// <summary>What the capability check is doing, or why options are unfiltered.</summary>
    [ObservableProperty]
    private string _capabilityStatus = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _availableModels = new();

    [ObservableProperty]
    private StreamCommitPolicy _selectedCommitPolicy = StreamCommitPolicy.StreamCommitAuto;

    [ObservableProperty]
    private int? _stablePrefixAgreement;

    [ObservableProperty]
    private int? _moonshineMinDecodeIntervalMs;

    [ObservableProperty]
    private int? _parakeetAttContextRight;

    [ObservableProperty]
    private int? _parakeetLeftMs;

    [ObservableProperty]
    private int? _parakeetChunkMs;

    [ObservableProperty]
    private int? _parakeetRightMs;

    [ObservableProperty]
    private SortformerPreset? _selectedSortformerPreset;

    [ObservableProperty]
    private int? _voxtralNumDelayTokens;

    [ObservableProperty]
    private int? _voxtralMinDecodeIntervalMs;

    public ObservableCollection<StreamCommitPolicy> AvailableCommitPolicies { get; } = new()
    {
        StreamCommitPolicy.StreamCommitAuto,
        StreamCommitPolicy.StreamCommitOnFinalize,
        StreamCommitPolicy.StreamCommitStablePrefix,
    };

    public ObservableCollection<SortformerPreset> AvailableSortformerPresets { get; } = new()
    {
        SortformerPreset.SortformerPresetDefault,
        SortformerPreset.SortformerPresetVeryHighLatency,
        SortformerPreset.SortformerPresetHighLatency,
        SortformerPreset.SortformerPresetLowLatency,
    };

    /// <summary>Whether the family-extension group applies to the selected model.</summary>
    public bool ShowFamilyExtensions => ModelOptionVisibility.ShowStreamingExtensions(_selectedCapabilities);

    /// <summary>Whether the Moonshine extension applies.</summary>
    public bool ShowMoonshineExtensions => ModelOptionVisibility.ShowMoonshineExtensions(_selectedCapabilities);

    /// <summary>Whether the Parakeet extensions apply.</summary>
    public bool ShowParakeetExtensions => ModelOptionVisibility.ShowParakeetExtensions(_selectedCapabilities);

    /// <summary>Whether the Sortformer extension applies.</summary>
    public bool ShowSortformerExtensions => ModelOptionVisibility.ShowSortformerExtensions(_selectedCapabilities);

    /// <summary>Whether the Voxtral realtime extensions apply.</summary>
    public bool ShowVoxtralExtensions => ModelOptionVisibility.ShowVoxtralExtensions(_selectedCapabilities);

    /// <summary>False while streaming or a capability check is running.</summary>
    public bool CanStartStreaming => !IsStreaming && !IsCheckingCapabilities;

    public StreamingViewModel(
        ITranscriptionService transcriptionService,
        SettingsViewModel settings)
    {
        _transcriptionService = transcriptionService;
        _settings = settings;
        LoadModels();
        // The default alias is a fixed one (MOSS, ~700 MB); this bounds the
        // startup probe to that single model. A model not on disk is not loaded.
        RequestCapabilityProbe(ModelAlias);
    }

    private void LoadModels()
    {
        AvailableModels.Clear();
        foreach (var model in ModelStore.Catalog)
        {
            AvailableModels.Add(model.Alias);
        }
    }

    partial void OnModelAliasChanged(string value) => RequestCapabilityProbe(value);

    /// <summary>
    /// Reads the capabilities of the selected model, once per alias, so the
    /// family-extension controls shown match what the model actually has.
    /// </summary>
    /// <remarks>
    /// Nothing is hidden for a model that was not checked: an unloaded model
    /// shows every extension, because a wrong guess would hide one that works.
    /// The architecture gates the family; <c>SupportsStreaming</c> gates the
    /// group, so an offline Parakeet (same architecture as a streaming one) does
    /// not show the buffered-streaming controls.
    /// </remarks>
    private void RequestCapabilityProbe(string alias)
    {
        if (_capabilityCache.TryGetValue(alias, out ModelCapabilitySnapshot? cached))
        {
            ApplyCapabilities(cached);
            CapabilityStatus = string.Empty;
            return;
        }

        ModelDescriptor? descriptor = ModelStore.Catalog.FirstOrDefault(m => m.Alias == alias);
        if (descriptor is null || !ModelStore.IsCached(descriptor))
        {
            ApplyCapabilities(null);
            CapabilityStatus = descriptor is null
                ? string.Empty
                : $"{alias} is not downloaded, so its capabilities are unknown. Every option is shown until it is checked.";
            return;
        }

        if (_probing)
        {
            _pendingProbeAlias = alias;
            return;
        }

        _ = ProbeAsync(alias, descriptor);
    }

    private async System.Threading.Tasks.Task ProbeAsync(string alias, ModelDescriptor descriptor)
    {
        _probing = true;
        IsCheckingCapabilities = true;
        CapabilityStatus = $"Checking what {alias} supports...";
        try
        {
            string path = ModelStore.CachedPath(descriptor);
            ModelCapabilitySnapshot snapshot = await System.Threading.Tasks.Task.Run(
                () => ModelCapabilityProbe.Probe(path)).ConfigureAwait(true);

            _capabilityCache[alias] = snapshot;
            if (string.Equals(ModelAlias, alias, StringComparison.Ordinal))
            {
                ApplyCapabilities(snapshot);
                CapabilityStatus = string.Empty;
            }
        }
        catch (Exception ex)
        {
            if (string.Equals(ModelAlias, alias, StringComparison.Ordinal))
            {
                ApplyCapabilities(null);
                CapabilityStatus = $"Could not check {alias}: {ex.Message}. Every option is shown.";
            }
        }
        finally
        {
            _probing = false;
            IsCheckingCapabilities = false;

            string? pending = _pendingProbeAlias;
            _pendingProbeAlias = null;
            if (pending is not null && !string.Equals(pending, alias, StringComparison.Ordinal))
            {
                RequestCapabilityProbe(pending);
            }
        }
    }

    private void ApplyCapabilities(ModelCapabilitySnapshot? capabilities)
    {
        _selectedCapabilities = capabilities;
        OnPropertyChanged(nameof(ShowFamilyExtensions));
        OnPropertyChanged(nameof(ShowMoonshineExtensions));
        OnPropertyChanged(nameof(ShowParakeetExtensions));
        OnPropertyChanged(nameof(ShowSortformerExtensions));
        OnPropertyChanged(nameof(ShowVoxtralExtensions));
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task StartStreamingAsync()
    {
        if (IsStreaming)
        {
            return;
        }

        IsStreaming = true;
        StatusMessage = "Streaming...";
        CommittedText = string.Empty;
        TentativeText = string.Empty;
        _cts = new CancellationTokenSource();

        try
        {
            var options = new StreamOptions
            {
                // The backend and device chosen in Settings. These were
                // displayed but read by nothing, so the pickers did nothing.
                BackendRequest = _settings.SelectedBackend,
                Device = _settings.SelectedDevice,
                CommitPolicy = SelectedCommitPolicy,
                StablePrefixAgreement = (uint?)StablePrefixAgreement,
                MoonshineMinDecodeIntervalMs = MoonshineMinDecodeIntervalMs,
                ParakeetAttContextRight = ParakeetAttContextRight,
                ParakeetLeftMs = ParakeetLeftMs,
                ParakeetChunkMs = ParakeetChunkMs,
                ParakeetRightMs = ParakeetRightMs,
                SortformerPreset = SelectedSortformerPreset,
                VoxtralNumDelayTokens = VoxtralNumDelayTokens,
                VoxtralMinDecodeIntervalMs = VoxtralMinDecodeIntervalMs,
            };

            await foreach (var update in _transcriptionService.StreamTranscribeAsync(
                ModelAlias, options, _cts.Token))
            {
                CommittedText = update.CommittedText;
                TentativeText = update.TentativeText;
            }

            StatusMessage = "Streaming complete.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Streaming cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsStreaming = false;
        }
    }

    [RelayCommand]
    private void StopStreaming()
    {
        _cts?.Cancel();
        StatusMessage = "Stopping...";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
