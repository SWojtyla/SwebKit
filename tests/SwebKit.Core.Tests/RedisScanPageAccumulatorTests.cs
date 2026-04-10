using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public sealed class RedisScanPageAccumulatorTests
{
    [Fact]
    public void AppendBatch_WhenScanBatchOvershootsPageSize_BuffersOverflowForNextPage()
    {
        var accumulator = new RedisScanPageAccumulator(pageSize: 3);

        var firstPage = accumulator.AppendBatch(["alpha", "beta", "gamma", "delta", "epsilon"], currentPageCount: 0);

        Assert.Equal(["alpha", "beta", "gamma"], firstPage.VisibleKeys);
        Assert.True(firstPage.IsPageFull);
        Assert.True(accumulator.HasOverflow);
        Assert.Equal(2, accumulator.OverflowCount);

        var nextPage = accumulator.TakeOverflowPage(currentPageCount: 0);

        Assert.Equal(["delta", "epsilon"], nextPage);
        Assert.False(accumulator.HasOverflow);
    }

    [Fact]
    public void AppendBatch_PreservesOverflowOrderAcrossLaterPages()
    {
        var accumulator = new RedisScanPageAccumulator(pageSize: 2);

        var firstPage = accumulator.AppendBatch(["alpha", "beta", "gamma"], currentPageCount: 0);
        var carriedForward = accumulator.TakeOverflowPage(currentPageCount: 0).ToList();
        var secondPage = accumulator.AppendBatch(["gamma", "delta", "epsilon"], currentPageCount: carriedForward.Count);

        Assert.Equal(["alpha", "beta"], firstPage.VisibleKeys);
        Assert.Equal(["gamma"], carriedForward);
        Assert.Equal(["delta"], secondPage.VisibleKeys);
        Assert.True(accumulator.HasOverflow);
        Assert.Equal(["epsilon"], accumulator.TakeOverflowPage(currentPageCount: 0));
    }

    [Fact]
    public void RegisterVisibleKey_PreventsLaterDuplicateFromBeingAdded()
    {
        var accumulator = new RedisScanPageAccumulator(pageSize: 2);
        accumulator.RegisterVisibleKey("renamed:key");

        var page = accumulator.AppendBatch(["renamed:key", "other:key"], currentPageCount: 0);

        Assert.Equal(["other:key"], page.VisibleKeys);
    }
}