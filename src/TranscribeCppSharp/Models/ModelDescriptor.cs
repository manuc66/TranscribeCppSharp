using System;

namespace TranscribeCppSharp.Models;

/// <summary>
/// One entry of the curated model manifest: a GGUF published on HuggingFace at a
/// pinned revision, with the checksum needed to verify the download.
/// </summary>
/// <param name="Alias">Short name accepted wherever a model is expected.</param>
/// <param name="Repo">HuggingFace repository, "owner/name".</param>
/// <param name="Revision">Pinned commit; the manifest never floats.</param>
/// <param name="License">SPDX-style license identifier.</param>
/// <param name="LicenseUrl">Where the license text lives.</param>
/// <param name="Quant">Quantization of this entry's file, e.g. "Q5_K_M".</param>
/// <param name="File">File name inside <paramref name="Repo"/>.</param>
/// <param name="Sha256">Expected checksum of the downloaded file.</param>
/// <param name="Size">Size in bytes, or 0 when the manifest does not state it.</param>
public sealed record ModelDescriptor(
    string Alias,
    string Repo,
    string Revision,
    string License,
    string LicenseUrl,
    string Quant,
    string File,
    string Sha256,
    long Size)
{
    /// <summary>
    /// The URL the file is downloaded from. Pinned to <see cref="Revision"/>, so
    /// the same bytes are fetched every time.
    /// </summary>
    public string DownloadUrl => $"https://huggingface.co/{Repo}/resolve/{Revision}/{File}";

    /// <summary>Model card for this repository.</summary>
    public string RepoUrl => $"https://huggingface.co/{Repo}";

    /// <summary>
    /// True when the license terms forbid commercial use. The manifest uses
    /// SPDX ids, so this is a text test on the identifier, not a license list.
    /// </summary>
    public bool IsNonCommercialLicense
        => License.Contains("-nc", StringComparison.OrdinalIgnoreCase)
        || License.Contains("noncommercial", StringComparison.OrdinalIgnoreCase)
        || License.Contains("non-commercial", StringComparison.OrdinalIgnoreCase);
}
