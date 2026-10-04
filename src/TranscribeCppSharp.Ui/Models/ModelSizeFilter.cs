namespace TranscribeCppSharp.Ui.Models;

/// <summary>Size bands offered as a quick filter over the catalogue.</summary>
public enum ModelSizeFilter
{
    /// <summary>No size restriction.</summary>
    Any = 0,

    /// <summary>Below 500 MB.</summary>
    Small = 1,

    /// <summary>From 500 MB up to 1.5 GB.</summary>
    Medium = 2,

    /// <summary>Above 1.5 GB.</summary>
    Large = 3,
}

/// <summary>
/// One entry of the size quick filter.
/// </summary>
/// <remarks>
/// The split points come from what the manifest actually contains, not from a
/// rule of thumb: sizes there run from 33 MB to 16 GB, and the bands below are
/// the ones that leave a useful number of models on each side.
/// </remarks>
/// <param name="Value">The band this entry selects.</param>
/// <param name="Label">Text shown in the picker.</param>
public sealed record ModelSizeFilterOption(ModelSizeFilter Value, string Label)
{
    /// <summary>Lower bound in bytes, inclusive; 0 for the open band.</summary>
    public long MinBytes => Value switch
    {
        ModelSizeFilter.Small => 0,
        ModelSizeFilter.Medium => 500L * 1024 * 1024,
        ModelSizeFilter.Large => 1536L * 1024 * 1024,
        _ => 0,
    };

    /// <summary>Upper bound in bytes, exclusive; -1 for the open band.</summary>
    public long MaxBytes => Value switch
    {
        ModelSizeFilter.Small => 500L * 1024 * 1024,
        ModelSizeFilter.Medium => 1536L * 1024 * 1024,
        _ => -1,
    };

    /// <summary>Whether a size falls in this band.</summary>
    /// <param name="bytes">Size in bytes.</param>
    /// <returns>true when the size is inside the band.</returns>
    public bool Contains(long bytes)
    {
        if (Value == ModelSizeFilter.Any)
        {
            return true;
        }

        return bytes >= MinBytes && (MaxBytes < 0 || bytes < MaxBytes);
    }
}