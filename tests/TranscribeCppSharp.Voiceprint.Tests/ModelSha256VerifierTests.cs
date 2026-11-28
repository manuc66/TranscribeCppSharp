namespace TranscribeCppSharp.Voiceprint.Tests;

using System;
using System.IO;
using TranscribeCppSharp.Voiceprint;
using Xunit;

public sealed class ModelSha256VerifierTests
{
    [Fact]
    public void ComputesAndVerifiesActualFileSha256()
    {
        string path = Path.Combine(Path.GetTempPath(), "tcs-model-sha-" + Guid.NewGuid().ToString("N") + ".onnx");
        byte[] bytes = [1, 2, 3, 4, 5];

        try
        {
            File.WriteAllBytes(path, bytes);

            string sha = ModelSha256Verifier.ComputeSha256(path);
            Assert.Equal("74F81FE167D99B4CB41D6D0CCDA82278CAEE9F3E2F25D5E5A3936FF3DCEC60D0".ToLowerInvariant(), sha);
            Assert.True(ModelSha256Verifier.Verify(path, sha));
            Assert.True(ModelSha256Verifier.Verify(path, sha.ToUpperInvariant()));
            Assert.False(ModelSha256Verifier.Verify(path, new string('0', 64)));
            Assert.Throws<ArgumentException>(() => ModelSha256Verifier.Verify(path, "bad"));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
