using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.UI.Services;

namespace TranscribeCppSharp.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private BackendRequest _selectedBackend = BackendRequest.BackendAuto;

    /// <summary>
    /// The compute device to pin, or null to let the backend resolve one.
    /// </summary>
    /// <remarks>
    /// A device is a stricter choice than a backend: it never falls back. Left
    /// null, which is what an unset picker means, the backend decides — Auto
    /// probing discrete GPUs before integrated ones.
    /// </remarks>
    [ObservableProperty]
    private BackendDevice? _selectedDevice;

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

    public ObservableCollection<BackendRequest> AvailableBackends { get; } = new()
    {
        BackendRequest.BackendAuto,
        BackendRequest.BackendCpu,
        BackendRequest.BackendCpuAccel,
        BackendRequest.BackendMetal,
        BackendRequest.BackendVulkan,
        BackendRequest.BackendCuda,
        BackendRequest.BackendRocm
    };

    public SettingsViewModel()
    {
        // From ModelStore, not built here. This used to be
        // LocalApplicationData/TranscribeCppSharp/models, which is the Windows
        // path only: on Linux and macOS it pointed at a directory that does not
        // exist while the models sat in ~/.cache or ~/Library/Caches. A settings
        // pane that names the wrong directory is worse than one that names none.
        CacheDirectory = ModelStore.CacheRoot();

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
            foreach (var device in Backends.EnumerateDevices())
            {
                Devices.Add(device);
            }
        }
        catch (Exception ex) when (ex is TranscribeException or DllNotFoundException or EntryPointNotFoundException)
        {
            // The device list is incomplete but the backend is usable, so this is
            // reported on its own rather than as a failure to start.
            UiLog.Failure("device enumeration", ex);
            BackendError = $"{Devices.Count} device(s) listed, then enumeration failed: {ex.Message} (logged to {UiLog.FilePath})";
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
            List<string> available = AvailableBackends
                .Where(backend => backend != BackendRequest.BackendAuto && Backends.BackendAvailable(backend))
                .Select(BackendLabel)
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
                    : $"{BackendLabel(SelectedBackend)} resolves to: {MemoryPreflight.Describe(target)}.";
    }

    partial void OnSelectedBackendChanged(BackendRequest value) => UpdateTargetHint();

    partial void OnSelectedDeviceChanged(BackendDevice? value) => UpdateTargetHint();

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

            Devices.Clear();
            foreach (BackendDevice device in fresh)
            {
                Devices.Add(device);
            }

            SelectedDevice = keep is null
                ? null
                : Devices.FirstOrDefault(d => d.Handle == keep.Handle && d.Handle != IntPtr.Zero)
                    ?? Devices.FirstOrDefault(d => !string.IsNullOrEmpty(d.DeviceId) && d.DeviceId == keep.DeviceId);

            UpdateBackendAvailability();
        }
        catch (Exception ex) when (ex is TranscribeException or DllNotFoundException or EntryPointNotFoundException)
        {
            UiLog.Failure("device enumeration", ex);
            BackendError = $"The device list could not be refreshed: {ex.Message} (logged to {UiLog.FilePath})";
        }
    }

    private static string BackendLabel(BackendRequest backend) => backend switch
    {
        BackendRequest.BackendCpu => "CPU",
        BackendRequest.BackendCpuAccel => "CPU (accelerated)",
        BackendRequest.BackendMetal => "Metal",
        BackendRequest.BackendVulkan => "Vulkan",
        BackendRequest.BackendCuda => "CUDA",
        BackendRequest.BackendRocm => "ROCm",
        _ => backend.ToString(),
    };
}
