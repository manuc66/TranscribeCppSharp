using Avalonia.Platform.Storage;

namespace TranscribeCppSharp.UI.Services;

/// <summary>
/// Opens a picker where the last one was used, and records the folder a picked
/// file came from.
/// </summary>
/// <remarks>
/// The note itself lives in <see cref="UiSettings"/>. This class only turns it
/// into the folder a picker wants and back from the path a picker returned. A
/// stored folder that is gone, or one the platform cannot resolve, yields null
/// so the picker keeps its own default rather than failing.
/// </remarks>
internal static class PickerFolder
{
    /// <summary>
    /// The folder the last picker used, or null when there is none or the
    /// platform cannot resolve it.
    /// </summary>
    /// <param name="provider">The window's storage provider.</param>
    public static async Task<IStorageFolder?> StartAsync(IStorageProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        string? path = UiSettings.LastDirectory();
        if (path is null)
        {
            return null;
        }

        try
        {
            return await provider.TryGetFolderFromPathAsync(path);
        }
        catch (Exception ex) when (ex is ArgumentException or UriFormatException or IOException)
        {
            // A hand-edited settings file can hold a path the URI parser
            // rejects; that is the same as no answer, not an app failure.
            return null;
        }
    }

    /// <summary>The folder a picked file sits in, or null when the path has none.</summary>
    public static string? DirectoryOf(string? pickedPath)
    {
        if (string.IsNullOrWhiteSpace(pickedPath))
        {
            return null;
        }

        try
        {
            return Path.GetDirectoryName(pickedPath);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Records the folder a picked file sits in.</summary>
    public static void Remember(string? pickedPath)
    {
        string? directory = DirectoryOf(pickedPath);
        if (!string.IsNullOrEmpty(directory))
        {
            UiSettings.SetLastDirectory(directory);
        }
    }
}
