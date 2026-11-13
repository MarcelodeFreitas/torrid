using Torrid.Core.Overlay;

namespace Torrid.Tests;

public class OverlayPlacementTests
{
    [Fact]
    public void TopRight_HugsTheRightEdgeWithTheMargin()
    {
        var (left, top) = OverlayPlacement.TopRight(0, 0, 1920, 200, 16);

        Assert.Equal(1704, left);
        Assert.Equal(16, top);
    }

    [Fact]
    public void TopRight_HonoursTheWorkAreaOffset()
    {
        var (left, top) = OverlayPlacement.TopRight(100, 40, 1920, 200, 16);

        Assert.Equal(1804, left);
        Assert.Equal(56, top);
    }

    [Fact]
    public void TopRight_WiderThanTheWorkArea_ClampsToTheLeftEdge()
    {
        var (left, _) = OverlayPlacement.TopRight(100, 0, 150, 200, 16);

        Assert.Equal(100, left);
    }
}
