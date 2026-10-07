using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TranscribeCppSharp.Interop;
using TranscribeCppSharp.Models;

namespace TranscribeCppSharp.Ui.ViewModels;

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
            BackendError = $"Compute backends unavailable: {ex.Message}";
            return;
        }

        try
        {
            BackendVersion = Backends.Version;
        }
        catch (Exception ex) when (ex is TranscribeException or DllNotFoundException or EntryPointNotFoundException)
        {
            BackendError = $"Backends initialised, but the version could not be read: {ex.Message}";
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
            BackendError = $"{Devices.Count} device(s) listed, then enumeration failed: {ex.Message}";
        }
    }
}
