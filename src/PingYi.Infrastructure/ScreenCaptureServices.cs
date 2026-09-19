using System.ComponentModel;
using System.Runtime.InteropServices;
using PingYi.Core;
using SkiaSharp;

namespace PingYi.Infrastructure;

public static class ScreenCaptureServiceFactory
{
    public static IScreenCaptureService Create() =>
        OperatingSystem.IsWindows()
            ? new WindowsScreenCaptureService()
            : LinuxDesktop.Session == LinuxDesktopSession.Wayland
                ? new PortalScreenCaptureService()
                : new X11ScreenCaptureService();
}

internal sealed class WindowsScreenCaptureService : IScreenCaptureService
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const int SrcCopy = 0x00CC0020;
    private const int CaptureBlt = 0x40000000;
    private const uint DibRgbColors = 0;

    public Task<ImageFrame> CaptureDesktopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var x = GetSystemMetrics(SmXVirtualScreen);
        var y = GetSystemMetrics(SmYVirtualScreen);
        var width = GetSystemMetrics(SmCxVirtualScreen);
        var height = GetSystemMetrics(SmCyVirtualScreen);
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("无法获取虚拟桌面尺寸。");
        }

        var screenDc = GetDC(IntPtr.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmap = CreateCompatibleBitmap(screenDc, width, height);
        var oldObject = SelectObject(memoryDc, bitmap);
        try
        {
            if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, x, y, SrcCopy | CaptureBlt))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "屏幕捕获失败。");
            }

            var header = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0,
                    SizeImage = (uint)(width * height * 4)
                }
            };
            var pixels = new byte[width * height * 4];
            var copied = GetDIBits(memoryDc, bitmap, 0, (uint)height, pixels, ref header, DibRgbColors);
            if (copied == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取屏幕像素。");
            }

            return Task.FromResult(new ImageFrame(
                EncodeBgraToPng(pixels, width, height),
                width,
                height,
                new PixelRect(x, y, width, height)));
        }
        finally
        {
            SelectObject(memoryDc, oldObject);
            DeleteObject(bitmap);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    internal static byte[] EncodeBgraToPng(byte[] pixels, int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(
        IntPtr destination,
        int x,
        int y,
        int width,
        int height,
        IntPtr source,
        int sourceX,
        int sourceY,
        int operation);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetDIBits(
        IntPtr dc,
        IntPtr bitmap,
        uint start,
        uint lines,
        [Out] byte[] bits,
        ref BitmapInfo info,
        uint usage);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr dc);
}

internal sealed class X11ScreenCaptureService : IScreenCaptureService
{
    private const int ZPixmap = 2;

    public Task<ImageFrame> CaptureDesktopAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => Capture(cancellationToken), cancellationToken);

    private static ImageFrame Capture(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureX11();
        var display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero)
        {
            throw new InvalidOperationException("无法连接 X11 显示服务器。");
        }

        try
        {
            var screen = XDefaultScreen(display);
            var root = XRootWindow(display, screen);
            var width = XDisplayWidth(display, screen);
            var height = XDisplayHeight(display, screen);
            if (width <= 0 || height <= 0 || (long)width * height > 100_000_000)
                throw new ProviderException("capture_dimensions", "屏幕尺寸无效或超过捕获上限。");
            cancellationToken.ThrowIfCancellationRequested();
            var imagePointer = XGetImage(display, root, 0, 0, (uint)width, (uint)height, ulong.MaxValue, ZPixmap);
            if (imagePointer == IntPtr.Zero)
            {
                throw new InvalidOperationException("XGetImage 屏幕捕获失败。");
            }

            try
            {
                var image = Marshal.PtrToStructure<XImage>(imagePointer);
                var raw = new byte[checked(image.BytesPerLine * image.Height)];
                Marshal.Copy(image.Data, raw, 0, raw.Length);
                var bgra = ConvertXImageToBgra(raw, image);
                return new ImageFrame(
                    WindowsScreenCaptureService.EncodeBgraToPng(bgra, width, height),
                    width,
                    height,
                    new PixelRect(0, 0, width, height));
            }
            finally
            {
                XDestroyImage(imagePointer);
            }
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    private static byte[] ConvertXImageToBgra(byte[] raw, XImage image)
    {
        var bytesPerPixel = Math.Max(1, (image.BitsPerPixel + 7) / 8);
        if (bytesPerPixel is not (3 or 4))
        {
            throw new NotSupportedException($"暂不支持 {image.BitsPerPixel} 位 X11 像素格式。");
        }

        var output = new byte[image.Width * image.Height * 4];
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var sourceOffset = y * image.BytesPerLine + x * bytesPerPixel;
                ulong pixel = 0;
                if (image.ByteOrder == 0)
                {
                    for (var index = 0; index < bytesPerPixel; index++)
                    {
                        pixel |= (ulong)raw[sourceOffset + index] << (index * 8);
                    }
                }
                else
                {
                    for (var index = 0; index < bytesPerPixel; index++)
                    {
                        pixel = (pixel << 8) | raw[sourceOffset + index];
                    }
                }

                var destinationOffset = (y * image.Width + x) * 4;
                output[destinationOffset] = ExtractChannel(pixel, image.BlueMask);
                output[destinationOffset + 1] = ExtractChannel(pixel, image.GreenMask);
                output[destinationOffset + 2] = ExtractChannel(pixel, image.RedMask);
                output[destinationOffset + 3] = 255;
            }
        }

        return output;
    }

    private static byte ExtractChannel(ulong pixel, ulong mask)
    {
        if (mask == 0)
        {
            return 0;
        }

        var shift = 0;
        var shiftedMask = mask;
        while ((shiftedMask & 1) == 0)
        {
            shift++;
            shiftedMask >>= 1;
        }

        var value = (pixel & mask) >> shift;
        return (byte)(value * 255 / shiftedMask);
    }

    private static void EnsureX11()
    {
        if (LinuxDesktop.Session == LinuxDesktopSession.Wayland)
        {
            throw new ProviderException("capture_wayland_portal_required", "Wayland 截图必须使用系统截图门户。");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XImage
    {
        public int Width;
        public int Height;
        public int XOffset;
        public int Format;
        public IntPtr Data;
        public int ByteOrder;
        public int BitmapUnit;
        public int BitmapBitOrder;
        public int BitmapPad;
        public int Depth;
        public int BytesPerLine;
        public int BitsPerPixel;
        public ulong RedMask;
        public ulong GreenMask;
        public ulong BlueMask;
        public IntPtr ObData;
        public IntPtr Functions;
    }

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XDefaultScreen(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XRootWindow(IntPtr display, int screenNumber);

    [DllImport("libX11.so.6")]
    private static extern int XDisplayWidth(IntPtr display, int screenNumber);

    [DllImport("libX11.so.6")]
    private static extern int XDisplayHeight(IntPtr display, int screenNumber);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XGetImage(
        IntPtr display,
        IntPtr drawable,
        int x,
        int y,
        uint width,
        uint height,
        ulong planeMask,
        int format);

    [DllImport("libX11.so.6")]
    private static extern int XDestroyImage(IntPtr image);
}
