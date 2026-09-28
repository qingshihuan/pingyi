using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PingYi.Infrastructure;

/// <summary>Owns only a child started by this application, never processes found by name/port.</summary>
public sealed class OwnedProcessScope : IAsyncDisposable, IDisposable
{
    private static readonly object RegistryLock = new();
    private static readonly HashSet<OwnedProcessScope> Registry = [];
    private static bool _exiting;
    private readonly object _sync = new();
    private readonly Process _process;
    private SafeFileHandle? _job;
    private Task? _stopTask;
    private bool _disposed;

    static OwnedProcessScope() => AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        TerminateAllAtExit(TimeSpan.FromSeconds(2));

    private OwnedProcessScope(Process process) { _process = process; _job = TryCreateJob(process); }
    public Process Process => _process;
    internal bool HasWindowsJob => _job is { IsInvalid: false, IsClosed: false };

    public static OwnedProcessScope Start(ProcessStartInfo startInfo)
    {
        if (startInfo.UseShellExecute) throw new ArgumentException("Owned backends must not use a shell.", nameof(startInfo));
        lock (RegistryLock)
        {
            if (_exiting) throw new ObjectDisposedException(nameof(OwnedProcessScope), "Application is exiting.");
            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            try
            {
                if (!process.Start()) throw new InvalidOperationException("Backend process did not start.");
                // Open and retain the OS handle now; never re-resolve a PID to decide what to kill.
                _ = process.SafeHandle;
                var scope = new OwnedProcessScope(process);
                Registry.Add(scope);
                return scope;
            }
            catch
            {
                try { if (!process.HasExited) { process.Kill(true); process.WaitForExit(2000); } }
                catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException) { }
                process.Dispose();
                throw;
            }
        }
    }

    public Task StopAsync()
    {
        lock (_sync)
        {
            if (_disposed) return Task.CompletedTask;
            // Keep ownership after a failed stop so a final exit can retry termination.
            if (_stopTask is null || _stopTask.IsFaulted || _stopTask.IsCanceled) _stopTask = StopCoreAsync();
            return _stopTask;
        }
    }

    private async Task StopCoreAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        RequestTermination();
        try
        {
            await _process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            // Waiting for the root alone does not prove its descendants exited on Windows.
            while (!JobIsEmpty()) await Task.Delay(25, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            RequestTermination();
            throw new TimeoutException("Owned backend did not exit in time.");
        }
        lock (_sync)
        {
            _disposed = true;
            _job?.Dispose(); _job = null;
            _process.Dispose();
        }
        lock (RegistryLock) Registry.Remove(this);
    }

    private void RequestTermination()
    {
        lock (_sync)
        {
            if (_disposed) return;
            // A private, unnamed Job contains only the child we created and its descendants.
            if (_job is { IsInvalid: false, IsClosed: false }) TerminateJobObject(_job, 0);
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (Win32Exception)
            {
                // Tree enumeration can fail while the root remains accessible.
                try { if (!_process.HasExited) _process.Kill(); }
                catch (Exception e) when (e is InvalidOperationException or Win32Exception) { }
            }
        }
    }

    private bool JobIsEmpty()
    {
        lock (_sync)
        {
            if (_job is null || _job.IsClosed) return true;
            if (!QueryInformationJobObject(_job, 1, out var info, Marshal.SizeOf<JobAccounting>(), IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return info.ActiveProcesses == 0;
        }
    }

    /// <summary>Dispatcher-independent fallback for lifetime termination or initialization failures.</summary>
    public static bool TerminateAllAtExit(TimeSpan budget)
    {
        OwnedProcessScope[] owners;
        lock (RegistryLock) { _exiting = true; owners = Registry.ToArray(); }
        foreach (var owner in owners) owner.RequestTermination();
        var clock = Stopwatch.StartNew();
        var stopped = true;
        foreach (var owner in owners)
        {
            lock (owner._sync)
            {
                if (owner._disposed) continue;
                try
                {
                    var remaining = Math.Max(0, (int)(budget - clock.Elapsed).TotalMilliseconds);
                    if (!owner._process.WaitForExit(remaining)) stopped = false;
                    // Closing this last handle also tears down descendants after an abrupt exit.
                    owner._job?.Dispose(); owner._job = null;
                }
                catch (Exception e) when (e is InvalidOperationException or Win32Exception) { stopped = false; }
            }
        }
        return stopped;
    }

    public ValueTask DisposeAsync() => new(StopAsync());
    public void Dispose() => StopAsync().GetAwaiter().GetResult();

    private static SafeFileHandle? TryCreateJob(Process process)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid) { job.Dispose(); return null; }
        var limits = new JobLimits { Basic = new BasicLimits { Flags = 0x2000 } }; // KILL_ON_JOB_CLOSE
        if (SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf<JobLimits>()) &&
            AssignProcessToJobObject(job, process.SafeHandle)) return job;
        // Some host sandbox jobs disallow nesting. Normal owned-process cleanup still applies.
        job.Dispose();
        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long ProcessUserTime, JobUserTime;
        public uint Flags;
        public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct JobLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct JobAccounting
    {
        public long TotalUserTime, TotalKernelTime, ThisUserTime, ThisKernelTime;
        public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, ref JobLimits info, int length);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(SafeFileHandle job, uint code);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(SafeFileHandle job, int informationClass,
        out JobAccounting info, int length, IntPtr returnLength);
}
