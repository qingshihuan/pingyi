namespace PingYi.Core;

public static class ScreenSelectionGeometry
{
    public static PixelRect NormalizeAndClamp(
        int startX,
        int startY,
        int endX,
        int endY,
        PixelRect desktopBounds)
    {
        var left = Math.Clamp(Math.Min(startX, endX), desktopBounds.X, Right(desktopBounds));
        var top = Math.Clamp(Math.Min(startY, endY), desktopBounds.Y, Bottom(desktopBounds));
        var right = Math.Clamp(Math.Max(startX, endX), desktopBounds.X, Right(desktopBounds));
        var bottom = Math.Clamp(Math.Max(startY, endY), desktopBounds.Y, Bottom(desktopBounds));
        return new PixelRect(left, top, right - left, bottom - top);
    }

    public static PixelRect Intersect(PixelRect first, PixelRect second)
    {
        var left = Math.Max(first.X, second.X);
        var top = Math.Max(first.Y, second.Y);
        var right = Math.Min(Right(first), Right(second));
        var bottom = Math.Min(Bottom(first), Bottom(second));
        return right <= left || bottom <= top
            ? new PixelRect(0, 0, 0, 0)
            : new PixelRect(left, top, right - left, bottom - top);
    }

    public static PixelRect ToCaptureRelative(PixelRect globalSelection, PixelRect desktopBounds) =>
        new(
            globalSelection.X - desktopBounds.X,
            globalSelection.Y - desktopBounds.Y,
            globalSelection.Width,
            globalSelection.Height);

    private static int Right(PixelRect rectangle) => checked(rectangle.X + rectangle.Width);

    private static int Bottom(PixelRect rectangle) => checked(rectangle.Y + rectangle.Height);
}
