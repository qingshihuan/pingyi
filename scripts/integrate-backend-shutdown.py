"""One-use, reviewable integration edits. Removed before the final feature commit."""
from pathlib import Path
ROOT = Path.cwd()

def edit(name, before, after, count=1):
    path = ROOT / name
    text = path.read_text(encoding='utf-8-sig')
    if text.count(before) != count:
        raise RuntimeError(f'{name}: integration anchor changed')
    path.write_text(text.replace(before, after), encoding='utf-8')

p = 'src/PingYi.App/App.axaml.cs'
edit(p, '            InitializeDesktop(desktop);', '            RegisterShutdown(desktop);\n            InitializeDesktop(desktop);')
edit(p, '            _services = await AppServices.CreateAsync();', '            _services = await AppServices.CreateAsync();\n            if (_isExiting) { await _services.DisposeAsync(); return; }')
edit(p, '            _mainShell = (IMainWindowShell)_mainWindow;', '            _mainShell = (IMainWindowShell)_mainWindow;\n            ((MainWindow)_mainWindow).ExitRequested = ExitAsync;', 2)
edit(p, '            _services.HotkeyService.Pressed += (_, _) =>\n                Dispatcher.UIThread.Post(() => _ = _captureCoordinator.StartCaptureAsync(_mainShell));', '            _services.HotkeyService.Pressed += (_, _) =>\n                Dispatcher.UIThread.Post(() => { if (!_isExiting) _ = _captureCoordinator.StartCaptureAsync(_mainShell); });')
edit(p, '        catch (Exception exception)\n        {\n            _mainWindow = new MainWindow();', '        catch (Exception exception)\n        {\n            if (_isExiting) return;\n            _mainWindow = new MainWindow();')
edit(p, '            await _services!.HotkeyService.StartAsync(_services.Settings.Hotkey);', '            if (_isExiting || _services is null) return;\n            await _services.HotkeyService.StartAsync(_services.Settings.Hotkey);\n            if (_isExiting) { await _services.HotkeyService.StopAsync(); return; }')
edit(p, '            _services!.HotkeyRegistrationError = exception;', '            if (_isExiting) return;\n            _services!.HotkeyRegistrationError = exception;')
edit(p, 'captureItem.Click += (_, _) => _ = _captureCoordinator!.StartCaptureAsync(_mainShell);', 'captureItem.Click += (_, _) => { if (!_isExiting) _ = _captureCoordinator!.StartCaptureAsync(_mainShell); };')
edit(p, 'qrItem.Click += (_, _) => _ = _captureCoordinator!.StartCaptureAsync(_mainShell, CapturePurpose.DecodeQrCode);', 'qrItem.Click += (_, _) => { if (!_isExiting) _ = _captureCoordinator!.StartCaptureAsync(_mainShell, CapturePurpose.DecodeQrCode); };')
edit(p, 'var exitItem = new NativeMenuItem(UiText.T("退出"));', 'var exitItem = new NativeMenuItem(CaptureUiText.Pick("退出并释放资源", "Quit and release resources"));')
edit(p, 'if (_mainWindow is null || _captureCoordinator?.IsCapturingScreen == true)', 'if (_isExiting || _mainWindow is null || _captureCoordinator?.IsCapturingScreen == true)')
edit(p, '    private async Task HandleExternalCommandAsync(string command)\n    {', '    private async Task HandleExternalCommandAsync(string command)\n    {\n        if (_isExiting) return;')
edit(p, '        if (_captureCoordinator?.IsCapturingScreen == true) return Task.CompletedTask;', '        if (_isExiting || _captureCoordinator?.IsCapturingScreen == true) return Task.CompletedTask;')
text = (ROOT/p).read_text()
start = text.index('    private async Task ExitAsync()')
(ROOT/p).write_text(text[:start] + '}\n')

edit('src/PingYi.App/Program.cs', '            App.SingleInstance = null;', '            // The dispatcher may already have stopped; this path never needs UI continuations.\n            PingYi.Infrastructure.OwnedProcessScope.TerminateAllAtExit(TimeSpan.FromSeconds(4));\n            App.SingleInstance = null;')
edit('src/PingYi.App/MainWindow.axaml.cs', '        InitializeSmartCaptureUi();', '        InitializeSmartCaptureUi();\n        InitializeExitControl();')
p = 'src/PingYi.App/MainWindow.axaml'
edit(p, '<Grid ColumnDefinitions="Auto,*" ColumnSpacing="10">', '<Grid ColumnDefinitions="Auto,*,Auto" ColumnSpacing="10">')
edit(p, '        </StackPanel>\n      </Grid>\n    </Border>\n  </Grid>', '        </StackPanel>\n        <Button Grid.Column="2" x:Name="ExitApplicationButton" Classes="toolbar-action"\n                MinHeight="32" Padding="12,6" VerticalAlignment="Center" Click="ExitApplication_OnClick"/>\n      </Grid>\n    </Border>\n  </Grid>')

