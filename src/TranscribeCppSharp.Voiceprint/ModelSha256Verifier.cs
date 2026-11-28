namespace TranscribeCppSharp.Voiceprint;

using System;
using System.IO;
using System.Security.Cryptography;

/// <summary>Verifies actual local model file SHA-256. No `nl`; no `model sha` inference.</summary>
public static class ModelSha256Verifier
{
    /// <summary>Computes lowercase hex SHA-256 of a file.</summary>
    public static string ComputeSha256(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>Verifies expected SHA-256 (hex, case-insensitive) against actual file bytes.</summary>
    public static bool Verify(string path, string expectedSha256Hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256Hex);
        ValidateHexSha256(expectedSha256Hex);
        string actual = ComputeSha256(path);
        return string.Equals(actual, expectedSha256Hex, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Validates 64 hex chars; invalid is `ArgumentException`, mismatch is false.</summary>
    public static void ValidateHexSha256(string sha256Hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256Hex);
        if (sha256Hex.Length != 64)
        {
            throw new ArgumentException("SHA-256 must be 64 hex characters.", nameof(sha256Hex));
        }

        foreach (char c in sha256Hex)
        {
            bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!hex)
            {
                throw new ArgumentException("SHA-256 must be hex.", nameof(sha256Hex));
            }
        }
    }
}
