using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PingYi.Core;

namespace PingYi.App;

internal sealed partial class ConfigurationCenterView
{
    private readonly TabControl _tabs = new() { Name = "ConfigurationTabs" };
    private readonly StackPanel _overviewRows = new() { Spacing = 10 };
    private readonly TextBlock _overviewSummary = new() { TextWrapping = TextWrapping.Wrap };
    private AppSettings _baselineSettings = new();
    internal IReadOnlyList<TaskConfigurationReview> TaskReview { get; private set; } = [];
    internal string CapabilityText => _capabilities.Text ?? "";

    private Control CreateOverviewPage() => new StackPanel
    {
        Spacing = 12,
        Children =
        {
            Text("当前任务配置", "Current task configuration", true),
            Text("显示当前表单草稿，保存后才会生效。仅检查配置与固定样例测试记录；不会联网、启动模型或读取密钥。",
                "This is the current draft; changes apply only after saving. Review uses configuration and recorded sample tests, with no network requests, model startup or key reads."),
            _overviewSummary,
            new WrapPanel { Children =
            {
                Button("管理服务与模型", "Manage services", (_, _) => _tabs.SelectedIndex = 1),
                Button("调整任务配置", "Edit task routing", (_, _) => _tabs.SelectedIndex = 2),
                Button("更新配置概览", "Refresh overview", (_, _) => RefreshOverview())
            } },
            _overviewRows,
            Text("二维码解析始终在本机完成。远程文字任务发送识别文本，远程视觉任务发送所选截图；不会静默换用其他云服务。",
                "QR decoding always stays local. Remote text tasks send recognized text; remote vision tasks send the selected screenshot. There is no silent switch to another cloud service.")
        }
    };

    /// <summary>Read the editor without staging secrets, saving settings or mutating a profile.</summary>
    private ModelServiceProfile? CurrentEditorProfile()
    {
        if (_editing is null) return null;
        var endpoint = _endpoint.Text ?? "";
        var protocol = (_protocol.SelectedItem as ApiProtocolChoice)?.Id ?? ModelApiProtocols.ChatCompletions;
        var sameIdentity = false;
        try
        {
            endpoint = ModelServiceProfile.CanonicalEndpoint(endpoint);
            sameIdentity = endpoint == ModelServiceProfile.CanonicalEndpoint(_editing.Endpoint) && protocol == _editing.Protocol;
        }
        catch (ProviderException) { }
        var models = sameIdentity ? _editing.Models : [];
        if (!string.IsNullOrWhiteSpace(_key.Text) || _clearKey.IsChecked == true)
            models = models.Select(m => m with { TextVerified = false, VisionVerified = false }).ToArray();
        return _editing with { Name = _name.Text?.Trim() ?? "", Endpoint = endpoint, Protocol = protocol,
            DefaultModel = _model.Text?.Trim() ?? "", Models = models };
    }

    internal void RefreshOverview()
    {
        if (_loading || _closed || _routeControls.Count != ModelTasks.All.Count) return;
        var current = CurrentEditorProfile();
        var profiles = _profiles.Select(profile => current?.Id == profile.Id ? current : profile).ToArray();
        var configuration = _configuration with { Services = profiles!, Tasks = ReadRoutes() };
        TaskReview = ConfigurationReview.Read(_baselineSettings with { ModelConfiguration = configuration });
        var invalid = TaskReview.Count(t => t.Evidence == ConfigurationEvidence.NeedsConfiguration);
        _overviewSummary.Text = T($"{profiles.Length} 个服务档案 · {TaskReview.Count} 项任务 · {invalid} 项配置需处理",
            $"{profiles.Length} service profiles · {TaskReview.Count} tasks · {invalid} configuration issues");
        _overviewRows.Children.Clear();
        foreach (var task in TaskReview)
        {
            var body = new StackPanel { Spacing = 5 };
            body.Children.Add(new TextBlock { Text = TaskName(task.Task, UiText.IsEnglish), FontWeight = FontWeight.SemiBold });
            var source = task.Service switch
            {
                ModelConfiguration.Managed => T("本机托管模型", "Managed local model"),
                "local-paddle" => "PaddleOCR", "local-argos" => "Argos",
                "" => T("兼容配置", "Compatibility configuration"),
                _ => task.Service
            };
            body.Children.Add(new TextBlock { Text = source + (task.Model.Length > 0 ? " · " + task.Model : ""), TextWrapping = TextWrapping.Wrap });
            var evidence = new TextBlock { Text = EvidenceLabel(task.Evidence), TextWrapping = TextWrapping.Wrap };
            ThemeResources.Use(evidence, TextBlock.ForegroundProperty,
                task.Evidence == ConfigurationEvidence.NeedsConfiguration ? "DangerTextBrush" : "SecondaryTextBrush");
            body.Children.Add(evidence);
            if (task.Destination.Length > 0)
                body.Children.Add(new TextBlock { Text = (task.Remote ? T("远程目的地：", "Remote destination: ") : T("本机服务：", "Local service: ")) + task.Destination, TextWrapping = TextWrapping.Wrap });
            if (task.Inherited) body.Children.Add(new TextBlock { Text = T("沿用原配置；可在任务配置中改为独立绑定。", "Follows compatibility settings; assign an independent route under Tasks."), TextWrapping = TextWrapping.Wrap });
            if (task.Task is ModelTasks.Ocr or ModelTasks.Translation)
                body.Children.Add(new TextBlock { Text = task.AllowOfflineFallback
                    ? T("失败策略：允许轻量离线回退", "Failure policy: lightweight offline fallback allowed")
                    : T("失败策略：停止并报告，不切换服务", "Failure policy: stop and report; no provider switch"), TextWrapping = TextWrapping.Wrap });
            _overviewRows.Children.Add(new Border { Name = "TaskReview-" + task.Task, Classes = { "workspace-inset" },
                Padding = new Thickness(14), HorizontalAlignment = HorizontalAlignment.Stretch, Child = body });
        }
    }

    private static string EvidenceLabel(ConfigurationEvidence evidence) => evidence switch
    {
        ConfigurationEvidence.NeedsConfiguration => T("需要配置：检查服务地址、协议和模型。", "Needs configuration: check service URL, protocol and model."),
        ConfigurationEvidence.SampleTested => T("固定样例已测试；不代表当前可达、额度充足或所有任务质量已验证。", "A fixed sample was tested; current reachability, quota and task quality are not guaranteed."),
        ConfigurationEvidence.ManagedOnDemand => T("本机模型按需加载；文件和后端状态见运行状态。", "Local model loads on demand; check Runtime status for files and backend readiness."),
        ConfigurationEvidence.OfflineComponent => T("本机离线组件；安装状态见运行状态。", "Local offline component; check Runtime status for installation readiness."),
        _ => T("未验证；模型列表可见不等于可推理或支持图片。", "Unverified; a listed model does not prove inference access or image support.")
    };
}
