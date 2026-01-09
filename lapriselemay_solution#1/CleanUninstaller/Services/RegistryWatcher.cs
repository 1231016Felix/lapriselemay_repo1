using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace CleanUninstaller.Services;

/// <summary>
/// Surveille une clé de registre pour les modifications en temps réel
/// utilisant l'API Windows RegNotifyChangeKeyValue
/// </summary>
public sealed partial class RegistryWatcher : IDisposable
{
    private readonly RegistryKey _rootKey;
    private readonly string _subPath;
    private readonly SafeRegistryHandle? _keyHandle;
    private readonly AutoResetEvent _notifyEvent;
    private readonly Thread? _watchThread;
    private volatile bool _isDisposed;
    private volatile bool _isRunning;

    public event EventHandler<RegistryChangeEventArgs>? Changed;

    #region P/Invoke

    [LibraryImport("advapi32.dll", SetLastError = true)]
    private static partial int RegNotifyChangeKeyValue(
        SafeRegistryHandle hKey,
        [MarshalAs(UnmanagedType.Bool)] bool bWatchSubtree,
        uint dwNotifyFilter,
        SafeWaitHandle hEvent,
        [MarshalAs(UnmanagedType.Bool)] bool fAsynchronous);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegOpenKeyExW(
        SafeRegistryHandle hKey,
        string lpSubKey,
        uint ulOptions,
        uint samDesired,
        out SafeRegistryHandle phkResult);

    private const uint KEY_READ = 0x20019;
    private const uint KEY_NOTIFY = 0x0010;

    private const uint REG_NOTIFY_CHANGE_NAME = 0x00000001;
    private const uint REG_NOTIFY_CHANGE_ATTRIBUTES = 0x00000002;
    private const uint REG_NOTIFY_CHANGE_LAST_SET = 0x00000004;
    private const uint REG_NOTIFY_CHANGE_SECURITY = 0x00000008;

    private const uint REG_LEGAL_CHANGE_FILTER = 
        REG_NOTIFY_CHANGE_NAME | 
        REG_NOTIFY_CHANGE_ATTRIBUTES | 
        REG_NOTIFY_CHANGE_LAST_SET;

    private static readonly SafeRegistryHandle HKEY_LOCAL_MACHINE = 
        new(new IntPtr(unchecked((int)0x80000002)), false);
    private static readonly SafeRegistryHandle HKEY_CURRENT_USER = 
        new(new IntPtr(unchecked((int)0x80000001)), false);
    private static readonly SafeRegistryHandle HKEY_CLASSES_ROOT = 
        new(new IntPtr(unchecked((int)0x80000000)), false);
    private static readonly SafeRegistryHandle HKEY_USERS = 
        new(new IntPtr(unchecked((int)0x80000003)), false);

    #endregion

    public RegistryWatcher(RegistryKey rootKey, string subPath)
    {
        _rootKey = rootKey;
        _subPath = subPath;
        _notifyEvent = new AutoResetEvent(false);

        // Obtenir le handle de la clé racine
        var rootHandle = GetRootHandle(rootKey);
        if (rootHandle == null)
        {
            throw new ArgumentException($"Clé racine non supportée: {rootKey.Name}");
        }

        // Ouvrir la clé avec les permissions de notification
        var result = RegOpenKeyExW(
            rootHandle,
            subPath,
            0,
            KEY_READ | KEY_NOTIFY,
            out var keyHandle);

        if (result != 0)
        {
            throw new InvalidOperationException(
                $"Impossible d'ouvrir la clé {rootKey.Name}\\{subPath}. Code erreur: {result}");
        }

        _keyHandle = keyHandle;

        _watchThread = new Thread(WatchRegistryKey)
        {
            IsBackground = true,
            Name = $"RegWatch_{subPath.Split('\\').LastOrDefault()}"
        };
    }

    private static SafeRegistryHandle? GetRootHandle(RegistryKey root)
    {
        if (root == Registry.LocalMachine) return HKEY_LOCAL_MACHINE;
        if (root == Registry.CurrentUser) return HKEY_CURRENT_USER;
        if (root == Registry.ClassesRoot) return HKEY_CLASSES_ROOT;
        if (root == Registry.Users) return HKEY_USERS;
        return null;
    }

    public void Start()
    {
        if (_isRunning || _isDisposed) return;

        _isRunning = true;
        _watchThread?.Start();
    }

    public void Stop()
    {
        if (!_isRunning) return;

        _isRunning = false;
        _notifyEvent.Set(); // Débloquer le thread en attente
    }

    private void WatchRegistryKey()
    {
        while (_isRunning && !_isDisposed)
        {
            try
            {
                if (_keyHandle == null || _keyHandle.IsClosed) break;

                // Configurer la notification
                var result = RegNotifyChangeKeyValue(
                    _keyHandle,
                    bWatchSubtree: true,
                    REG_LEGAL_CHANGE_FILTER,
                    _notifyEvent.SafeWaitHandle,
                    fAsynchronous: true);

                if (result != 0)
                {
                    // Erreur, arrêter la surveillance
                    break;
                }

                // Attendre la notification ou l'arrêt
                if (_notifyEvent.WaitOne(1000))
                {
                    // Notification reçue
                    if (_isRunning && !_isDisposed)
                    {
                        OnChanged();
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception)
            {
                // Continuer malgré les erreurs
                Thread.Sleep(100);
            }
        }
    }

    private void OnChanged()
    {
        Changed?.Invoke(this, new RegistryChangeEventArgs
        {
            Root = _rootKey,
            SubPath = _subPath
        });
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        _isDisposed = true;
        _isRunning = false;

        _notifyEvent.Set();
        _watchThread?.Join(1000);

        _keyHandle?.Dispose();
        _notifyEvent.Dispose();

        GC.SuppressFinalize(this);
    }
}
