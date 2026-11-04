using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PingYi.Core;
using Xunit;

namespace PingYi.App.Tests;

public sealed class ConfigurationOverviewViewportTests
{
    private static IEnumerable<T> Controls<T>(Control c) where T : Control =>
        c.GetLogicalDescendants().OfType<T>().Distinct();
    private static T Named<T>(Control c, string name) where T : Control => Controls<T>(c).Single(x => x.Name == name);
    private static void Flush(Window w) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); }
    private static AppSettings Sample()
    {
        var service = new ModelServiceProfile { Id = "preview-api", Name = "Synthetic API", Endpoint = "https://example.test/v1",
            DefaultModel = "example-vision", Models = [new("example-vision")] };
        return new() { ModelConfiguration = new() { Services = [service], Tasks = new()
        {
            Ocr = new(service.Id), Translation = new(service.Id),
            Description = new(service.Id, "", false), Prompt = new(service.Id, "", false)
        } } };
    }

    [AvaloniaFact]
    public void Blank_route_model_explains_default_and_updates_without_overwriting_the_binding()
    {
        UiText.Configure("zh-CN"); var window = new SettingsWindow();
        try
        {
            var view = window.ConfigurationCenter; view.Load(Sample());
            window.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 3;
            window.Show(); Flush(window);
            Named<TabControl>(view, "ConfigurationTabs").SelectedIndex = 2; Flush(window);
            var help = Named<TextBlock>(view, "RouteHelp-translation");
            Assert.Contains("留空", help.Text); Assert.Contains("example-vision", help.Text);
            view.ModelEditor.Text = "changed-default"; Flush(window);
            Assert.Contains("changed-default", help.Text);
            var model = Named<ComboBox>(view, "RouteModel-translation");
            Assert.True(string.IsNullOrEmpty(model.Text));
            model.Text = "task-override"; Flush(window);
            Assert.Contains("task-override", help.Text);
            Assert.Equal("task-override", view.BuildDraft().Tasks.Translation.ModelId);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Inherited_routes_review_current_compatibility_draft_without_normalizing_an_invalid_url()
    {
        UiText.Configure("en-US"); var window = new SettingsWindow();
        try
        {
            var view = window.ConfigurationCenter;
            view.Load(new AppSettings { ModelConfiguration = new() });
            view.CompatibilityDraft = () => new() { CustomTranslationEndpoint = "https://new.example/v1", CustomTranslationModel = "draft-model" };
            view.RefreshOverview();
            Assert.True(view.TaskReview[1].Inherited); Assert.True(view.TaskReview[1].Remote);
            Assert.Equal("draft-model", view.TaskReview[1].Model);
            Assert.Equal("https://new.example", view.TaskReview[1].Destination);
            view.CompatibilityDraft = () => new() { CustomTranslationEndpoint = "not a URL" };
            view.RefreshOverview();
            Assert.Equal(ConfigurationEvidence.NeedsConfiguration, view.TaskReview[1].Evidence);
            Assert.Empty(view.PendingKeys);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1040, 760, "zh-CN", false)] [InlineData(1040, 760, "en-US", true)]
    [InlineData(800, 560, "zh-CN", true)] [InlineData(800, 560, "en-US", false)]
    public void Every_task_is_scroll_accessible_with_a_fixed_save_bar(int width, int height, string language, bool dark)
    {
        UiText.Configure(language);
        var window = new SettingsWindow { Width = width, Height = height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            var view = window.ConfigurationCenter; view.Load(Sample());
            window.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 3;
            window.SetGlobalStatus(language == "zh-CN" ? "界面测试预览 · 合成服务与模型，不代表真实账号状态" : "UI test preview · synthetic service and models, not a real account", false);
            window.Show(); Flush(window);
            var tabs = Named<TabControl>(view, "ConfigurationTabs");
            var scroll = view.GetVisualAncestors().OfType<ScrollViewer>().First();
            var save = window.FindControl<Button>("SaveSettingsButton")!;
            for (var page = 0; page < 4; page++)
            {
                tabs.SelectedIndex = page; Flush(window);
                scroll.Offset = default; Flush(window);
                Save(window, $"config-verified-{page}-{width}-{language}-{dark}-top");
                scroll.Offset = new Vector(0, scroll.Extent.Height); Flush(window);
                var last = page == 0 ? (Control)Named<Border>(view, "TaskReview-prompt")
                    : page == 2 ? Named<TextBlock>(view, "RouteHelp-prompt") : null;
                if (last is not null)
                {
                    var point = last.TranslatePoint(default, scroll);
                    Assert.NotNull(point);
                    Assert.True(point.Value.Y + last.Bounds.Height > 0 && point.Value.Y < scroll.Bounds.Height,
                        "The last task must intersect the scroll viewport, not be hidden below it.");
                }
                var buttonPoint = save.TranslatePoint(default, window);
                Assert.NotNull(buttonPoint);
                Assert.True(buttonPoint.Value.Y >= 0 && buttonPoint.Value.Y + save.Bounds.Height <= window.ClientSize.Height + 1);
                Save(window, $"config-verified-{page}-{width}-{language}-{dark}-bottom");
            }
        }
        finally { window.Close(); }
    }
    private static void Save(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("PINGYI_UI_ARTIFACTS");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"));
    }
}
