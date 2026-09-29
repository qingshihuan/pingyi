from pathlib import Path


def edit(path, old, new, count=1):
    target = Path(path)
    text = target.read_text(encoding='utf-8-sig')
    if text.count(old) != count:
        raise RuntimeError(f'{path}: {text.count(old)} anchors, expected {count}')
    target.write_text(text.replace(old, new), encoding='utf-8', newline='\n')


path = 'src/PingYi.App/MainWindow.axaml'
t = Path(path).read_text(encoding='utf-8')
start = t.index('        <Border Classes="workspace-card" Padding="18,12" CornerRadius="16">')
end = t.index('        <Border x:Name="RecoveryBorder"', start)
t = t[:start] + '''        <Border x:Name="ModeStatusBorder" Classes="workspace-card" Padding="18,12" CornerRadius="16">
          <Grid RowDefinitions="Auto,Auto" RowSpacing="12">
            <Grid ColumnDefinitions="*,Auto,Auto" ColumnSpacing="14">
              <TextBlock x:Name="ModeStatusTitleText" FontSize="19" FontWeight="SemiBold" VerticalAlignment="Center"/>
              <Button Grid.Column="1" x:Name="RefreshWorkspaceButton" Classes="toolbar-action" Content="{DynamicResource Workspace.Refresh}"
                      Click="RefreshWorkspaceButton_OnClick" ToolTip.Tip="F5" VerticalAlignment="Center"/>
              <Button Grid.Column="2" x:Name="ModeDetailsButton" Classes="toolbar-action" Foreground="{DynamicResource BrandBrush}"
                      Click="OpenModeDetails_OnClick" VerticalAlignment="Center"/>
            </Grid>
            <local:ModeStatusCards x:Name="ModeCards" Grid.Row="1"/>
          </Grid>
        </Border>
''' + t[end:]
Path(path).write_text(t, encoding='utf-8', newline='\n')
path = 'src/PingYi.App/MainWindow.axaml.cs'
edit(path, '        InitializeExitControl();', '        InitializeExitControl();\n        InitializeModeStatusUi();')
t = Path(path).read_text(encoding='utf-8')
start = t.index('    private async Task RefreshDashboardAsync()')
end = t.index('    private async void CaptureButton_OnClick', start)
t = t[:start] + '''    private async Task RefreshDashboardAsync(bool force = false)
    {
        if (_services is null || _isRefreshing || _statusClosed || _services.IsInitialSetupActive) return;
        _isRefreshing = true;
        TopStatusText.Text = ModeStatusText.Pick("正在检查", "Checking");
        TopStatusDot.Background = FindBrush("TertiaryTextBrush");
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var snapshot = await _services.ReadRuntimeStatusAsync(_statusLifetime.Token, force);
                if (_statusClosed) return;
                if (snapshot.Settings != _services.Settings) continue;
                RenderRuntimeStatus(snapshot);
                RenderPipelineStatus(snapshot);
                _passiveRefresh.RecordRefresh(snapshot.Settings);
                return;
            }
        }
        catch (OperationCanceledException) when (_statusClosed || _services.IsShuttingDown) { }
        catch (Exception) { if (!_statusClosed) SetGlobalStatus(ModeStatusText.Pick("状态检查未完成，请重试。", "Status check did not complete; retry."), true); }
        finally { _isRefreshing = false; }
    }
''' + t[end:]
t = t.replace('UpdateProductTitle(); LoadSettings(); }', 'UpdateProductTitle(); LoadSettings(); RefreshModeStatusLanguage(); }')
Path(path).write_text(t, encoding='utf-8', newline='\n')
edit('src/PingYi.App/MainWindow.Workspace.cs', 'await RefreshDashboardAsync();', 'await RefreshDashboardAsync(force: true);')

# A sixth category preserves all existing tab indices and input controls.
edit('src/PingYi.App/SettingsWindow.axaml', '    </TabControl>', '''      <TabItem Classes="workspace-tab" x:Name="RuntimeStatusSettingsTab">
        <TabItem.Header>
          <Grid ColumnDefinitions="28,*" ColumnSpacing="10">
            <Border Width="28" Height="28" CornerRadius="7" Background="{DynamicResource NavBlueBrush}">
              <Path Classes="symbol" Width="16" Height="16" Stroke="White" Data="M2 12H7L10 4L14 20L17 12H22"/>
            </Border>
            <TextBlock Grid.Column="1" x:Name="RuntimeStatusNavText" TextWrapping="Wrap" VerticalAlignment="Center"/>
          </Grid>
        </TabItem.Header>
        <local:RuntimeStatusView x:Name="RuntimeStatusPage"/>
      </TabItem>
    </TabControl>''')
path = 'src/PingYi.App/SettingsWindow.axaml.cs'
edit(path, '        InitializeLinuxHelp();', '        InitializeLinuxHelp();\n        InitializeRuntimeStatusPage();')
edit(path, '''        Opened += async (_, _) =>
        {
            await RefreshCredentialStatusAsync();''', '''        Opened += async (_, _) =>
        {
            if (RuntimeStatusSettingsTab.IsSelected)
            {
                _statusOpenedWithoutProviders = true;
                await RefreshRuntimeStatusAsync();
                return;
            }
            await RefreshCredentialStatusAsync();''')
