using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using PingYi.Core;
using Xunit;

namespace PingYi.App.Tests;

public sealed class ConfigurationCenterUiTests
{
    private static ModelServiceProfile A => new() { Id = "test-local", Name = "本机视觉 / Local vision",
        Endpoint = "http://127.0.0.1:8080/v1/chat/completions", DefaultModel = "local-vision",
        Models = [new("local-vision", true, true)] };
    private static ModelServiceProfile B => new() { Id = "test-api", Name = "示例 API / Example API",
        Endpoint = "https://example.test/v1/chat/completions", Protocol = ModelApiProtocols.Responses,
        DefaultModel = "example-text", Models = [new("example-text"), new("example-vision")] };
    private static AppSettings Sample => new() { ModelConfiguration = new() { Services = [A, B], Tasks = new()
        { Ocr = new(A.Id), Translation = new(B.Id), Description = new(B.Id, "example-vision", false), Prompt = new(A.Id, "local-vision", false) } } };
    private static void Flush(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static Button FindButton(Control view, string content) => view.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, content));

    [AvaloniaFact]
    public void Loading_navigating_and_changing_language_never_send_requests_or_discard_drafts()
    {
        UiText.Configure("zh-CN");
        var window = new SettingsWindow();
        try
        {
            var view = window.ConfigurationCenter; view.Load(Sample);
            view.Discover = (_, _, _) => throw new InvalidOperationException("No network allowed");
            view.Verify = (_, _, _, _, _) => throw new InvalidOperationException("No inference allowed");
            window.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 3;
            window.Show(); Flush(window);
            view.ModelEditor.Text = "unsaved-模型";
            view.KeyEditor.Text = "synthetic-key";
            UiText.Configure("en-US"); Flush(window);
            window.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 5; Flush(window);
            window.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 3; Flush(window);
            Assert.Equal("synthetic-key", view.KeyEditor.Text);
            Assert.Equal('●', view.KeyEditor.PasswordChar);
            var draft = view.BuildDraft();
            Assert.Equal("unsaved-模型", draft.Services[0].DefaultModel);
            Assert.Equal("synthetic-key", view.PendingKeys[A.CredentialRef]);
            Assert.Equal("local-vision", Sample.ModelConfiguration!.Services[0].DefaultModel);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Editing_destination_clears_draft_credentials_and_stale_model_choices()
    {
        UiText.Configure("zh-CN"); var window = new SettingsWindow();
        try
        {
            var view = window.ConfigurationCenter; view.Load(Sample);
            window.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 3;
            window.Show(); Flush(window);
            view.KeyEditor.Text = "old-endpoint-test-key";
            view.BuildDraft(); Assert.NotEmpty(view.PendingKeys);
            view.EndpointEditor.Text = "https://new.example/v1"; Flush(window);
            Assert.True(string.IsNullOrEmpty(view.KeyEditor.Text)); Assert.Empty(view.PendingKeys);
            Assert.Empty(view.ModelEditor.Items);
            var draft = view.BuildDraft();
            Assert.Empty(draft.Services[0].Models);
            Assert.NotEqual(A.CredentialRef, draft.Services[0].CredentialRef);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Discovery_uses_current_draft_key_without_saving_settings_or_inventing_vision_support()
    {
        UiText.Configure("zh-CN"); var window = new SettingsWindow();
        try
        {
            var view = window.ConfigurationCenter; view.Load(Sample);
            var calls = 0;
            view.Discover = (profile, key, _) =>
            {
                calls++; Assert.Equal(A.Id, profile.Id); Assert.Equal("new-test-key", key);
                return Task.FromResult<IReadOnlyList<string>>(["new-model", "another-model"]);
            };
            window.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 3;
            window.Show(); Flush(window);
            view.KeyEditor.Text = "new-test-key";
            Click(FindButton(view, "获取模型列表")); await view.OperationTask; Flush(window);
            Assert.Equal(1, calls);
            var draft = view.BuildDraft();
            Assert.Equal(2, draft.Services[0].Models.Length);
            Assert.All(draft.Services[0].Models, m => { Assert.False(m.TextVerified); Assert.False(m.VisionVerified); });
            Assert.Equal("local-vision", view.ModelEditor.Text); // Do not silently choose a new paid model.
            Assert.Single(Sample.ModelConfiguration!.Services[0].Models);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Closing_cancels_discovery_and_clears_unsaved_keys()
    {
        UiText.Configure("zh-CN"); var window = new SettingsWindow();
        var view = window.ConfigurationCenter; view.Load(Sample);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = false;
        view.Discover = async (_, _, token) =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { cancelled = true; throw; }
            return [];
        };
        window.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 3;
        window.Show(); Flush(window); view.KeyEditor.Text = "never-persist";
        Click(FindButton(view, "获取模型列表")); await started.Task;
        window.Close(); await view.OperationTask;
        Assert.True(cancelled); Assert.Empty(view.PendingKeys); Assert.True(string.IsNullOrEmpty(view.KeyEditor.Text));
    }

    [AvaloniaFact]
    public void Bound_profiles_cannot_be_deleted_and_per_task_model_choices_are_persisted_in_draft()
    {
        UiText.Configure("zh-CN"); var window = new SettingsWindow();
        try
        {
            var view = window.ConfigurationCenter; view.Load(Sample);
            window.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 3;
            window.Show(); Flush(window);
            Click(FindButton(view, "删除服务")); Click(FindButton(view, "删除服务"));
            Assert.Equal(2, view.BuildDraft().Services.Length);
            var tabs = view.GetLogicalDescendants().OfType<TabControl>().Single();
            tabs.SelectedIndex = 1; Flush(window);
            var service = view.GetLogicalDescendants().OfType<ComboBox>().Single(c => c.Name == "RouteService-translation");
            service.SelectedItem = service.Items.Cast<object>().Single(i => i.ToString() == A.Name);
            var model = view.GetLogicalDescendants().OfType<ComboBox>().Single(c => c.Name == "RouteModel-translation");
            model.Text = "local-text-override";
            var draft = view.BuildDraft();
            Assert.Equal(A.Id, draft.Tasks.Translation.ServiceId);
            Assert.Equal("local-text-override", draft.Tasks.Translation.ModelId);
            Assert.Equal(B.Id, draft.Tasks.Description.ServiceId);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Api_and_hybrid_onboarding_open_configuration_without_a_download(bool hybrid)
    {
        UiText.Configure("zh-CN"); var choices = new List<InitialSetupChoice>();
        var setup = new FirstRunSetupWindow(false, (_, _, _, _) => throw new InvalidOperationException("Must not download"),
            (choice, _) => { choices.Add(choice); return Task.CompletedTask; });
        setup.Show();
        Click(hybrid ? setup.HybridButton : setup.ApiButton); await setup.OperationTask;
        Assert.Equal([InitialSetupChoice.ConfigureExisting], choices);
        setup.Close();
    }

    [AvaloniaTheory]
    [InlineData(1040, "zh-CN", false)] [InlineData(1040, "en-US", true)]
    [InlineData(800, "zh-CN", true)] [InlineData(800, "en-US", false)]
    public void Configuration_center_renders_real_service_and_route_controls_without_hiding_save(int width, string language, bool dark)
    {
        UiText.Configure(language);
        var window = new SettingsWindow { Width = width, Height = 760,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.ConfigurationCenter.Load(Sample);
            window.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 3;
            window.Show(); Flush(window);
            var save = window.FindControl<Button>("SaveSettingsButton")!;
            var point = save.TranslatePoint(default, window);
            Assert.NotNull(point); Assert.True(point.Value.Y >= 0 && point.Value.Y + save.Bounds.Height <= window.ClientSize.Height + 1);
            var directory = Environment.GetEnvironmentVariable("PINGYI_UI_ARTIFACTS");
            var tabs = window.ConfigurationCenter.GetLogicalDescendants().OfType<TabControl>().Single();
            for (var page = 0; page < 3; page++)
            {
                tabs.SelectedIndex = page; Flush(window);
                Assert.True(window.ConfigurationCenter.Bounds.Width > 100);
                if (string.IsNullOrEmpty(directory)) continue;
                Directory.CreateDirectory(directory);
                using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                frame.Save(Path.Combine(directory, $"configuration-{page}-{width}-{language}-{dark}.png"));
            }
        }
        finally { window.Close(); }
    }
}
