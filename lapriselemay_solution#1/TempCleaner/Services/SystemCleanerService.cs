using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using TempCleaner.Helpers;

namespace TempCleaner.Services;

/// <summary>
/// Service pour les opérations de nettoyage système avancées
/// </summary>
public class SystemCleanerService
{
    #region Win32 API Imports

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHAddToRecentDocs(uint uFlags, IntPtr pv);

    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool DeleteFile(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint SetFileAttributes(string lpFileName, uint dwFileAttributes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtSetSystemInformation(int InfoClass, ref int Info, int Length);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState, int BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

    #endregion

    #region Structures

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct SHQUERYRBINFO
    {
        public uint cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES { public LUID Luid; public uint Attributes; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID_AND_ATTRIBUTES Privileges; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    #endregion

    #region Result Classes

    public class MemoryPurgeResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public long TotalMemory { get; init; }
        public long AvailableBefore { get; init; }
        public long AvailableAfter { get; init; }
        public long UsedBefore => TotalMemory - AvailableBefore;
        public long UsedAfter => TotalMemory - AvailableAfter;
        public long Freed => AvailableAfter - AvailableBefore;
        public int ProcessCount { get; init; }
        public int SuccessCount { get; init; }
        public double MemoryLoadBefore { get; init; }
        public double MemoryLoadAfter { get; init; }
    }

    #endregion

    #region Constants

    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;
    private const uint SHARD_PIDL = 0x00000001;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_SET_QUOTA = 0x0100;
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    private const string SE_INCREASE_QUOTA_NAME = "SeIncreaseQuotaPrivilege";
    private const string SE_PROF_SINGLE_PROCESS_NAME = "SeProfileSingleProcessPrivilege";
    private const int SystemMemoryListInformation = 80;
    private const int MemoryFlushModifiedList = 3;
    private const int MemoryPurgeStandbyList = 4;
    private const int MemoryPurgeLowPriorityStandbyList = 5;

    #endregion


    #region Memory Operations

    public (long total, long available, double loadPercent) GetMemoryInfo()
    {
        var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref memStatus) 
            ? ((long)memStatus.ullTotalPhys, (long)memStatus.ullAvailPhys, memStatus.dwMemoryLoad) 
            : (0, 0, 0);
    }

    private static bool EnablePrivilege(string privilegeName)
    {
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr tokenHandle))
                return false;

            try
            {
                if (!LookupPrivilegeValue(null, privilegeName, out LUID luid))
                    return false;

                var tokenPrivileges = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privileges = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = SE_PRIVILEGE_ENABLED }
                };

