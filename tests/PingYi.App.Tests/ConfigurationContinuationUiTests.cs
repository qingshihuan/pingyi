using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using PingYi.Core;
using Xunit;

namespace PingYi.App.Tests;

public sealed class ConfigurationContinuationUiTests
{
    private static ModelServiceProfile Profile => new() { Id = "test-api", Name = "Synthetic API",
        Endpoint = "https://example.test/v1/chat/completions", DefaultModel = "verified-model", Models = [new("verified-model", true, true)] };
    private static SettingsWindow Open()
    {
        UiText.Configure("zh-CN");
        var window = new SettingsWindow();
        window.ConfigurationCenter.Load(new() { ModelConfiguration = new() { Services = [Profile], Tasks = new()
            { Ocr = new(Profile.Id), Translation = new(Profile.Id), Description = new(Profile.Id), Prompt = new(Profile.Id) } } });
        window.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 3;
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); return window;
    }
    private static T Named<T>(Control view, string name) where T : Control =>
        view.GetLogicalDescendants().OfType<T>().Distinct().Single(c => c.Name == name);
    private static void Click(ConfigurationCenterView view, string label) => view.GetLogicalDescendants().OfType<Button>().Distinct()
        .Single(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void Overview_uses_unsaved_draft_without_network_or_key_staging()
    {
        var window = Open();
        try
        {
            var view = window.ConfigurationCenter;
            view.Discover = (_, _, _) => throw new InvalidOperationException("No network expected");
            view.Verify = (_, _, _, _, _) => throw new InvalidOperationException("No inference expected");
            view.ModelEditor.Text = "unsaved-model";
            view.KeyEditor.Text = "synthetic-unsaved-key";
            view.RefreshOverview();
            Assert.Equal("unsaved-model", view.TaskReview[1].Model);
            Assert.Equal(ConfigurationEvidence.Unverified, view.TaskReview[1].Evidence);
            Assert.Equal("synthetic-unsaved-key", view.KeyEditor.Text); Assert.Empty(view.PendingKeys);
            UiText.Configure("en-US"); Dispatcher.UIThread.RunJobs();
            Assert.Equal("synthetic-unsaved-key", view.KeyEditor.Text);
            Assert.Equal("unsaved-model", view.TaskReview[1].Model);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Manual_model_entry_and_key_deletion_update_capability_evidence_immediately()
    {
        var window = Open();
        try
        {
            var view = window.ConfigurationCenter;
            Assert.Contains("已测试", view.CapabilityText);
            view.ModelEditor.Text = "not-tested";
            Assert.DoesNotContain("已测试", view.CapabilityText);
            view.ModelEditor.Text = "verified-model";
            Assert.Contains("已测试", view.CapabilityText);
            view.GetLogicalDescendants().OfType<CheckBox>().Distinct().Single(c => Equals(c.Content, "删除此服务已保存的密钥")).IsChecked = true;
            Assert.DoesNotContain("已测试", view.CapabilityText);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Failed_retest_clears_only_the_capability_being_retested()
    {
        var window = Open();
        try
        {
            var view = window.ConfigurationCenter;
            view.Verify = (_, _, _, _, _) => throw new InvalidOperationException("Synthetic failure");
            Click(view, "测试文字能力"); await view.OperationTask;
            var entry = Assert.Single(Assert.Single(view.BuildDraft().Services).Models);
            Assert.False(entry.TextVerified); Assert.True(entry.VisionVerified);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Cancelled_test_cannot_restore_verification_when_handler_returns_late_success()
    {
        var window = Open();
        try
        {
            var view = window.ConfigurationCenter;
            var completion = new TaskCompletionSource<ModelCatalogEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
            view.Verify = (_, _, _, _, _) => completion.Task;
            Click(view, "测试图片能力");
            Click(view, "取消请求");
            completion.SetResult(new("verified-model", true, true));
            await view.OperationTask;
            var entry = Assert.Single(Assert.Single(view.BuildDraft().Services).Models);
            Assert.True(entry.TextVerified); Assert.False(entry.VisionVerified);
            Assert.Contains("取消", view.StatusMessage);
        }
        finally { window.Close(); }
    }
}
