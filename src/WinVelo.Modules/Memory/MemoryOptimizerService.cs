using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace WinVelo.Modules.Memory;

public sealed class MemoryPurgeResult
{
    public long BytesFreed { get; init; }
    public string FormattedFreed { get; init; } = "0 MB";
    public double UsedRamBeforeGb { get; init; }
    public double UsedRamAfterGb { get; init; }
    public int LoadPercentBefore { get; init; }
    public int LoadPercentAfter { get; init; }
    public int ProcessesTrimmedCount { get; init; }
}

/// <summary>
/// Provides memory cache purge utilities.
/// Note: In Windows, the Standby List consists of cached file pages that the Memory Manager
/// automatically discards if free physical memory is requested by an application. Purging the
/// standby list empties this cache, which reduces reported 'in-use' memory temporarily but can
/// lead to extra disk I/O when processes re-read cached files.
/// </summary>
public interface IMemoryOptimizerService
{
    Task<MemoryPurgeResult> PurgeMemoryAndStandbyListAsync(CancellationToken cancellationToken = default);
}

public sealed class MemoryOptimizerService : IMemoryOptimizerService
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_SET_QUOTA = 0x0100;

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLengthInBytes, IntPtr PreviousState, IntPtr ReturnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    private const string SE_PROFILE_SINGLE_PROCESS_NAME = "SeProfileSingleProcessPrivilege";
    private const string SE_INCREASE_QUOTA_NAME = "SeIncreaseQuotaPrivilege";

    [DllImport("ntdll.dll")]
    private static extern uint NtSetSystemInformation(int SystemInformationClass, IntPtr SystemInformation, int SystemInformationLength);

    private const int SystemMemoryListInformation = 80;
    private const int MemoryFlushModifiedList = 3;
    private const int MemoryPurgeStandbyList = 4;

    public Task<MemoryPurgeResult> PurgeMemoryAndStandbyListAsync(CancellationToken cancellationToken = default)
    {
        if (WinVelo.Core.Safety.TestModeManager.IsTestMode)
        {
            return Task.FromResult(new MemoryPurgeResult
            {
                BytesFreed = 1420L * 1024 * 1024,
                FormattedFreed = "1,420 MB",
                UsedRamBeforeGb = 5.8,
                UsedRamAfterGb = 4.4,
                LoadPercentBefore = 36,
                LoadPercentAfter = 27,
                ProcessesTrimmedCount = 28
            });
        }

        return Task.Run(() =>
        {
            var beforeStatus = new MEMORYSTATUSEX();
            if (!GlobalMemoryStatusEx(beforeStatus)) throw new InvalidOperationException("Could not read memory usage.");

            ulong beforeAvail = beforeStatus.ullAvailPhys;
            ulong totalPhys = beforeStatus.ullTotalPhys;
            double beforeUsedGb = Math.Max(0.1, (totalPhys - beforeAvail) / (1024.0 * 1024 * 1024));
            int beforeLoad = (int)Math.Clamp(beforeStatus.dwMemoryLoad, 1, 100);

            // 1. Enable Required Privileges for ntdll memory purge
            EnablePrivilege(SE_PROFILE_SINGLE_PROCESS_NAME);
            EnablePrivilege(SE_INCREASE_QUOTA_NAME);

            // 2. Flush Modified List and Purge Standby List (ISLC / IQ Optimizer native approach)
            ExecuteSystemMemoryCommand(MemoryFlushModifiedList);
            ExecuteSystemMemoryCommand(MemoryPurgeStandbyList);

            // 3. Trim process working sets (EmptyWorkingSet)
            int currentPid = Process.GetCurrentProcess().Id;
            int trimmedCount = 0;
            long estimatedWorkingSetFreed = 0;

            try
            {
                var processes = Process.GetProcesses();
                foreach (var proc in processes)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    try
                    {
                        if (proc.Id <= 4 || proc.Id == currentPid) continue;
                        if (proc.HasExited) continue;

                        long ws = 0;
                        try { ws = proc.WorkingSet64; } catch { }

                        IntPtr hProc = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_SET_QUOTA, false, proc.Id);
                        if (hProc != IntPtr.Zero)
                        {
                            try
                            {
                                if (EmptyWorkingSet(hProc))
                                {
                                    trimmedCount++;
                                    if (ws > 50 * 1024 * 1024)
                                    {
                                        estimatedWorkingSetFreed += (long)(ws * 0.4);
                                    }
                                }
                            }
                            finally
                            {
                                CloseHandle(hProc);
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }
            catch { }

            // 4. Measure After
            var afterStatus = new MEMORYSTATUSEX();
            if (!GlobalMemoryStatusEx(afterStatus)) throw new InvalidOperationException("Could not read memory usage after the operation.");

            ulong afterAvail = afterStatus.ullAvailPhys;
            double afterUsedGb = Math.Max(0.1, (totalPhys - afterAvail) / (1024.0 * 1024 * 1024));
            int afterLoad = (int)Math.Clamp(afterStatus.dwMemoryLoad, 1, 100);

            long bytesFreed = 0;
            if (afterAvail > beforeAvail)
            {
                bytesFreed = (long)(afterAvail - beforeAvail);
            }
            else
            {
                bytesFreed = 0;
            }

            string formatted = FormatBytes(bytesFreed);

            return new MemoryPurgeResult
            {
                BytesFreed = bytesFreed,
                FormattedFreed = formatted,
                UsedRamBeforeGb = Math.Round(beforeUsedGb, 1),
                UsedRamAfterGb = Math.Round(afterUsedGb, 1),
                LoadPercentBefore = beforeLoad,
                LoadPercentAfter = afterLoad,
                ProcessesTrimmedCount = trimmedCount
            };
        }, cancellationToken);
    }

    private static void ExecuteSystemMemoryCommand(int command)
    {
        try
        {
            IntPtr pCmd = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                Marshal.WriteInt32(pCmd, command);
                NtSetSystemInformation(SystemMemoryListInformation, pCmd, sizeof(int));
            }
            finally
            {
                Marshal.FreeHGlobal(pCmd);
            }
        }
        catch { }
    }

    private static void EnablePrivilege(string privilegeName)
    {
        try
        {
            if (OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr hToken))
            {
                try
                {
                    if (LookupPrivilegeValue(null, privilegeName, out LUID luid))
                    {
                        var tp = new TOKEN_PRIVILEGES
                        {
                            PrivilegeCount = 1,
                            Luid = luid,
                            Attributes = SE_PRIVILEGE_ENABLED
                        };
                        AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                    }
                }
                finally
                {
                    CloseHandle(hToken);
                }
            }
        }
        catch { }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 MB";
        double mb = bytes / (1024.0 * 1024.0);
        if (mb >= 1024)
        {
            double gb = mb / 1024.0;
            return $"{gb:F2} GB";
        }
        return $"{mb:N0} MB";
    }
}
