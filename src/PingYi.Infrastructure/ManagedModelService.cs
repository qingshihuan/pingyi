using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using PingYi.Core;

namespace PingYi.Infrastructure;

public sealed record ManagedModelProgress(
    string Phase,
    string Message,
    long BytesCompleted,
    long TotalBytes,
    bool IsIndeterminate = false)
{
    public double Percentage => TotalBytes <= 0
        ? 0
        : Math.Clamp(BytesCompleted * 100d / TotalBytes, 0, 100);
}

public sealed record ManagedModelStatus(
    bool IsInstalled,
    bool HasPartialDownload,
    string Message,
    string DirectoryPath);

public sealed class ManagedModelService : IAsyncDisposable
{
    private static readonly Uri ManagedModelsEndpoint = new("http://127.0.0.1:18080/v1/models");
    private static readonly Uri ManagedHealthEndpoint = new("http://127.0.0.1:18080/health");
    private readonly AppDataPaths _paths;
    private readonly HttpClient _downloadClient;
    private readonly HttpClient _probeClient;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly ConcurrentQueue<string> _recentServerErrors = new();
    private Process? _ownedProcess;
    private string? _runningModelId;
    private string? _runningBackendId;

    public ManagedModelService(AppDataPaths paths)
    {
        _paths = paths;
        _downloadClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _downloadClient.DefaultRequestHeaders.UserAgent.ParseAdd("PingYi-Complete/0.2");
        _probeClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2)
        };
    }

    public bool IsCompleteEdition => AppEdition.IsComplete;

    public bool HasBundledRuntime => GetRuntimeCandidates().Count > 0;

    public string GetModelDirectory(ManagedMultimodalModel model) =>
        Path.Combine(_paths.ManagedModelDirectory, model.Id);

    public async Task<ManagedModelStatus> GetStatusAsync(
        ManagedMultimodalModel model,
        CancellationToken cancellationToken = default)
    {
        var directory = GetModelDirectory(model);
        var modelValid = await IsFileValidAsync(
            Path.Combine(directory, model.ModelFile.FileName),
            model.ModelFile,
            cancellationToken);
        var projectorValid = await IsFileValidAsync(
            Path.Combine(directory, model.ProjectorFile.FileName),
            model.ProjectorFile,
            cancellationToken);
        var hasPartial = File.Exists(Path.Combine(directory, model.ModelFile.FileName + ".partial")) ||
                         File.Exists(Path.Combine(directory, model.ProjectorFile.FileName + ".partial"));

        return modelValid && projectorValid
            ? new ManagedModelStatus(true, hasPartial, "模型与视觉组件均已通过 SHA-256 校验", directory)
            : new ManagedModelStatus(
                false,
                hasPartial,
                hasPartial ? "发现未完成下载，可继续断点续传" : "尚未下载",
                directory);
    }

    public async Task DownloadAsync(
        ManagedMultimodalModel model,
        IProgress<ManagedModelProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            EnsureCompleteRuntimeAvailable();
            var directory = GetModelDirectory(model);
            Directory.CreateDirectory(directory);
            EnsureFreeDiskSpace(model, directory);

            var files = new[] { model.ModelFile, model.ProjectorFile };
            long completedBefore = 0;
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = Path.Combine(directory, file.FileName);
                if (await IsFileValidAsync(destination, file, cancellationToken))
                {
                    completedBefore += file.Size;
                    progress?.Report(new ManagedModelProgress(
                        "download",
                        $"{file.FileName} 已存在并通过校验",
                        completedBefore,
                        model.TotalSize));
                    continue;
                }

                await DownloadFileAsync(
                    model.BuildDownloadUri(file),
                    destination,
                    file,
                    completedBefore,
                    model.TotalSize,
                    progress,
                    cancellationToken);
                completedBefore += file.Size;
            }

            progress?.Report(new ManagedModelProgress(
                "complete",
                "模型下载完成并通过 SHA-256 校验",
                model.TotalSize,
                model.TotalSize));
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<string> EnsureStartedAsync(
        ManagedMultimodalModel model,
        IProgress<ManagedModelProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        await EnsureStartedAsync(
            model,
            ManagedRuntimeBackends.Auto.Id,
            progress,
            cancellationToken);

    public async Task<string> EnsureStartedAsync(
        ManagedMultimodalModel model,
        string backendId,
        IProgress<ManagedModelProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            EnsureCompleteRuntimeAvailable();
            var status = await GetStatusAsync(model, cancellationToken);
            if (!status.IsInstalled)
            {
                throw new InvalidOperationException("模型文件尚未完整下载或校验失败。");
            }

            var normalizedBackend = ManagedRuntimeBackends.Normalize(backendId);
            if (_ownedProcess is { HasExited: true })
            {
                await StopOwnedProcessCoreAsync();
            }

            if (await EndpointServesModelAsync(model.ModelAlias, cancellationToken))
            {
                var ownedProcessMatches = _ownedProcess is not null &&
                                          string.Equals(_runningModelId, model.Id, StringComparison.OrdinalIgnoreCase) &&
                                          (normalizedBackend == ManagedRuntimeBackends.Auto.Id ||
                                           string.Equals(_runningBackendId, normalizedBackend, StringComparison.OrdinalIgnoreCase));
                if (_ownedProcess is null || ownedProcessMatches)
                {
                    progress?.Report(new ManagedModelProgress(
                        "ready",
                        "本机模型服务已经运行",
                        model.TotalSize,
                        model.TotalSize));
                    return _ownedProcess is null
                        ? "已连接正在运行的本机模型服务；其计算后端由该服务决定"
                        : $"本机模型服务已通过 {DescribeBackend(_runningBackendId)} 运行";
                }

                await StopOwnedProcessCoreAsync();
            }
            else if (_ownedProcess is not null)
            {
                await StopOwnedProcessCoreAsync();
            }
            else if (await EndpointRespondsAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "端口 18080 已被其他模型服务占用。请关闭该服务，或在高级设置中继续使用自定义接口。");
            }

            Exception? lastError = null;
            foreach (var runtime in GetRuntimeCandidates(normalizedBackend))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var backendName = runtime.IsGpu ? "Vulkan 通用显卡" : "CPU";
                progress?.Report(new ManagedModelProgress(
                    "starting",
                    $"正在使用 {backendName} 后端加载模型…",
                    model.TotalSize,
                    model.TotalSize,
                    true));
                try
                {
                    StartProcess(runtime, model);
                    await WaitUntilReadyAsync(model, TimeSpan.FromSeconds(75), cancellationToken);
                    _runningModelId = model.Id;
                    _runningBackendId = runtime.IsGpu
                        ? ManagedRuntimeBackends.Vulkan.Id
                        : ManagedRuntimeBackends.Cpu.Id;
                    return runtime.IsGpu
                        ? "模型已通过 Vulkan 显卡后端启动"
                        : normalizedBackend == ManagedRuntimeBackends.Cpu.Id
                            ? "模型已通过 CPU 后端启动"
                            : "显卡后端不可用，已自动回退 CPU 并启动";
                }
                catch (Exception exception)
                {
                    lastError = exception;
                    await StopOwnedProcessCoreAsync();
                    if (runtime.IsGpu && normalizedBackend == ManagedRuntimeBackends.Auto.Id)
                    {
                        progress?.Report(new ManagedModelProgress(
                            "fallback",
                            "Vulkan 启动失败，正在自动回退 CPU…",
                            model.TotalSize,
                            model.TotalSize,
                            true));
                    }
                }
            }

            throw new InvalidOperationException(
                $"无法启动内置 llama.cpp。{BuildServerErrorMessage(lastError)}",
                lastError);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            await StopOwnedProcessCoreAsync();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task DownloadFileAsync(
        Uri source,
        string destination,
        ManagedModelFile expected,
        long completedBefore,
        long packageSize,
        IProgress<ManagedModelProgress>? progress,
        CancellationToken cancellationToken)
    {
        var partial = destination + ".partial";
        var existingLength = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (existingLength > expected.Size)
        {
            File.Delete(partial);
            existingLength = 0;
        }

        using var response = await SendDownloadRequestAsync(source, existingLength, cancellationToken);
        var append = existingLength > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!append)
        {
            existingLength = 0;
        }

        var mode = append ? FileMode.Append : FileMode.Create;
        await using (var output = new FileStream(
                         partial,
                         mode,
                         FileAccess.Write,
                         FileShare.Read,
                         1024 * 1024,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        {
            var buffer = new byte[1024 * 1024];
            long downloaded = existingLength;
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloaded += read;
                progress?.Report(new ManagedModelProgress(
                    "download",
                    $"正在下载 {expected.FileName}",
                    completedBefore + Math.Min(downloaded, expected.Size),
                    packageSize));
            }
        }

        var actualLength = new FileInfo(partial).Length;
        if (actualLength != expected.Size)
        {
            throw new IOException(
                $"{expected.FileName} 下载长度不完整（{actualLength:N0}/{expected.Size:N0} 字节），可再次点击继续下载。");
        }

        progress?.Report(new ManagedModelProgress(
            "verify",
            $"正在校验 {expected.FileName}…",
            completedBefore + expected.Size,
            packageSize,
            true));
        if (!await IsFileValidAsync(partial, expected, cancellationToken))
        {
            File.Delete(partial);
            throw new InvalidDataException($"{expected.FileName} 的 SHA-256 不匹配，已移除损坏文件，请重试。");
        }

        File.Move(partial, destination, true);
    }

    private async Task<HttpResponseMessage> SendDownloadRequestAsync(
        Uri source,
        long existingLength,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, source);
        if (existingLength > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingLength, null);
        }

        var response = await _downloadClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        request.Dispose();
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new HttpRequestException($"魔搭下载返回 HTTP {(int)response.StatusCode}。", null, response.StatusCode);
        }

        return response;
    }

    private static async Task<bool> IsFileValidAsync(
        string path,
        ManagedModelFile expected,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != expected.Size)
        {
            return false;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return string.Equals(Convert.ToHexString(hash), expected.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureFreeDiskSpace(ManagedMultimodalModel model, string directory)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(directory));
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var available = new DriveInfo(root).AvailableFreeSpace;
        var required = (long)(model.TotalSize * 1.1);
        if (available < required)
        {
            throw new IOException(
                $"磁盘空间不足：至少需要 {FormatBytes(required)}，当前可用 {FormatBytes(available)}。");
        }
    }

    private void EnsureCompleteRuntimeAvailable()
    {
        if (!IsCompleteEdition || !HasBundledRuntime)
        {
            throw new InvalidOperationException("此功能需要屏译完全版内置的 llama.cpp 运行时。");
        }
    }

    private IReadOnlyList<RuntimeCandidate> GetRuntimeCandidates()
        => GetRuntimeCandidates(ManagedRuntimeBackends.Auto.Id);

    private IReadOnlyList<RuntimeCandidate> GetRuntimeCandidates(string backendId)
    {
        var executableName = OperatingSystem.IsWindows() ? "llama-server.exe" : "llama-server";
        var candidates = new[]
        {
            new RuntimeCandidate(
                Path.Combine(_paths.LlamaRuntimeDirectory, "vulkan", executableName),
                true),
            new RuntimeCandidate(
                Path.Combine(_paths.LlamaRuntimeDirectory, "cpu", executableName),
                false)
        };
        var normalized = ManagedRuntimeBackends.Normalize(backendId);
        return candidates
            .Where(candidate => File.Exists(candidate.ExecutablePath))
            .Where(candidate => normalized switch
            {
                "vulkan" => candidate.IsGpu,
                "cpu" => !candidate.IsGpu,
                _ => true
            })
            .ToArray();
    }

    private void StartProcess(RuntimeCandidate runtime, ManagedMultimodalModel model)
    {
        _recentServerErrors.Clear();
        var directory = GetModelDirectory(model);
        var startInfo = new ProcessStartInfo
        {
            FileName = runtime.ExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(runtime.ExecutablePath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        AddArgument(startInfo, "-m", Path.Combine(directory, model.ModelFile.FileName));
        AddArgument(startInfo, "--mmproj", Path.Combine(directory, model.ProjectorFile.FileName));
        AddArgument(startInfo, "--alias", model.ModelAlias);
        AddArgument(startInfo, "--host", "127.0.0.1");
        AddArgument(startInfo, "--port", "18080");
        AddArgument(startInfo, "--ctx-size", "8192");
        AddArgument(startInfo, "--parallel", "1");
        AddArgument(startInfo, "--n-gpu-layers", runtime.IsGpu ? "99" : "0");
        AddArgument(startInfo, "--flash-attn", "auto");
        AddArgument(startInfo, "--reasoning", "off");
        AddArgument(startInfo, "--image-max-tokens", "1120");
        startInfo.ArgumentList.Add("--no-webui");
        startInfo.ArgumentList.Add("--log-disable");

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += CaptureServerLine;
        process.ErrorDataReceived += CaptureServerLine;
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("llama.cpp 进程未能启动。");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _ownedProcess = process;
    }

    private static void AddArgument(ProcessStartInfo startInfo, string name, string value)
    {
        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add(value);
    }

    private void CaptureServerLine(object sender, DataReceivedEventArgs eventArgs)
    {
        if (string.IsNullOrWhiteSpace(eventArgs.Data))
        {
            return;
        }

        _recentServerErrors.Enqueue(eventArgs.Data);
        while (_recentServerErrors.Count > 12)
        {
            _recentServerErrors.TryDequeue(out _);
        }
    }

    private async Task WaitUntilReadyAsync(
        ManagedMultimodalModel model,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        while (!deadline.IsCancellationRequested)
        {
            if (_ownedProcess is { HasExited: true })
            {
                throw new InvalidOperationException(
                    $"llama.cpp 提前退出（代码 {_ownedProcess.ExitCode}）。{BuildServerErrorMessage(null)}");
            }

            try
            {
                using var response = await _probeClient.GetAsync(ManagedHealthEndpoint, deadline.Token);
                if (response.IsSuccessStatusCode && await EndpointServesModelAsync(model.ModelAlias, deadline.Token))
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (HttpRequestException)
            {
                // Loading is still in progress.
            }

            await Task.Delay(500, deadline.Token).ContinueWith(
                _ => { },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException("llama.cpp 在 75 秒内未完成模型加载。");
    }

    private async Task<bool> EndpointServesModelAsync(string expectedAlias, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _probeClient.GetAsync(ManagedModelsEndpoint, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return document.RootElement.TryGetProperty("data", out var data) &&
                   data.ValueKind == JsonValueKind.Array &&
                   data.EnumerateArray().Any(item =>
                       item.TryGetProperty("id", out var id) &&
                       string.Equals(id.GetString(), expectedAlias, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            return false;
        }
    }

    private async Task<bool> EndpointRespondsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _probeClient.GetAsync(ManagedModelsEndpoint, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private async Task StopOwnedProcessCoreAsync()
    {
        var process = _ownedProcess;
        _ownedProcess = null;
        _runningModelId = null;
        _runningBackendId = null;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        catch (InvalidOperationException)
        {
            // The process already ended between checks.
        }
        finally
        {
            process.Dispose();
        }
    }

    private string BuildServerErrorMessage(Exception? exception)
    {
        var detail = _recentServerErrors.LastOrDefault(line =>
            line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("failed", StringComparison.OrdinalIgnoreCase));
        return !string.IsNullOrWhiteSpace(detail)
            ? detail
            : exception?.Message ?? "请确认显卡驱动正常，或重新安装完全版运行时。";
    }

    private static string FormatBytes(long bytes) => $"{bytes / 1024d / 1024d / 1024d:0.00} GiB";

    private static string DescribeBackend(string? backendId) =>
        string.Equals(backendId, ManagedRuntimeBackends.Vulkan.Id, StringComparison.OrdinalIgnoreCase)
            ? "Vulkan 通用显卡"
            : "CPU";

    public async ValueTask DisposeAsync()
    {
        await StopOwnedProcessCoreAsync();
        _downloadClient.Dispose();
        _probeClient.Dispose();
        _operationGate.Dispose();
    }

    private sealed record RuntimeCandidate(string ExecutablePath, bool IsGpu);
}
