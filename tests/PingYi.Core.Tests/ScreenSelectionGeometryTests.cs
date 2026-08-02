using PingYi.Core;

namespace PingYi.Core.Tests;

public sealed class ScreenSelectionGeometryTests
{
    [Fact]
    public void NormalizeAndClamp_PreservesNegativeDesktopCoordinatesAcrossDisplays()
    {
        var desktop = new PixelRect(-1920, -200, 4480, 1640);

        var selection = ScreenSelectionGeometry.NormalizeAndClamp(
            startX: 320,
            startY: 900,
            endX: -600,
            endY: 100,
            desktop);

        Assert.Equal(new PixelRect(-600, 100, 920, 800), selection);
        Assert.Equal(
            new PixelRect(1320, 300, 920, 800),
            ScreenSelectionGeometry.ToCaptureRelative(selection, desktop));
    }

    [Fact]
    public void Intersect_ReturnsPerDisplaySliceOfCrossScreenSelection()
    {
        var selection = new PixelRect(-300, 100, 900, 500);
        var leftDisplay = new PixelRect(-1920, 0, 1920, 1080);
        var rightDisplay = new PixelRect(0, 0, 2560, 1440);

        Assert.Equal(
            new PixelRect(-300, 100, 300, 500),
            ScreenSelectionGeometry.Intersect(selection, leftDisplay));
        Assert.Equal(
            new PixelRect(0, 100, 600, 500),
            ScreenSelectionGeometry.Intersect(selection, rightDisplay));
    }

    [Fact]
    public void NormalizeAndClamp_ClampsPointerOutsideVirtualDesktop()
    {
        var desktop = new PixelRect(-1280, 0, 3200, 1080);

        var selection = ScreenSelectionGeometry.NormalizeAndClamp(
            -4000,
            -500,
            5000,
            2000,
            desktop);

        Assert.Equal(desktop, selection);
    }
}