p = 'src/PingYi.App/AppServices.cs'
edit(p, '    public AppDataPaths Paths { get; }', '    public bool IsShuttingDown => Volatile.Read(ref _disposeState) != 0;\n    public AppDataPaths Paths { get; }')
text = (ROOT/p).read_text()
start = text.index('    public async ValueTask DisposeAsync()')
text = text[:start] + '''    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0) { await _disposeCompletion.Task; return; }
        Exception? failure = null;
        try
        {
            // Start owned runtime termination before waiting for browser/settings gates or UI teardown.
            // Native ONNX disposal retains its own inference lock; it is not freed during Run().
            await ShutdownWork.RunAllAsync(
                () => _lifetime.CancelAsync(),
                () => ManagedModels.DisposeAsync().AsTask(),
                async () =>
                {
                    var previous = InvalidateManagedRuntimeStartup();
                    CancelAndRelease(previous.Cancellation, previous.Task);
                    await previous.Task;
                },
                () => _browserBridge?.DisposeAsync().AsTask() ?? Task.CompletedTask,
                () => HotkeyService.DisposeAsync().AsTask(),
                () => Engine.DisposeAsync().AsTask(),
                () => PaddleProvider.DisposeAsync().AsTask(),
                async () => { await _settingsTransitionGate.WaitAsync(); _settingsTransitionGate.Release(); },
                async () => { await _initialModelGate.WaitAsync(); _initialModelGate.Release(); });
        }
        catch (Exception error) { failure = error; }
        finally
        {
            _httpClient.Dispose(); _imageAnalysisClient.Dispose();
            Volatile.Write(ref _disposeState, 2);
            if (failure is null) _disposeCompletion.TrySetResult();
            else _disposeCompletion.TrySetException(failure);
        }
        await _disposeCompletion.Task;
    }
}
'''
(ROOT/p).write_text(text)
p = 'src/PingYi.App/CaptureCoordinator.cs'
edit(p, 'if (services.IsInitialSetupActive) return;', 'if (services.IsInitialSetupActive || services.IsShuttingDown) return;')
edit(p, 'if (Volatile.Read(ref _disposeState) != 0 || services.IsInitialSetupActive) return null;', 'if (Volatile.Read(ref _disposeState) != 0 || services.IsInitialSetupActive || services.IsShuttingDown) return null;')
p = 'src/PingYi.App/AppServices.InitialSetup.cs'
edit(p, '        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);', '        ObjectDisposedException.ThrowIf(IsShuttingDown, this);\n        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);')
edit(p, '            if (attemptedStart && Settings == before)', '            if (attemptedStart && Settings == before && !IsShuttingDown)')

p = 'src/PingYi.Infrastructure/ManagedModelService.cs'
edit(p, '    private Process? _ownedProcess;', '    private Process? _ownedProcess;\n    private OwnedProcessScope? _ownedProcessScope;')
edit(p, '''        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("llama.cpp 进程未能启动。");
        }

        // Own the process before starting readers, so a reader setup failure is cleaned up.
        _ownedProcess = process;''', '''        // No backend is allowed to outlive our ownership just because startup/teardown failed.
        _ownedProcessScope = OwnedProcessScope.Start(startInfo);
        var process = _ownedProcess = _ownedProcessScope.Process;''')
text = (ROOT/p).read_text()
start = text.index('    private async Task StopOwnedProcessCoreAsync()')
end = text.index('    private string BuildServerErrorMessage', start)
text = text[:start] + '''    private async Task StopOwnedProcessCoreAsync()
    {
        if (_ownedProcessScope is null) return; // An externally connected service is never owned.
        await _ownedProcessScope.StopAsync();
        _ownedProcessScope = null;
        _ownedProcess = null;
        _runningModelId = _runningBackendId = null;
        try { await Task.WhenAll(_outputReaders).WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (TimeoutException) { }
        _outputReaders = [];
    }

''' + text[end:]
start = text.index('    public async ValueTask DisposeAsync()')
end = text.index('    private sealed record RuntimeCandidate', start)
text = text[:start] + '''    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
        {
            await _disposeCompletion.Task;
            return;
        }
        Exception? failure = null;
        try
        {
            await _lifetime.CancelAsync();
            await _operationGate.WaitAsync();
            try { await StopOwnedProcessCoreAsync(); }
            finally { _operationGate.Release(); }
        }
        catch (Exception error) { failure = error; }
        finally
        {
            _downloadClient.Dispose(); _probeClient.Dispose();
            Volatile.Write(ref _disposeState, 2);
            if (failure is null) _disposeCompletion.TrySetResult();
            else _disposeCompletion.TrySetException(failure);
        }
        await _disposeCompletion.Task;
    }

''' + text[end:]
(ROOT/p).write_text(text)

