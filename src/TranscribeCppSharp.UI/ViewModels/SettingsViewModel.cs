using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;

namespace TranscribeCppSharp.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    /// <summary>
    /// Whether this instance reads and writes the compute choice in
    /// <see cref="UiSettings"/>. The app turns it on; the default is off so a test
    /// that constructs the tab cannot be steered by, or write to, the developer's
    /// real settings file.
    /// </summary>
    private readonly bool _rememberSelection;

    /// <summary>True while a saved choice is being applied, so it is not written back.</summary>
    private bool _restoringSelection;

    [ObservableProperty]
    private BackendRequest _selectedBackend = BackendRequest.BackendAuto;

    /// <summary>
    /// The picker's items: Auto first, then every device the last enumeration
    /// reported.
    /// </summary>
    /// <remarks>
    /// Auto is an item rather than the absence of a selection. A ComboBox bound to
    /// a nullable device has no row to show for null, so once a device was picked
    /// the reader had no way back to automatic.
    /// </remarks>
    public ObservableCollection<DeviceOption> DeviceOptions { get; } =
        new() { DeviceOption.Automatic };

    private DeviceOption _selectedDeviceOption = DeviceOption.Automatic;

    /// <summary>The choice selected in the device picker.</summary>
    public DeviceOption SelectedDeviceOption
    {
        get => _selectedDeviceOption;
        set
        {
            DeviceOption next = value ?? DeviceOption.Automatic;
            if (SetProperty(ref _selectedDeviceOption, next))
            {
                OnPropertyChanged(nameof(SelectedDevice));
                UpdateTargetHint();
                RememberComputeSelection();
            }
        }
    }

    /// <summary>
    /// The compute device to pin, or null to let the backend resolve one.
    /// </summary>
    /// <remarks>
    /// A device is a stricter choice than a backend: it never falls back. When
    /// this is null — the Auto item — the backend decides, probing discrete GPUs
    /// before integrated ones.
    /// </remarks>
    public BackendDevice? SelectedDevice => SelectedDeviceOption.Device;

    /// <summary>
    /// True while <see cref="Devices"/> is being rebuilt in one go, so the
    /// per-item collection event does not rebuild the picker from a half-filled
    /// list.
    /// </summary>
    private bool _bulkDeviceEdit;

    [ObservableProperty]
    private string _cacheDirectory = string.Empty;

    [ObservableProperty]
    private string _backendVersion = string.Empty;

    /// <summary>
    /// What the compute controls mean here: Auto is the default and resolves a
    /// backend by itself. Filled after enumeration so it can name the backends
    /// this build and machine report.
    /// </summary>
    [ObservableProperty]
    private string _backendAvailabilityHint = BaselineBackendHint;

    /// <summary>
    /// Which device this configuration resolves to, with its live free figure.
    /// "Auto resolves to" is the native policy, not a guess made here.
    /// </summary>
    [ObservableProperty]
    private string _backendTargetHint = string.Empty;

    private const string BaselineBackendHint =
        "Auto is the default: the GPU is used when one initializes, otherwise the CPU. "
        + "These controls are optional — use them to force a backend or pin a device.";

    /// <summary>
    /// Why the backend list is unavailable, when it is.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="BackendVersion"/> on purpose. The two fail for
    /// different reasons and the old code reported both as "Not initialized",
    /// which was wrong whenever enumeration failed but initialisation had
    /// already succeeded — and it left the version blank in that case too.
    /// </remarks>
    [ObservableProperty]
    private string _backendError = string.Empty;

    [ObservableProperty]
    private ObservableCollection<BackendDevice> _devices = new();

    /// <summary>
    /// The app's own version, which is the wrapper's SemVer.
    /// </summary>
    /// <remarks>
    /// Read from the assembly rather than written here: the UI project takes its
    /// version from <c>build/WRAPPER_VERSION</c> through Directory.Build.props, so
    /// this is the same number the release archive is named after and cannot
    /// drift from it. The native library's version is <see cref="BackendVersion"/>.
    /// </remarks>
    public string AppVersion { get; } = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    /// <summary>
    /// The backend choices, in the order the picker shows them. Labels are written
    /// here because the enum members read "BackendAuto", "BackendCpu" and so on.
    /// </summary>
    public ObservableCollection<BackendOption> BackendOptions { get; } = new()
    {
        new(BackendRequest.BackendAuto, "Auto"),
        new(BackendRequest.BackendCpu, "CPU"),
        new(BackendRequest.BackendCpuAccel, "CPU (accelerated)"),
        new(BackendRequest.BackendMetal, "Metal"),
        new(BackendRequest.BackendVulkan, "Vulkan"),
        new(BackendRequest.BackendCuda, "CUDA"),
        new(BackendRequest.BackendRocm, "ROCm"),
    };

    /// <summary>
    /// The backend choice shown in the picker. Reading it maps
    /// <see cref="SelectedBackend"/> back to its labelled item; writing it sets the
    /// enum the run uses.
    /// </summary>
    public BackendOption SelectedBackendOption
    {
        get => BackendOptions.FirstOrDefault(option => option.Value == SelectedBackend)
            ?? BackendOptions[0];
        set
        {
            if (value is not null)
            {
                SelectedBackend = value.Value;
            }
        }
    }

    public SettingsViewModel(bool rememberSelection = false)
    {
        _rememberSelection = rememberSelection;

        // From ModelStore, not built here. This used to be
        // LocalApplicationData/TranscribeCppSharp/models, which is the Windows
        // path only: on Linux and macOS it pointed at a directory that does not
        // exist while the models sat in ~/.cache or ~/Library/Caches. A settings
        // pane that names the wrong directory is worse than one that names none.
        CacheDirectory = ModelStore.CacheRoot();

        // The tests and any future caller can edit Devices directly; the picker
        // follows, because a raw list that no longer matches its choices is how a
        // device ends up selectable but absent.
        Devices.CollectionChanged += (_, _) => OnDevicesChanged();

        try
        {
            Backends.InitDefault();
        }
        catch (Exception ex) when (ex is TranscribeException or DllNotFoundException or EntryPointNotFoundException)
        {
            UiLog.Failure("backend initialisation", ex);
            BackendError = $"Compute backends unavailable: {ex.Message} (logged to {UiLog.FilePath})";
            return;
        }

        try
        {
            BackendVersion = Backends.Version;
        }
        catch (Exception ex) when (ex is TranscribeException or DllNotFoundException or EntryPointNotFoundException)
        {
            UiLog.Failure("backend version read", ex);
            BackendError = $"Backends initialised, but the version could not be read: {ex.Message} (logged to {UiLog.FilePath})";
        }

        try
        {
            _bulkDeviceEdit = true;
            try
            {
                foreach (var device in Backends.EnumerateDevices())
                {
                    Devices.Add(device);
                }
            }
            finally
            {
                _bulkDeviceEdit = false;
            }
        }
        catch (Exception ex) when (ex is TranscribeException or DllNotFoundException or EntryPointNotFoundException)
        {
            // The device list is incomplete but the backend is usable, so this is
            // reported on its own rather than as a failure to start.
            UiLog.Failure("device enumeration", ex);
            BackendError = $"{Devices.Count} device(s) listed, then enumeration failed: {ex.Message} (logged to {UiLog.FilePath})";
        }

        RebuildDeviceOptions();
        if (_rememberSelection)
        {
            RestoreComputeSelection();
        }

        UpdateBackendAvailability();
    }

    /// <summary>
    /// Names the backends this build reports as available, after the baseline that
    /// says the panel is optional. `BackendAvailable` answers from the native
    /// library, so this can fail like any other backend call and is not worth an
    /// error of its own: the baseline is still true without the list.
    /// </summary>
    private void UpdateBackendAvailability()
    {
        try
        {
            List<string> available = BackendOptions
                .Where(option => option.Value != BackendRequest.BackendAuto
                    && Backends.BackendAvailable(option.Value))
                .Select(option => option.Label)
                .ToList();

            BackendAvailabilityHint = available.Count > 0
                ? $"{BaselineBackendHint} Available on this machine: {string.Join(", ", available)}."
                : $"{BaselineBackendHint} This build reports no usable device.";
        }
        catch (Exception ex) when (ex is TranscribeException or DllNotFoundException or EntryPointNotFoundException)
        {
            // The baseline is the part that matters; the list is a convenience.
        }

        UpdateTargetHint();
    }

    /// <summary>
    /// States which device the current backend/device choice resolves to, and its
    /// live free memory. It is the answer to "what will actually run?" before a
    /// model is loaded.
    /// </summary>
    private void UpdateTargetHint()
    {
        BackendDevice? target = MemoryPreflight.Target(Devices, SelectedBackend, SelectedDevice);
        BackendTargetHint = target is null
            ? "No compute device reported, so there is nothing to say about memory."
            : SelectedDevice is not null
                ? $"Pinned device: {MemoryPreflight.Describe(target)}."
                : SelectedBackend == BackendRequest.BackendAuto
                    ? $"Auto resolves to: {MemoryPreflight.Describe(target)}."
                    : $"{SelectedBackendOption.Label} resolves to: {MemoryPreflight.Describe(target)}.";
    }

    partial void OnSelectedBackendChanged(BackendRequest value)
    {
        OnPropertyChanged(nameof(SelectedBackendOption));
        UpdateTargetHint();
        RememberComputeSelection();
    }

    /// <summary>
    /// Applies the saved backend and pins the saved device when it is still
    /// enumerated. A device id that is no longer present leaves Auto selected and
    /// is left in the file, so a device that comes back is used again.
    /// </summary>
    private void RestoreComputeSelection()
    {
        ComputeSelection saved = UiSettings.LoadComputeSelection();

        _restoringSelection = true;
        try
        {
            SelectedBackend = saved.Backend;
            SelectedDeviceOption = saved.DeviceId is null
                ? DeviceOption.Automatic
                : DeviceOptions.FirstOrDefault(option =>
                    option.Device is { } device
                    && !string.IsNullOrEmpty(device.DeviceId)
                    && string.Equals(device.DeviceId, saved.DeviceId, StringComparison.Ordinal))
                    ?? DeviceOption.Automatic;
        }
        finally
        {
            _restoringSelection = false;
        }
    }

    /// <summary>Writes the current choice, if this instance is the app's and not a test's.</summary>
    private void RememberComputeSelection()
    {
        if (!_rememberSelection || _restoringSelection)
        {
            return;
        }

        UiSettings.SaveComputeSelection(SelectedBackend, SelectedDevice?.DeviceId);
    }

    private void OnDevicesChanged()
    {
        if (_bulkDeviceEdit)
        {
            return;
        }

        RebuildDeviceOptions();
    }

    /// <summary>
    /// Rebuilds the picker from <see cref="Devices"/>, keeping
    /// <paramref name="keep"/> selected when it is still present.
    /// </summary>
    /// <remarks>
    /// The selected device is matched by runtime handle first and stable device id
    /// second, the same rule <see cref="RefreshDevices"/> uses. A device that is
    /// gone leaves Auto selected rather than keeping a stale entry.
    /// </remarks>
    private void RebuildDeviceOptions(BackendDevice? keep = null)
    {
        keep ??= SelectedDevice;

        DeviceOptions.Clear();
        DeviceOptions.Add(DeviceOption.Automatic);
        foreach (BackendDevice device in Devices)
        {
            DeviceOptions.Add(new DeviceOption(device));
        }

        SelectedDeviceOption = keep is null
            ? DeviceOption.Automatic
            : DeviceOptions.FirstOrDefault(option => SameDevice(option.Device, keep))
                ?? DeviceOption.Automatic;
    }

    private static bool SameDevice(BackendDevice? left, BackendDevice? right)
        => left is not null
        && right is not null
        && ((left.Handle == right.Handle && left.Handle != IntPtr.Zero)
            || (!string.IsNullOrEmpty(left.DeviceId) && left.DeviceId == right.DeviceId));

    /// <summary>
    /// Re-reads the device list and its live free-memory figures. Keeps a pinned
    /// device selected when it is still present, matching the runtime handle first
    /// and the stable device id second.
    /// </summary>
    [RelayCommand]
    private void RefreshDevices()
    {
        try
        {
            BackendDevice? keep = SelectedDevice;
            IReadOnlyList<BackendDevice> fresh = Backends.EnumerateDevices();

            _bulkDeviceEdit = true;
            try
            {
                Devices.Clear();
                foreach (BackendDevice device in fresh)
                {
                    Devices.Add(device);
                }
            }
            finally
            {
                _bulkDeviceEdit = false;
            }

            RebuildDeviceOptions(keep);
            UpdateBackendAvailability();
        }
        catch (Exception ex) when (ex is TranscribeException or DllNotFoundException or EntryPointNotFoundException)
        {
            UiLog.Failure("device enumeration", ex);
            BackendError = $"The device list could not be refreshed: {ex.Message} (logged to {UiLog.FilePath})";
        }
    }
}
