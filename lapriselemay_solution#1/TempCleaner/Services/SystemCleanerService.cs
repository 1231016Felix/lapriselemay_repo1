using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace TempCleaner.Services;

/// <summary>
/// Service pour les opérations de nettoyage système avancées
/// Équivalent C# du SystemCleaner C++ de PerfMonitorQt
/// </summary>
public class SystemCleanerService
{
    #region Win32 API Imports

    // Shell32 - Corbeille
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHAddToRecentDocs(uint uFlags, IntPtr pv);

    // User32 - Presse-papiers
    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    // Kernel32 - Fichiers
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool DeleteFile(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint SetFileAttributes(string lpFileName, uint dwFileAttributes);

    // Kernel32/PSApi - Mémoire
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

    // NtDll - Purge Standby List (nécessite admin)
    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtSetSystemInformation(int InfoClass, ref int Info, int Length);

    // Advapi32 - Privilèges
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
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID_AND_ATTRIBUTES Privileges;
    }

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

    /// <summary>
    /// Résultat détaillé d'une purge mémoire
    /// </summary>
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

    // SHEmptyRecycleBin flags
    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;

    // SHAddToRecentDocs flags
    private const uint SHARD_PIDL = 0x00000001;

    // File attributes
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    // Process access rights
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_SET_QUOTA = 0x0100;

    // Token access rights
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;

    // Privilege constants
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    private const string SE_INCREASE_QUOTA_NAME = "SeIncreaseQuotaPrivilege";
    private const string SE_PROF_SINGLE_PROCESS_NAME = "SeProfileSingleProcessPrivilege";

    // NtSetSystemInformation - Memory purge commands
    private const int SystemMemoryListInformation = 80;
    private const int MemoryEmptyWorkingSets = 2;
    private const int MemoryFlushModifiedList = 3;
    private const int MemoryPurgeStandbyList = 4;
    private const int MemoryPurgeLowPriorityStandbyList = 5;

    #endregion

    #region Memory Operations

