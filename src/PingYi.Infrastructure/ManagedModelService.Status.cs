using PingYi.Core;

namespace PingYi.Infrastructure;

public sealed record PassiveModelStatus(bool FilesPresent, bool Running, string ModelName,
    string RuntimeDescription, string? RunningBackend);

public sealed partial class ManagedModelService
{
    /// <summary>Read metadata only. Never download, hash GiB of weights, start or stop a server.</summary>
    public PassiveModelStatus InspectForStatus(AppSettings settings)
    {
        ManagedMultimodalModels.TryGet(settings.ManagedModelPackageId, out var model);
        var filesPresent = false;
        if (model is not null)
        {
            try
            {
                var root = GetModelDirectory(model);
                filesPresent = new[] { model.ModelFile, model.ProjectorFile }.All(file =>
                {
                    var info = new FileInfo(Path.Combine(root, file.FileName));
                    return info.Exists && info.Length == file.Size;
                });
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        var running = false;
        try { running = _ownedProcess is { HasExited: false } && _runningModelId == model?.Id && _runningBackendId is not null; }
        catch (Exception error) when (error is InvalidOperationException) { }
        return new(filesPresent, running, model?.DisplayName ?? settings.CustomTranslationModel,
            running ? CurrentRuntimeDescription : "", running ? _runningBackendId : null);
    }
}
