using PingYi.Core;
using SkiaSharp;

namespace PingYi.Infrastructure;

/// <summary>Let the Wayland compositor own interactive selection and permission.</summary>
public sealed class PortalScreenCaptureService : IInteractiveScreenCaptureService
{
    public static readonly TimeSpan InteractionTimeout = TimeSpan.FromMinutes(3);

    public Task<ImageFrame> CaptureDesktopAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<ImageFrame>(new ProviderException("capture_wayland_portal_required",
            "Wayland 截图由系统选择界面完成，不能直接读取桌面底图。"));

    public Task<ImageFrame?> CaptureSelectionAsync(CancellationToken cancellationToken = default) =>
        Task.Factory.StartNew(() => Capture(cancellationToken), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static ImageFrame? Capture(CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(InteractionTimeout);
        try
        {
            using var portal = new GioPortal(deadline.Token);
            portal.RegisterApplication(deadline.Token);
            var requestToken = "pingyi_" + Guid.NewGuid().ToString("N");
            using var response = portal.Request("org.freedesktop.portal.Screenshot", "Screenshot", requestToken,
                $"('', {{'handle_token': <'{requestToken}'>, 'interactive': <true>, 'modal': <false>}})", deadline.Token);
            if (response.Code == 1) return null; // The user's Esc/Cancel is not a failure.
            if (response.Code != 0) throw new ProviderException("capture_portal_denied", "系统未允许截图或截图已被拒绝，请检查系统截图权限后重试。");
            using var uri = response.Values.Lookup("uri");
            if (uri is null) throw new ProviderException("capture_portal_image", "系统截图门户没有返回可读取的图片。");
            return ReadPortalImage(uri.String(), deadline.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new ProviderException("capture_portal_timeout", "等待系统截图选择已超时，请重新点击截图并在系统界面中完成选择。"); }
        catch (Exception e) when (e is GioPortal.PortalCallException or DllNotFoundException or EntryPointNotFoundException)
        { throw new ProviderException("capture_portal_unavailable", "无法调用系统截图门户。Ubuntu 请检查 xdg-desktop-portal 与 xdg-desktop-portal-gnome 是否安装并运行；修复后重新登录。也可使用 Ubuntu on Xorg 会话。", e); }
    }

    public static ImageFrame ReadPortalImage(string uri, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var source) || !source.IsFile ||
            !string.IsNullOrEmpty(source.Host) || !string.IsNullOrEmpty(source.Query) || !string.IsNullOrEmpty(source.Fragment))
            throw new ProviderException("capture_portal_image", "系统截图门户返回了无效的本地图片地址。");
        try
        {
            var file = new FileInfo(source.LocalPath);
            // Do not follow a link and do not open pipes/devices reported as zero-length.
            if (file.LinkTarget is not null || file.Length <= 0 || file.Length > 128 * 1024 * 1024)
                throw new ProviderException("capture_portal_image", "系统截图文件无效或超过大小上限。");
            using var input = file.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
            var length = input.Length;
            if (length <= 0 || length > 128 * 1024 * 1024)
                throw new ProviderException("capture_portal_image", "系统截图文件超过大小上限。");
            var bytes = new byte[checked((int)length)];
            input.ReadExactly(bytes);
            token.ThrowIfCancellationRequested();
            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 ||
                (long)codec.Info.Width * codec.Info.Height > 100_000_000 || codec.EncodedFormat != SKEncodedImageFormat.Png)
                throw new ProviderException("capture_portal_image", "系统截图文件不是受支持的 PNG 图片或尺寸超过上限。");
            // The portal provides no desktop coordinates. Do not invent a multi-monitor
            // origin or draw another overlay over an already selected image.
            return new ImageFrame(bytes, codec.Info.Width, codec.Info.Height,
                new PixelRect(0, 0, codec.Info.Width, codec.Info.Height));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { throw new ProviderException("capture_portal_image", "无法读取系统截图门户返回的图片，请重试。", e); }
        // Do not delete files owned by the desktop/Document portal or user. PingYi
        // creates no new file or history; the compositor controls temporary files.
    }
}
