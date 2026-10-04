using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TranscribeCppSharp;
using TranscribeCppSharp.Interop;

namespace TranscribeCppSharp.Ui.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private BackendRequest _selectedBackend = BackendRequest.BackendAuto;

    [ObservableProperty]
    private string _cacheDirectory = string.Empty;

    [ObservableProperty]
    private string _backendVersion = string.Empty;

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
        CacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TranscribeCppSharp", "models");

        try
        {
            Backends.InitDefault();
            BackendVersion = Backends.Version;
            foreach (var device in Backends.EnumerateDevices())
            {
                Devices.Add(device);
            }
        }
        catch
        {
            BackendVersion = "Not initialized";
        }
    }
}
