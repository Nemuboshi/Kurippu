using Kurippu.Core;

namespace Kurippu.Core.Tests;

public class PlaybackTimelineTests
{
    [Fact]
    public void LoopWithOnlyEndMarkReturnsToBeginning()
    {
        Assert.Equal(0, PlaybackTimeline.LoopTarget(12, "", "00:10", 60));
        Assert.Null(PlaybackTimeline.LoopTarget(10, "", "00:10", 60));
    }

    [Fact]
    public void LoopRespectsStartAndDoesNotWrapEmptyInterval()
    {
        Assert.Equal(5, PlaybackTimeline.LoopTarget(11, "5", "10", 60));
        Assert.Null(PlaybackTimeline.LoopTarget(11, "10", "5", 60));
    }

    [Fact]
    public void ResumeRewindsFromEndOrPastLoopBoundary()
    {
        Assert.Equal(0, PlaybackTimeline.ResumeTarget(59.99, 60, false, "", ""));
        Assert.Equal(5, PlaybackTimeline.ResumeTarget(12, 60, true, "5", "10"));
        Assert.Null(PlaybackTimeline.ResumeTarget(7, 60, true, "5", "10"));
    }

    [Fact]
    public void MarkEndAtDurationClearsEndAndCrossingMarksClearOtherSide()
    {
        Assert.Equal("", PlaybackTimeline.EndMark(60, 60));
        Assert.Equal(("00:08.000", ""), PlaybackTimeline.MarkStart(8, "5", "7"));
        Assert.Equal(("", "00:03.000"), PlaybackTimeline.MarkEnd(3, "5", "8", 60));
    }

    [Theory]
    [InlineData("", "", 0, 60)]
    [InlineData("10", "", 10, 60)]
    [InlineData("", "30", 0, 30)]
    [InlineData("10", "30", 10, 30)]
    [InlineData("-1", "80", 0, 60)]
    [InlineData("40", "20", 0, 60)]
    public void HighlightShowsOnlyValidSelectedInterval(string start, string end, double expectedStart, double expectedEnd)
    {
        Assert.Equal((expectedStart, expectedEnd), PlaybackTimeline.HighlightBounds(60, start, end));
    }

    [Fact]
    public void ZeroDurationHasNoHighlight()
    {
        Assert.Equal((0d, 0d), PlaybackTimeline.HighlightBounds(0, "", ""));
    }
}