p = 'src/PingYi.Infrastructure/EngineProcessClient.cs'
edit(p, '    private Process? _process;', '    private Process? _process;\n    private OwnedProcessScope? _processScope;')
edit(p, '''        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动截屏释义本地引擎。");''', '''        _processScope = OwnedProcessScope.Start(startInfo);
        _process = _processScope.Process;''')
text = (ROOT/p).read_text()
start = text.index('    private void ResetProcess(bool terminate = false)')
end = text.index('    private async Task ReleaseIdleProcessAsync()', start)
text = text[:start] + '''    private void ResetProcess(bool terminate = false)
    {
        // The scope closes only this instance's child, including a Python launcher child on Windows.
        // Do not drop ownership before confirmed exit, even on request failure or idle reclamation.
        _processScope?.Dispose();
        _processScope = null;
        _process = null;
    }

''' + text[end:]
text = text.replace('exception is InvalidOperationException or IOException)', 'exception is InvalidOperationException or IOException or TimeoutException)')
start = text.index('    public async ValueTask DisposeAsync()')
before, after = text[:start], text[start:]
after = after.replace('        try\n        {\n            await _disposeCancellation', '        Exception? failure = null;\n        try\n        {\n            await _disposeCancellation', 1)
after = after.replace('''        finally
        {
            Volatile.Write(ref _disposeState, 2);
            _disposeCompletion.TrySetResult();
        }
    }
}''', '''        catch (Exception error) { failure = error; }
        finally
        {
            Volatile.Write(ref _disposeState, 2);
            if (failure is null) _disposeCompletion.TrySetResult();
            else _disposeCompletion.TrySetException(failure);
        }
        await _disposeCompletion.Task.ConfigureAwait(false);
    }
}''')
(ROOT/p).write_text(before + after)

with (ROOT/'scripts/test-linux-desktop.sh').open('a') as output:
    output.write('''

echo 'Testing explicit Quit and release resources (not close-to-tray)'
# Record only the synthetic application's existing direct children, never look up by executable name.
children=$(pgrep -P "$app_pid" || true)
xdotool windowactivate --sync "$main"
scrot "$PWD/artifacts/ui/native-linux-before-quit.png"
xdotool mousemove --window "$main" 900 683 click 1
for i in $(seq 1 240); do
  kill -0 "$app_pid" 2>/dev/null || break
  sleep 0.1
done
if kill -0 "$app_pid" 2>/dev/null; then
  diagnose 'application did not finish explicit exit'
  exit 1
fi
wait "$app_pid"
app_pid=''
for child in $children; do
  if test -e "/proc/$child/stat"; then
    state=$(awk '{print $3}' "/proc/$child/stat")
    if test "$state" != Z; then
      diagnose 'an owned synthetic backend survived exit'
      exit 1
    fi
  fi
done
echo 'Explicit exit completed and the synthetic application children are no longer running.'
''')

appendices = {
'README.md': '''
## 退出与显存释放

点击主窗口底部或托盘的 **“退出并释放资源”**，停止截屏释义及由它启动的模型后端，等待进程结束后退出。
关闭主窗口到托盘仍会保留后台运行，不等于退出。外部自行运行的 Ollama／LM Studio 等服务不会被误关，
模型文件与设置不会删除。显存由操作系统与驱动在后端退出后回收，不保证显卡总占用归零。
[退出行为、后端所有权与验证范围](docs/RUNTIME_SHUTDOWN.md)。这部分为源码改动，安装包以对应 Release 为准。
''',
'README.en.md': '''
## Quit and release resources

Use **Quit and release resources** in the main-window footer or tray to stop the app and its owned
model backends and wait for their exit. Closing the window still hides it to the tray. External
Ollama / LM Studio services are not killed; models and settings are retained. Backend allocations
are reclaimed by the OS/driver, not by resetting the entire GPU. See [shutdown and ownership](docs/RUNTIME_SHUTDOWN.md).
These are source changes; installed behavior depends on the corresponding Release.
''',
'THIRD_PARTY_NOTICES.md': '''
## Owned backend shutdown

Backend lifecycle management reuses .NET process APIs and Windows Job Objects. It adds no runtime
package, device driver, executable, font or telemetry dependency. It stops only processes started
by this application, never scans other applications by process name or GPU. Lifecycle tests use
synthetic child processes and the existing CI Python interpreter. No GPU reset or external-model
service shutdown API is called; user data, model files and credentials remain untouched.
'''
}
for name, appendix in appendices.items():
    with (ROOT/name).open('a', encoding='utf-8') as output:
        output.write('\n' + appendix.lstrip('\n'))
# Keep one-use integration machinery out of the final product tree.
(ROOT/'.github/workflows/shutdown-integration.yml').unlink()
Path(__file__).unlink()
