namespace PingYi.Core;

/// <summary>
/// One fresh desktop snapshot: either top-down opaque BGRA32 pixels or an encoded platform image.
/// The producer transfers ownership of the managed pixel buffer and must never mutate it afterwards.
/// Slices share that immutable buffer, including its stride; no native pointer outlives a pin.
/// Model/provider inputs remain PNG ImageFrame objects, encoded only after selection.
/// </summary>
public sealed class DesktopFrame
{
    public ImageFrame? EncodedImage { get; }
    public ReadOnlyMemory<byte> BgraPixels { get; }
    public int Stride { get; }
    public PixelRect DesktopBounds { get; }
    public int Width => DesktopBounds.Width;
    public int Height => DesktopBounds.Height;
    public bool IsRaw => EncodedImage is null;
    public int PngByteCount => EncodedImage?.PngBytes.Length ?? 0;

    public DesktopFrame(ImageFrame image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Width <= 0 || image.Height <= 0 || image.DesktopBounds.Width != image.Width || image.DesktopBounds.Height != image.Height)
            throw new ArgumentException("Invalid desktop frame dimensions.", nameof(image));
        EncodedImage = image;
        DesktopBounds = image.DesktopBounds;
    }

    public DesktopFrame(ReadOnlyMemory<byte> bgraPixels, int stride, PixelRect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || stride < checked(bounds.Width * 4) || stride % 4 != 0)
            throw new ArgumentOutOfRangeException(nameof(stride));
        var required = checked((bounds.Height - 1) * stride + bounds.Width * 4);
        if (bgraPixels.Length < required) throw new ArgumentException("Incomplete pixel buffer.", nameof(bgraPixels));
        BgraPixels = bgraPixels[..required];
        Stride = stride;
        DesktopBounds = bounds;
    }

    public DesktopFrame Slice(PixelRect relativeBounds)
    {
        if (!IsRaw) throw new InvalidOperationException("Only raw frames support pixel views.");
        if (relativeBounds.X < 0 || relativeBounds.Y < 0 || relativeBounds.IsEmpty ||
            (long)relativeBounds.X + relativeBounds.Width > Width || (long)relativeBounds.Y + relativeBounds.Height > Height)
            throw new ArgumentOutOfRangeException(nameof(relativeBounds));
        var offset = checked(relativeBounds.Y * Stride + relativeBounds.X * 4);
        return new DesktopFrame(BgraPixels[offset..], Stride,
            new PixelRect(checked(DesktopBounds.X + relativeBounds.X), checked(DesktopBounds.Y + relativeBounds.Y), relativeBounds.Width, relativeBounds.Height));
    }
}
