using TranscribeCppSharp.Interop;
using Xunit;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// The two native statuses that leave a partial result behind. Treating them as
/// exceptions threw away a transcript the native call had already produced.
/// </summary>
public class ResultBearingStatusTests
{
    [Theory]
    [InlineData(Status.ErrOutputTruncated)]
    [InlineData(Status.ErrOutputRepetition)]
    public void TheOutputStopStatusesLeaveAResult(Status status)
        => Assert.True(Session.IsResultBearing(status));

    [Theory]
    [InlineData(Status.Ok)]
    [InlineData(Status.ErrOom)]
    [InlineData(Status.ErrBackend)]
    [InlineData(Status.ErrAborted)]
    [InlineData(Status.ErrUnsupportedTimestamps)]
    public void OtherStatusesAreNotResultBearing(Status status)
        => Assert.False(Session.IsResultBearing(status));
}