                return AdjustTokenPrivileges(tokenHandle, false, ref tokenPrivileges, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally { CloseHandle(tokenHandle); }
        }
        catch { return false; }
    }

    public async Task<MemoryPurgeResult> PurgeWorkingSetAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var (totalMem, availBefore, loadBefore) = GetMemoryInfo();
                EnablePrivilege(SE_INCREASE_QUOTA_NAME);

                int processCount = 0, successCount = 0;

                foreach (var process in Process.GetProcesses())
                {
                    try
                    {
                        IntPtr handle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_SET_QUOTA, false, process.Id);
                        if (handle != IntPtr.Zero)
                        {
                            try
                            {
                                if (EmptyWorkingSet(handle)) successCount++;
                                processCount++;
                            }
                            finally { CloseHandle(handle); }
                        }
                    }
                    catch { }
                    finally { process.Dispose(); }
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                var (_, availAfter, loadAfter) = GetMemoryInfo();

                return new MemoryPurgeResult
                {
                    Success = true,
                    Message = $"Working Set purgé: {successCount}/{processCount} processus",
                    TotalMemory = totalMem,
                    AvailableBefore = availBefore,
                    AvailableAfter = availAfter,
                    ProcessCount = processCount,
                    SuccessCount = successCount,
                    MemoryLoadBefore = loadBefore,
                    MemoryLoadAfter = loadAfter
                };
            }
            catch (Exception ex)
            {
                return new MemoryPurgeResult { Success = false, Message = $"Erreur: {ex.Message}" };
            }
        });
    }

    public async Task<MemoryPurgeResult> PurgeStandbyListAsync()
    {
        return await PurgeMemoryListAsync(MemoryPurgeStandbyList, "Standby List");
    }

    public async Task<MemoryPurgeResult> PurgeModifiedPageListAsync()
    {
        return await PurgeMemoryListAsync(MemoryFlushModifiedList, "Modified Page List");
    }

    public async Task<MemoryPurgeResult> PurgeLowPriorityStandbyListAsync()
    {
        return await PurgeMemoryListAsync(MemoryPurgeLowPriorityStandbyList, "Low-Priority Standby List");
    }

    private async Task<MemoryPurgeResult> PurgeMemoryListAsync(int command, string listName)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!AdminService.IsRunningAsAdmin())
                    return new MemoryPurgeResult { Success = false, Message = "Droits administrateur requis" };

                var (totalMem, availBefore, loadBefore) = GetMemoryInfo();

                if (!EnablePrivilege(SE_PROF_SINGLE_PROCESS_NAME))
                    return new MemoryPurgeResult { Success = false, Message = "Impossible d'activer le privilège requis" };

                int cmd = command;
                int result = NtSetSystemInformation(SystemMemoryListInformation, ref cmd, sizeof(int));

                var (_, availAfter, loadAfter) = GetMemoryInfo();

                return result == 0
                    ? new MemoryPurgeResult
                    {
                        Success = true, Message = $"{listName} purgé", TotalMemory = totalMem,
                        AvailableBefore = availBefore, AvailableAfter = availAfter,
                        MemoryLoadBefore = loadBefore, MemoryLoadAfter = loadAfter
                    }
                    : new MemoryPurgeResult { Success = false, Message = $"Erreur: 0x{result:X8}" };
            }
            catch (Exception ex)
            {
                return new MemoryPurgeResult { Success = false, Message = $"Erreur: {ex.Message}" };
            }
        });
    }

    public async Task<MemoryPurgeResult> PurgeAllMemoryAsync()
    {
        var (totalMem, availBefore, loadBefore) = GetMemoryInfo();
        var wsResult = await PurgeWorkingSetAsync();

        MemoryPurgeResult? sbResult = null;
        if (AdminService.IsRunningAsAdmin())
            sbResult = await PurgeStandbyListAsync();

        var (_, availAfter, loadAfter) = GetMemoryInfo();

        var messages = new List<string> { wsResult.Message };
        messages.Add(sbResult?.Message ?? "Standby List: droits admin requis");

        return new MemoryPurgeResult
        {
            Success = wsResult.Success && (sbResult?.Success ?? true),
            Message = string.Join(" | ", messages),
            TotalMemory = totalMem,
            AvailableBefore = availBefore,
            AvailableAfter = availAfter,
            ProcessCount = wsResult.ProcessCount,
            SuccessCount = wsResult.SuccessCount,
            MemoryLoadBefore = loadBefore,
            MemoryLoadAfter = loadAfter
        };
    }

    public async Task<MemoryPurgeResult> PurgeMaxMemoryAsync()
    {
        var (totalMem, availBefore, loadBefore) = GetMemoryInfo();
        var messages = new List<string>();

        var wsResult = await PurgeWorkingSetAsync();
        messages.Add(wsResult.Message);

        if (AdminService.IsRunningAsAdmin())
        {
            var mpResult = await PurgeModifiedPageListAsync();
            messages.Add(mpResult.Success ? "Modified ✓" : "Modified ✗");

            var lpResult = await PurgeLowPriorityStandbyListAsync();
            messages.Add(lpResult.Success ? "LowPriority ✓" : "LowPriority ✗");

            var sbResult = await PurgeStandbyListAsync();
            messages.Add(sbResult.Success ? "Standby ✓" : "Standby ✗");
        }
        else
        {
            messages.Add("Admin requis pour purge maximale");
        }

        var (_, availAfter, loadAfter) = GetMemoryInfo();

        return new MemoryPurgeResult
        {
            Success = true,
            Message = string.Join(" | ", messages),
            TotalMemory = totalMem,
            AvailableBefore = availBefore,
            AvailableAfter = availAfter,
            ProcessCount = wsResult.ProcessCount,
            SuccessCount = wsResult.SuccessCount,
            MemoryLoadBefore = loadBefore,
            MemoryLoadAfter = loadAfter
        };
    }

    #endregion


    #region System Cleanup Commands

    public async Task<(bool success, string message)> CleanupWinSxSAsync(IProgress<string>? progress = null)
    {
        if (!AdminService.IsRunningAsAdmin())
            return (false, "Droits administrateur requis");

        return await Task.Run(async () =>
        {
            try
            {
                progress?.Report("Analyse des composants Windows...");

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "dism.exe",
                        Arguments = "/Online /Cleanup-Image /StartComponentCleanup /ResetBase",
                        UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                        CreateNoWindow = true, Verb = "runas"
                    }
                };

                process.Start();
                await process.WaitForExitAsync();

                return process.ExitCode == 0
                    ? (true, "Nettoyage WinSxS terminé avec succès")
                    : (false, $"Erreur DISM (code: {process.ExitCode})");
            }
            catch (Exception ex) { return (false, $"Erreur: {ex.Message}"); }
        });
    }

    public async Task<(bool success, string message, long freedBytes)> DisableHibernationAsync()
    {
        if (!AdminService.IsRunningAsAdmin())
            return (false, "Droits administrateur requis", 0);

        return await Task.Run(() =>
        {
            try
            {
                long fileSize = File.Exists(@"C:\hiberfil.sys") 
                    ? new FileInfo(@"C:\hiberfil.sys").Length : 0;

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powercfg.exe", Arguments = "/hibernate off",
                        UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
                    }
                };

                process.Start();
                process.WaitForExit(10000);

                return process.ExitCode == 0
                    ? (true, $"Hibernation désactivée ({FileSizeHelper.Format(fileSize)} libérés)", fileSize)
                    : (false, "Erreur lors de la désactivation", 0L);
            }
            catch (Exception ex) { return (false, $"Erreur: {ex.Message}", 0L); }
        });
    }

    public async Task<(bool success, string message)> EnableHibernationAsync()
    {
        if (!AdminService.IsRunningAsAdmin())
            return (false, "Droits administrateur requis");

        return await Task.Run(() =>
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powercfg.exe", Arguments = "/hibernate on",
                        UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true
                    }
                };

                process.Start();
                process.WaitForExit(10000);

                return process.ExitCode == 0 ? (true, "Hibernation réactivée") : (false, "Erreur lors de la réactivation");
            }
            catch (Exception ex) { return (false, $"Erreur: {ex.Message}"); }
        });
    }

    public bool IsHibernationEnabled() => File.Exists(@"C:\hiberfil.sys");

    public async Task<(bool success, string message)> ClearWindowsStoreCacheAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo { FileName = "wsreset.exe", UseShellExecute = false, CreateNoWindow = true }
                };
                process.Start();
                process.WaitForExit(30000);
                return (true, "Cache du Windows Store vidé");
            }
            catch (Exception ex) { return (false, $"Erreur: {ex.Message}"); }
        });
    }

    public async Task<(bool success, string message)> RunDiskCleanupAsync()
    {
        if (!AdminService.IsRunningAsAdmin())
            return (false, "Droits administrateur requis pour le nettoyage complet");

        return await Task.Run(() =>
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "cleanmgr.exe", Arguments = "/d C: /VERYLOWDISK",
                        UseShellExecute = false, CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit(300000);
                return (true, "Nettoyage de disque terminé");
            }
            catch (Exception ex) { return (false, $"Erreur: {ex.Message}"); }
        });
    }

    #endregion

    #region Recycle Bin Operations

    public (long size, int count) GetRecycleBinInfo()
    {
        var info = new SHQUERYRBINFO { cbSize = (uint)Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? (info.i64Size, (int)info.i64NumItems) : (0, 0);
    }

    public async Task<(bool success, string message)> EmptyRecycleBinAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var (size, count) = GetRecycleBinInfo();
                if (count == 0) return (true, "La corbeille est déjà vide");

                int result = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
                return result is 0 or 1
                    ? (true, $"Corbeille vidée: {count} éléments ({FileSizeHelper.Format(size)})")
                    : (false, $"Erreur: code {result}");
            }
            catch (Exception ex) { return (false, $"Erreur: {ex.Message}"); }
        });
    }

    #endregion


    #region DNS Cache Operations

    public async Task<(bool success, string message)> FlushDnsCacheAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ipconfig", Arguments = "/flushdns",
                        UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
                    }
                };

                process.Start();
                process.WaitForExit(5000);

                return process.ExitCode == 0 
                    ? (true, "Cache DNS vidé") 
                    : (false, "Erreur lors du vidage du cache DNS");
            }
            catch (Exception ex) { return (false, $"Erreur: {ex.Message}"); }
        });
    }

    #endregion

    #region Clipboard Operations

    public (bool success, string message) ClearClipboard()
    {
        try
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                EmptyClipboard();
                CloseClipboard();
                return (true, "Presse-papiers vidé");
            }

            Application.Current?.Dispatcher.Invoke(() => System.Windows.Clipboard.Clear());
            return (true, "Presse-papiers vidé");
        }
        catch (Exception ex) { return (false, $"Erreur: {ex.Message}"); }
    }

    #endregion

    #region Recent Documents Operations

    public async Task<(bool success, string message, int count)> ClearRecentDocumentsAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                int deletedCount = 0;
                var recentPaths = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.Recent),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "Microsoft", "Windows", "Recent", "AutomaticDestinations"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "Microsoft", "Windows", "Recent", "CustomDestinations")
                };

                SHAddToRecentDocs(SHARD_PIDL, IntPtr.Zero);

                foreach (var path in recentPaths.Where(Directory.Exists))
                {
                    foreach (var file in Directory.EnumerateFiles(path))
                    {
                        try
                        {
                            ForceDeleteFile(file);
                            deletedCount++;
                        }
                        catch { }
                    }
                }

                return (true, $"Documents récents effacés ({deletedCount} fichiers)", deletedCount);
            }
            catch (Exception ex) { return (false, $"Erreur: {ex.Message}", 0); }
        });
    }

    #endregion

    #region File Deletion Operations

    public bool ForceDeleteFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return true;

            SetFileAttributes(filePath, FILE_ATTRIBUTE_NORMAL);
            File.SetAttributes(filePath, FileAttributes.Normal);
            File.Delete(filePath);
            return true;
        }
        catch
        {
            try
            {
                SetFileAttributes(filePath, FILE_ATTRIBUTE_NORMAL);
                return DeleteFile(filePath);
            }
            catch { return false; }
        }
    }

    public bool ForceDeleteEmptyDirectory(string dirPath)
    {
        try
        {
            if (!Directory.Exists(dirPath)) return true;

            var dir = new DirectoryInfo(dirPath);
            if (dir.GetFileSystemInfos().Length == 0)
            {
                dir.Delete();
                return true;
            }
            return false;
        }
        catch { return false; }
    }

    public int DeleteEmptyDirectories(string basePath)
    {
        if (!Directory.Exists(basePath)) return 0;

        int deletedCount = 0;
        try
        {
            var directories = Directory.GetDirectories(basePath, "*", SearchOption.AllDirectories)
                .OrderByDescending(d => d.Length);

            foreach (var dir in directories)
            {
                if (ForceDeleteEmptyDirectory(dir))
                    deletedCount++;
            }
        }
        catch { }

        return deletedCount;
    }

    #endregion
}