    /// <summary>
    /// Obtenir les statistiques mémoire système
    /// </summary>
    public (long total, long available, double loadPercent) GetMemoryInfo()
    {
        var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref memStatus))
        {
            return ((long)memStatus.ullTotalPhys, (long)memStatus.ullAvailPhys, memStatus.dwMemoryLoad);
        }
        return (0, 0, 0);
    }

    /// <summary>
    /// Activer un privilège Windows (nécessaire pour certaines opérations mémoire)
    /// </summary>
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
                    Privileges = new LUID_AND_ATTRIBUTES
                    {
                        Luid = luid,
                        Attributes = SE_PRIVILEGE_ENABLED
                    }
                };

                return AdjustTokenPrivileges(tokenHandle, false, ref tokenPrivileges, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                CloseHandle(tokenHandle);
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Purger la mémoire de travail de tous les processus (équivalent EmptyWorkingSet du C++)
    /// </summary>
    public async Task<MemoryPurgeResult> PurgeWorkingSetAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                // Statistiques avant
                var (totalMem, availBefore, loadBefore) = GetMemoryInfo();

                // Activer le privilège nécessaire
                EnablePrivilege(SE_INCREASE_QUOTA_NAME);

                int processCount = 0;
                int successCount = 0;

                // Purger tous les processus accessibles
                foreach (var process in Process.GetProcesses())
                {
                    try
                    {
                        IntPtr handle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_SET_QUOTA, false, process.Id);
                        if (handle != IntPtr.Zero)
                        {
                            try
                            {
                                if (EmptyWorkingSet(handle))
                                    successCount++;
                                processCount++;
                            }
                            finally
                            {
                                CloseHandle(handle);
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        process.Dispose();
                    }
                }

                // Forcer le GC
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                // Statistiques après
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
                return new MemoryPurgeResult
                {
                    Success = false,
                    Message = $"Erreur: {ex.Message}"
                };
            }
        });
    }

    /// <summary>
    /// Purger le Standby List (mémoire en cache) - Nécessite admin
    /// Équivalent de RAMMap "Empty Standby List"
    /// </summary>
    public async Task<MemoryPurgeResult> PurgeStandbyListAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!AdminService.IsRunningAsAdmin())
                    return new MemoryPurgeResult { Success = false, Message = "Droits administrateur requis" };

                // Statistiques avant
                var (totalMem, availBefore, loadBefore) = GetMemoryInfo();

                // Activer le privilège SeProfileSingleProcessPrivilege
                if (!EnablePrivilege(SE_PROF_SINGLE_PROCESS_NAME))
                    return new MemoryPurgeResult { Success = false, Message = "Impossible d'activer le privilège requis" };

                // Purger le Standby List via NtSetSystemInformation
                int command = MemoryPurgeStandbyList;
                int result = NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int));

                // Statistiques après
                var (_, availAfter, loadAfter) = GetMemoryInfo();

                if (result == 0) // STATUS_SUCCESS
                {
                    return new MemoryPurgeResult
                    {
                        Success = true,
                        Message = "Standby List purgé",
                        TotalMemory = totalMem,
                        AvailableBefore = availBefore,
                        AvailableAfter = availAfter,
                        MemoryLoadBefore = loadBefore,
                        MemoryLoadAfter = loadAfter
                    };
                }

                return new MemoryPurgeResult { Success = false, Message = $"Erreur: 0x{result:X8}" };
            }
            catch (Exception ex)
            {
                return new MemoryPurgeResult { Success = false, Message = $"Erreur: {ex.Message}" };
            }
        });
    }

    /// <summary>
    /// Purger toute la mémoire (Working Set + Standby List)
    /// </summary>
    public async Task<MemoryPurgeResult> PurgeAllMemoryAsync()
    {
        // Statistiques initiales
        var (totalMem, availBefore, loadBefore) = GetMemoryInfo();

        // 1. Purger Working Set
        var wsResult = await PurgeWorkingSetAsync();

        // 2. Purger Standby List (si admin)
        MemoryPurgeResult? sbResult = null;
        if (AdminService.IsRunningAsAdmin())
        {
            sbResult = await PurgeStandbyListAsync();
        }

        // Statistiques finales
        var (_, availAfter, loadAfter) = GetMemoryInfo();

        var messages = new List<string> { wsResult.Message };
        if (sbResult != null)
            messages.Add(sbResult.Message);
        else
            messages.Add("Standby List: droits admin requis");

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

    /// <summary>
    /// Purger le Modified Page List (pages modifiées en attente d'écriture) - Nécessite admin
    /// </summary>
    public async Task<MemoryPurgeResult> PurgeModifiedPageListAsync()
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

                int command = MemoryFlushModifiedList;
                int result = NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int));

                var (_, availAfter, loadAfter) = GetMemoryInfo();

                if (result == 0)
                {
                    return new MemoryPurgeResult
                    {
                        Success = true,
                        Message = "Modified Page List purgé",
                        TotalMemory = totalMem,
                        AvailableBefore = availBefore,
                        AvailableAfter = availAfter,
                        MemoryLoadBefore = loadBefore,
                        MemoryLoadAfter = loadAfter
                    };
                }

                return new MemoryPurgeResult { Success = false, Message = $"Erreur: 0x{result:X8}" };
            }
            catch (Exception ex)
            {
                return new MemoryPurgeResult { Success = false, Message = $"Erreur: {ex.Message}" };
            }
        });
    }

    /// <summary>
    /// Purger le Low-Priority Standby List - Nécessite admin
    /// </summary>
    public async Task<MemoryPurgeResult> PurgeLowPriorityStandbyListAsync()
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

                int command = MemoryPurgeLowPriorityStandbyList;
                int result = NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int));

                var (_, availAfter, loadAfter) = GetMemoryInfo();

                if (result == 0)
                {
                    return new MemoryPurgeResult
                    {
                        Success = true,
                        Message = "Low-Priority Standby List purgé",
                        TotalMemory = totalMem,
                        AvailableBefore = availBefore,
                        AvailableAfter = availAfter,
                        MemoryLoadBefore = loadBefore,
                        MemoryLoadAfter = loadAfter
                    };
                }

                return new MemoryPurgeResult { Success = false, Message = $"Erreur: 0x{result:X8}" };
            }
            catch (Exception ex)
            {
                return new MemoryPurgeResult { Success = false, Message = $"Erreur: {ex.Message}" };
            }
        });
    }

    /// <summary>
    /// Purge MAXIMALE de la mémoire (tout: Working Set + Modified + Low-Priority + Standby)
    /// </summary>
    public async Task<MemoryPurgeResult> PurgeMaxMemoryAsync()
    {
        var (totalMem, availBefore, loadBefore) = GetMemoryInfo();
        int totalProcesses = 0, successProcesses = 0;
        var messages = new List<string>();

        // 1. Purger Working Set
        var wsResult = await PurgeWorkingSetAsync();
        messages.Add(wsResult.Message);
        totalProcesses = wsResult.ProcessCount;
        successProcesses = wsResult.SuccessCount;

        if (AdminService.IsRunningAsAdmin())
        {
            // 2. Purger Modified Page List
            var mpResult = await PurgeModifiedPageListAsync();
            messages.Add(mpResult.Success ? "Modified ✓" : "Modified ✗");

            // 3. Purger Low-Priority Standby
            var lpResult = await PurgeLowPriorityStandbyListAsync();
            messages.Add(lpResult.Success ? "LowPriority ✓" : "LowPriority ✗");

            // 4. Purger Standby List (le plus agressif)
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
            ProcessCount = totalProcesses,
            SuccessCount = successProcesses,
            MemoryLoadBefore = loadBefore,
            MemoryLoadAfter = loadAfter
        };
    }

    #endregion

    #region System Cleanup Commands

    /// <summary>
    /// Nettoyer les composants Windows obsolètes (WinSxS) - Nécessite admin
    /// </summary>
    public async Task<(bool success, string message)> CleanupWinSxSAsync(IProgress<string>? progress = null)
    {
        if (!AdminService.IsRunningAsAdmin())
            return (false, "Droits administrateur requis");

        return await Task.Run(async () =>
        {
            try
            {
                progress?.Report("Analyse des composants Windows...");

                // Utiliser DISM pour le nettoyage
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "dism.exe",
                        Arguments = "/Online /Cleanup-Image /StartComponentCleanup /ResetBase",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        Verb = "runas"
                    }
                };

                process.Start();
                
                // Lire la sortie de manière asynchrone
                var outputTask = process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                var output = await outputTask;

                if (process.ExitCode == 0)
                    return (true, "Nettoyage WinSxS terminé avec succès");

                return (false, $"Erreur DISM (code: {process.ExitCode})");
            }
            catch (Exception ex)
            {
                return (false, $"Erreur: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Désactiver l'hibernation (supprime hiberfil.sys) - Nécessite admin
    /// </summary>
    public async Task<(bool success, string message, long freedBytes)> DisableHibernationAsync()
    {
        if (!AdminService.IsRunningAsAdmin())
            return (false, "Droits administrateur requis", 0);

        return await Task.Run(() =>
        {
            try
            {
                // Vérifier si hiberfil.sys existe et sa taille
                var hiberFile = @"C:\hiberfil.sys";
                long fileSize = 0;

                if (File.Exists(hiberFile))
                {
                    try
                    {
                        var fileInfo = new FileInfo(hiberFile);
                        fileSize = fileInfo.Length;
                    }
                    catch { }
                }

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powercfg.exe",
                        Arguments = "/hibernate off",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                process.WaitForExit(10000);

                if (process.ExitCode == 0)
                    return (true, $"Hibernation désactivée ({FormatSize(fileSize)} libérés)", fileSize);

                return (false, "Erreur lors de la désactivation", 0);
            }
            catch (Exception ex)
            {
                return (false, $"Erreur: {ex.Message}", 0);
            }
        });
    }

    /// <summary>
    /// Réactiver l'hibernation - Nécessite admin
    /// </summary>
    public async Task<(bool success, string message)> EnableHibernationAsync()
    {
        if (!AdminService.IsRunningAsAdmin())
            return (false, "Droits administrateur requis");

        return await Task.Run(() =>
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powercfg.exe",
                        Arguments = "/hibernate on",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                process.WaitForExit(10000);

                return process.ExitCode == 0
                    ? (true, "Hibernation réactivée")
                    : (false, "Erreur lors de la réactivation");
            }
            catch (Exception ex)
            {
                return (false, $"Erreur: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Vérifier si l'hibernation est activée
    /// </summary>
    public bool IsHibernationEnabled()
    {
        return File.Exists(@"C:\hiberfil.sys");
    }

    /// <summary>
    /// Nettoyer le cache du Windows Store - Nécessite admin
    /// </summary>
    public async Task<(bool success, string message)> ClearWindowsStoreCacheAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "wsreset.exe",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                process.WaitForExit(30000);

                return (true, "Cache du Windows Store vidé");
            }
            catch (Exception ex)
            {
                return (false, $"Erreur: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Exécuter le nettoyage de disque Windows (cleanmgr) - Mode automatique
    /// </summary>
    public async Task<(bool success, string message)> RunDiskCleanupAsync()
    {
        if (!AdminService.IsRunningAsAdmin())
            return (false, "Droits administrateur requis pour le nettoyage complet");

        return await Task.Run(() =>
        {
            try
            {
                // Configurer les options de nettoyage dans le registre
                // puis lancer cleanmgr en mode automatique
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "cleanmgr.exe",
                        Arguments = "/d C: /VERYLOWDISK",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                process.WaitForExit(300000); // 5 minutes max

                return (true, "Nettoyage de disque terminé");
            }
            catch (Exception ex)
            {
                return (false, $"Erreur: {ex.Message}");
            }
        });
    }

    #endregion

    #region Recycle Bin Operations

    /// <summary>
    /// Obtenir les statistiques de la corbeille
    /// </summary>
    public (long size, int count) GetRecycleBinInfo()
    {
        var info = new SHQUERYRBINFO { cbSize = (uint)Marshal.SizeOf<SHQUERYRBINFO>() };
        int result = SHQueryRecycleBin(null, ref info);
        return result == 0 ? (info.i64Size, (int)info.i64NumItems) : (0, 0);
    }

    /// <summary>
    /// Vider la corbeille système
    /// </summary>
    public async Task<(bool success, string message)> EmptyRecycleBinAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var (size, count) = GetRecycleBinInfo();
                if (count == 0)
                    return (true, "La corbeille est déjà vide");

                int result = SHEmptyRecycleBin(IntPtr.Zero, null,
                    SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);

                return result is 0 or 1
                    ? (true, $"Corbeille vidée: {count} éléments ({FormatSize(size)})")
                    : (false, $"Erreur: code {result}");
            }
            catch (Exception ex)
            {
                return (false, $"Erreur: {ex.Message}");
            }
        });
    }

    #endregion

    #region DNS Cache Operations

    /// <summary>
    /// Vider le cache DNS
    /// </summary>
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
                        FileName = "ipconfig",
                        Arguments = "/flushdns",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                process.WaitForExit(5000);

                return process.ExitCode == 0
                    ? (true, "Cache DNS vidé")
                    : (false, "Erreur lors du vidage du cache DNS");
            }
            catch (Exception ex)
            {
                return (false, $"Erreur: {ex.Message}");
            }
        });
    }

    #endregion

    #region Clipboard Operations

    /// <summary>
    /// Vider le presse-papiers
    /// </summary>
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

            // Fallback WPF
            Application.Current?.Dispatcher.Invoke(() => System.Windows.Clipboard.Clear());
            return (true, "Presse-papiers vidé");
        }
        catch (Exception ex)
        {
            return (false, $"Erreur: {ex.Message}");
        }
    }

    #endregion

    #region Recent Documents Operations

    /// <summary>
    /// Effacer la liste des documents récents
    /// </summary>
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

                // Appeler l'API Windows
                SHAddToRecentDocs(SHARD_PIDL, IntPtr.Zero);

                // Supprimer les fichiers physiquement
                foreach (var path in recentPaths)
                {
                    if (!Directory.Exists(path)) continue;

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
            catch (Exception ex)
            {
                return (false, $"Erreur: {ex.Message}", 0);
            }
        });
    }

    #endregion

    #region File Deletion Operations

    /// <summary>
    /// Suppression forcée d'un fichier (retire attributs read-only/system)
    /// </summary>
    public bool ForceDeleteFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return true;

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
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Suppression forcée d'un dossier vide
    /// </summary>
    public bool ForceDeleteEmptyDirectory(string dirPath)
    {
        try
        {
            if (!Directory.Exists(dirPath))
                return true;

            var dir = new DirectoryInfo(dirPath);
            if (dir.GetFileSystemInfos().Length == 0)
            {
                dir.Delete();
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Supprime les dossiers vides récursivement
    /// </summary>
    public int DeleteEmptyDirectories(string basePath)
    {
        if (!Directory.Exists(basePath))
            return 0;

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

    #region Utility Methods

    private static string FormatSize(long bytes)
    {
        if (bytes == 0) return "0 B";
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int i = 0;
        double size = bytes;

        while (size >= 1024 && i < suffixes.Length - 1)
        {
            size /= 1024;
            i++;
        }

        return $"{size:N2} {suffixes[i]}";
    }

    #endregion
}
