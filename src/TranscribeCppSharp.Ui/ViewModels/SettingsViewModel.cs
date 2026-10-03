using CommunityToolkit.Mvvm.ComponentModel;
using TranscribeCppSharp;

namespace TranscribeCppSharp.Ui.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private BackendRequest _selectedBackend = BackendRequest.Auto;

    [ObservableProperty]
    private string _cacheDirectory = string.Empty;

    [ObservableProperty]
    private string _backendVersion = string.Empty;

    [ObservableProperty]
    private ObservableCollection<BackendDevice> _devices = new();

    public ObservableCollection<BackendRequest> AvailableBackends { get; } = new()
    {
        BackendRequest.Auto,
        BackendRequest.Cpu,
        BackendRequest.CpuAccel,
        BackendRequest.Metal,
        BackendRequest.Vulkan,
        BackendRequest.Cuda,
        BackendRequest.Rocm
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
