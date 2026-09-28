#nullable enable

using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// One collection for the test classes that decode through ffmpeg.
/// </summary>
/// <remarks>
/// ffmpeg's decoded audio is staged in the system temp directory while it is
/// being read, and that directory is shared by the whole test run. Two of these
/// classes assert on the files in it, which is only meaningful if no other
/// class is creating one at the same moment — so they are serialised together
/// rather than left to xUnit's default parallelism.
/// </remarks>
[CollectionDefinition("AudioStaging", DisableParallelization = true)]
public static class AudioStagingTestGroup
{
}
