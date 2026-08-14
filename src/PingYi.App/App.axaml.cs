using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.Markup.Xaml;
using SkiaSharp;
using PingYi.Core;
using PingYi.Infrastructure;
using System.Diagnostics;

namespace PingYi.App;

public partial class App : Application
{
    internal static SingleInstanceCoordinator? SingleInstance { get; set; }

    private AppServices? _services;
    private Window? _mainWindow;
    private Window? _settingsWindow;
    private IMainWindowShell? _mainShell;
    private CaptureCoordinator? _captureCoordinator;
    private TrayIcon? _trayIcon;
    private bool _isExiting;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            InitializeDesktop(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void InitializeDesktop(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            _services = await AppServices.CreateAsync();
            UiText.Configure(_services.Settings.UiLanguage);
            _captureCoordinator = new CaptureCoordinator(_services);
            var openSettings = desktop.Args?.Contains("--settings", StringComparer.OrdinalIgnoreCase) == true;
            _mainWindow = openSettings
                ? new MainWindow(_services, _captureCoordinator, settingsMode: true)
                : _services.Settings.InterfaceStyle == "classic"
                    ? new MainWindow(_services, _captureCoordinator)
                    : new MainWindowV2(_services, _captureCoordinator, OpenSettingsWindowAsync);
            if (openSettings)
            {
                _settingsWindow = _mainWindow;
            }
            _mainShell = (IMainWindowShell)_mainWindow;
            _mainWindow.Closing += (_, eventArgs) =>
            {
                if (_isExiting)
                {
                    return;
                }

                eventArgs.Cancel = true;
                _mainWindow.Hide();
            };
            desktop.MainWindow = _mainWindow;
            _trayIcon = CreateTrayIcon();
            SingleInstance?.SetHandler(async command =>
                await Dispatcher.UIThread.InvokeAsync(() => HandleExternalCommandAsync(command)));

            _services.HotkeyService.Pressed += (_, _) =>
                Dispatcher.UIThread.Post(() => _ = _captureCoordinator.StartCaptureAsync(_mainShell));
            _ = StartHotkeyAsync();

            if (_services.Settings.StartMinimized && !openSettings)
            {
                _mainWindow.Hide();
            }
            else
            {
                _mainWindow.Show();
                _mainWindow.Activate();
            }

            if (_services.Settings.CheckForUpdates)
            {
                _ = CheckForUpdatesAsync(userInitiated: false);
            }
        }
        catch (Exception exception)
        {
            _mainWindow = new MainWindow();
            _mainShell = (IMainWindowShell)_mainWindow;
            _mainShell.SetGlobalStatus(
                UiText.IsEnglish
                    ? $"Initialization failed: {UiText.Error(exception)}"
                    : $"初始化失败：{UiText.Error(exception)}",
                isError: true);
            desktop.MainWindow = _mainWindow;
            _mainWindow.Show();
            _mainWindow.Activate();
        }
    }

    private async Task StartHotkeyAsync()
    {
        try
        {
            await _services!.HotkeyService.StartAsync(_services.Settings.Hotkey);
            _mainShell?.SetGlobalStatus("快捷键已启用", isError: false);
        }
        catch (Exception exception)
        {
            _mainShell?.SetGlobalStatus(UiText.Error(exception), isError: true);
        }
    }