edit('src/PingYi.App/SettingsWindow.Language.cs', '        RefreshWindowTitle();', '        RefreshWindowTitle();\n        RefreshRuntimeStatusLanguage();')
path = 'src/PingYi.App/App.axaml.cs'
edit(path, '''    private Task OpenSettingsWindowAsync()
    {''', '''    private Task OpenSettingsWindowAsync() => OpenSettingsWindowAsync(false);

    private Task OpenSettingsWindowAsync(bool runtimeStatus)
    {''')
edit(path, '''        if (_settingsWindow is not null)
        {
            _settingsWindow.Show();''', '''        if (_settingsWindow is not null)
        {
            if (runtimeStatus && _settingsWindow is SettingsWindow current) current.SelectRuntimeStatus();
            _settingsWindow.Show();''')
edit(path, '''        var settingsWindow = new SettingsWindow(_services);
        _settingsWindow = settingsWindow;''', '''        var settingsWindow = new SettingsWindow(_services);
        if (runtimeStatus) settingsWindow.SelectRuntimeStatus();
        _settingsWindow = settingsWindow;''')
path = 'src/PingYi.App/Styles/ApplePalette.axaml'
edit(path, '<SolidColorBrush x:Key="DangerTextBrush">#BA3038</SolidColorBrush>', '<SolidColorBrush x:Key="DangerTextBrush">#BA3038</SolidColorBrush>\n      <SolidColorBrush x:Key="StatusDangerBackgroundBrush">#FDECEF</SolidColorBrush>')
edit(path, '<SolidColorBrush x:Key="DangerTextBrush">#FFA1A8</SolidColorBrush>', '<SolidColorBrush x:Key="DangerTextBrush">#FFA1A8</SolidColorBrush>\n      <SolidColorBrush x:Key="StatusDangerBackgroundBrush">#4A282F</SolidColorBrush>')
edit('tests/PingYi.App.Tests/WorkspaceTests.cs', 'Assert.Equal(5, tabs.Items.Count);', 'Assert.Equal(6, tabs.Items.Count);')
edit('tests/PingYi.App.Tests/ApplePolishTests.cs', '''            C<TextBlock>(home, "ModelStatusTitleText").Text = UiText.IsEnglish ? "Load on demand" : "按需加载";
            C<TextBlock>(home, "ModelStatusDetailText").Text = UiText.IsEnglish ? "Sample local OCR and translation setup." : "本地识别与离线翻译方案示例";''', '''            home.RenderRuntimeStatus(RuntimeStatusUiTests.SyntheticSnapshot());''')
edit('tests/PingYi.App.Tests/VideoPreviewTests.cs', '''            Text(home, "ModelStatusTitleText", "本地探测与轻量兜底");
            Text(home, "ModelStatusDetailText", "PaddleOCR / Argos · 演示状态，未运行模型");''', '''            home.RenderRuntimeStatus(RuntimeStatusUiTests.SyntheticSnapshot());''')

for path, tail in [
    ('README.md', '''
## 模式运行状态

主界面“运行状态”独立展示轻量、基础、云端三种模式，不再只显示轻量模型状态。绿色表示本地可用或已配置按需加载，红色表示未配置或需处理，灰色表示未检测/待验证；状态旁保留文字，不仅依赖颜色。点击“查看详情”进入设置的新“运行状态”页，查看模式、运行后端、执行显卡及模型信息并跳转配置。

刷新不会下载、启动视觉模型、执行推理或改变处理方案。本机服务只做回环地址状态探测；托管模型读取文件存在性/大小，完整校验仍在启动时进行。云端仅检查配置是否完整，不把密钥存在当成网络、配额或凭据验证通过，也不伪报下载服务正常。设置页的联网验证仍由用户主动执行。未加载的模型不会显示为正在使用显卡。
'''),
    ('README.en.md', '''
## Mode runtime status

Home now shows separate Lightweight, Basic and Cloud cards. Green means local readiness or configured on-demand loading; red means not configured/needs attention; gray means not checked/unverified. Text accompanies every indicator. View details opens the new Runtime status Settings category for mode, runtime, GPU and model information and configuration links.

Refresh never downloads, starts a vision model, runs inference or changes the scheme. Only loopback services may be probed. Managed-model file presence/sizes are inspected; full integrity verification remains at startup. Saved cloud credentials are not proof of connectivity, quota or successful authentication. Cloud validation stays an explicit user action. Download connectivity and an active GPU are never fabricated.
'''),
    ('THIRD_PARTY_NOTICES.md', '''
## Mode status interface

The three-mode overview and runtime-details page reuse Avalonia controls, theme resources and project-authored vector paths. No production dependencies, fonts, telemetry, remote status services or artwork are added. Status snapshots contain no credentials or captured content. New UI snapshots use explicitly synthetic state; they do not certify real models, cloud services or GPU hardware.
''')]:
    target = Path(path)
    target.write_text(target.read_text(encoding='utf-8') + tail, encoding='utf-8', newline='\n')

# Do not leave transfer machinery in product source or the merged branch.
Path('.github/workflows/integrate-mode-status.yml').unlink()
Path(__file__).unlink()
