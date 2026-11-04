namespace PingYi.Core;

/// <summary>Configuration evidence only, never live account, quota, driver or model readiness.</summary>
public enum ConfigurationEvidence { NeedsConfiguration, Unverified, SampleTested, OfflineComponent, ManagedOnDemand }

public sealed record TaskConfigurationReview(string Task, string Service, string Model, string Destination,
    bool Remote, bool Inherited, bool AllowOfflineFallback, ConfigurationEvidence Evidence, string? IssueCode = null);

public static class ConfigurationReview
{
    public static IReadOnlyList<TaskConfigurationReview> Read(AppSettings settings) =>
        ModelTasks.All.Select(task => ReadTask(settings, task)).ToArray();

    private static TaskConfigurationReview ReadTask(AppSettings settings, string task)
    {
        var binding = settings.ModelConfiguration?.Tasks.Get(task) ?? new ModelTaskBinding();
        var inherited = string.IsNullOrEmpty(binding.ServiceId);
        var fallback = task is ModelTasks.Ocr or ModelTasks.Translation && binding.AllowOfflineFallback;
        try
        {
            var route = ConfigurationRouting.Resolve(settings, task);
            var provider = task == ModelTasks.Ocr ? route.Settings.OcrProviderId : route.Settings.TranslationProviderId;
            if (task is ModelTasks.Ocr or ModelTasks.Translation && provider is not ("custom-chat" or "local-vlm-ocr"))
            {
                var offline = provider is "local-paddle" or "local-argos";
                return new(task, provider, "", "", !offline, inherited, fallback,
                    offline ? ConfigurationEvidence.OfflineComponent : ConfigurationEvidence.Unverified);
            }
            if (route.IsManaged)
                return new(task, ModelConfiguration.Managed, route.Settings.CustomTranslationModel, "", false,
                    inherited, fallback, ConfigurationEvidence.ManagedOnDemand);
            var endpoint = new Uri(ModelServiceProfile.CanonicalEndpoint(route.Settings.CustomTranslationEndpoint));
            var model = route.Settings.CustomTranslationModel;
            if (string.IsNullOrWhiteSpace(model))
                throw new ProviderException("task_model_missing", "Select a model for this task.");
            var profile = settings.ModelConfiguration?.Services.FirstOrDefault(p => p.Id == binding.ServiceId);
            var entry = profile?.Models.FirstOrDefault(m => m.Id == model);
            var tested = task == ModelTasks.Translation ? entry?.TextVerified == true : entry?.VisionVerified == true;
            return new(task, profile?.Name ?? "", model, endpoint.GetLeftPart(UriPartial.Authority), !endpoint.IsLoopback,
                inherited, fallback, tested ? ConfigurationEvidence.SampleTested : ConfigurationEvidence.Unverified);
        }
        catch (ProviderException error)
        {
            // Authored codes only: no URL, key, response content or untrusted error message.
            return new(task, "", "", "", false, inherited, fallback, ConfigurationEvidence.NeedsConfiguration, error.Code);
        }
    }
}