    private TrayIcon CreateTrayIcon()
    {
        var showItem = new NativeMenuItem(UiText.IsEnglish ? "Open PingYi" : $"打开 {AppEdition.ProductName}");
        showItem.Click += (_, _) => ShowMainWindow();
        var captureItem = new NativeMenuItem(UiText.T("截图翻译"));
        captureItem.Click += (_, _) => _ = _captureCoordinator!.StartCaptureAsync(_mainShell);
        var updateItem = new NativeMenuItem(UiText.IsEnglish ? "Check for updates" : "检查更新");
        updateItem.Click += (_, _) => _ = CheckForUpdatesAsync(userInitiated: true);
        var exitItem = new NativeMenuItem(UiText.T("退出"));
        exitItem.Click += async (_, _) => await ExitAsync();

        var menu = new NativeMenu();
        menu.Add(captureItem);
        menu.Add(showItem);
        menu.Add(updateItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(exitItem);
        var tray = new TrayIcon
        {
            Icon = CreateWindowIcon(),
            ToolTipText = $"{(UiText.IsEnglish ? "PingYi" : AppEdition.ProductName)} · {_services?.Settings.Hotkey ?? AppSettings.DefaultHotkey}",
            Menu = menu,
            IsVisible = true
        };
        tray.Clicked += (_, _) => ShowMainWindow();
        return tray;
    }

    private static WindowIcon CreateWindowIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://PingYi.App/Assets/pingyi-v2-icon.png"));
            return new WindowIcon(stream);
        }
        catch
        {
            return CreateLegacyWindowIcon();
        }
    }

    private static WindowIcon CreateLegacyWindowIcon()
    {
        using var bitmap = new SKBitmap(32, 32);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(37, 99, 235));
        using var paint = new SKPaint { Color = SKColors.White, StrokeWidth = 2.5f, IsAntialias = true, Style = SKPaintStyle.Stroke };
        canvas.DrawLine(7, 12, 7, 7, paint);
        canvas.DrawLine(7, 7, 12, 7, paint);
        canvas.DrawLine(20, 7, 25, 7, paint);
        canvas.DrawLine(25, 7, 25, 12, paint);
        canvas.DrawLine(7, 20, 7, 25, paint);
        canvas.DrawLine(7, 25, 12, 25, paint);
        canvas.DrawLine(20, 25, 25, 25, paint);
        canvas.DrawLine(25, 25, 25, 20, paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return new WindowIcon(new MemoryStream(data.ToArray()));
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private async Task HandleExternalCommandAsync(string command)
    {
        try
        {
            switch (command)
            {
                case "capture" when _captureCoordinator is not null && _mainShell is not null:
                    await _captureCoordinator.StartCaptureAsync(_mainShell);
                    break;
                case "settings":
                    await OpenSettingsWindowAsync();
                    break;
                default:
                    ShowMainWindow();
                    break;
            }
        }
        catch (Exception exception)
        {
            ShowMainWindow();
            _mainShell?.SetGlobalStatus(UiText.Error(exception), isError: true);
        }
    }

    private async Task OpenSettingsWindowAsync()
    {
        if (_services is null || _captureCoordinator is null || _mainWindow is null)
        {
            ShowMainWindow();
            return;
        }

        ShowMainWindow();
        if (_settingsWindow is not null)
        {
            _settingsWindow.Show();
            _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }

        var settingsWindow = new MainWindow(_services, _captureCoordinator, settingsMode: true);
        _settingsWindow = settingsWindow;
        settingsWindow.Closed += (_, _) => _settingsWindow = null;
        await settingsWindow.ShowDialog(_mainWindow);
    }

    private async Task CheckForUpdatesAsync(bool userInitiated)
    {
        if (_services is null)
        {
            return;
        }

        try
        {
            var update = await _services.UpdateService.CheckAsync();
            if (update.IsUpdateAvailable)
            {
                _mainShell?.SetGlobalStatus(
                    UiText.IsEnglish
                        ? $"PingYi {update.LatestVersion} is available. Open Settings or use the tray menu to download it."
                        : $"屏译 {update.LatestVersion} 已发布，可在设置或托盘菜单中打开下载页。",
                    isError: false);
                if (userInitiated)
                {
                    Process.Start(new ProcessStartInfo(update.ReleasePage.AbsoluteUri)
                    {
                        UseShellExecute = true
                    });
                }
            }
            else if (userInitiated)
            {
                ShowMainWindow();
                _mainShell?.SetGlobalStatus(
                    UiText.IsEnglish
                        ? $"You are up to date ({update.CurrentVersion})."
                        : $"当前已是最新版本（{update.CurrentVersion}）。",
                    isError: false);
            }
        }
        catch (Exception) when (!userInitiated)
        {
            // Opt-in background checks stay quiet when the device is offline.
        }
        catch (Exception exception)
        {
            ShowMainWindow();
            _mainShell?.SetGlobalStatus(UiText.Error(exception), isError: true);
        }
    }

    private async Task ExitAsync()
    {
        _isExiting = true;
        _trayIcon?.Dispose();
        if (_captureCoordinator is not null)
        {
            await _captureCoordinator.DisposeAsync();
        }
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
