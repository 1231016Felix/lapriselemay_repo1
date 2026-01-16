using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuickLauncher.Services;

/// <summary>
/// Service d'extraction des icônes natives des fichiers et applications.
/// Utilise les APIs Windows Shell pour extraire les icônes réelles.
/// </summary>
public static class IconExtractorService
{
    #region Native APIs

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetStockIconInfo(
        uint siid,
        uint uFlags,
        ref SHSTOCKICONINFO psii);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, uint nIconIndex);
    
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        out IShellItem ppv);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHSTOCKICONINFO
    {
        public uint cbSize;
        public IntPtr hIcon;
        public int iSysImageIndex;
        public int iIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szPath;
    }

    // Interfaces COM pour IShellItem et IShellItemImageFactory
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid bhid, 
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;

        public SIZE(int cx, int cy)
        {
            this.cx = cx;
            this.cy = cy;
        }
    }

    [Flags]
    private enum SIIGBF
    {
        SIIGBF_RESIZETOFIT = 0x00000000,
        SIIGBF_BIGGERSIZEOK = 0x00000001,
        SIIGBF_MEMORYONLY = 0x00000002,
        SIIGBF_ICONONLY = 0x00000004,
        SIIGBF_THUMBNAILONLY = 0x00000008,
        SIIGBF_INCACHEONLY = 0x00000010
    }

    private static readonly Guid IID_IShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    private static readonly Guid IID_IShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    // Flags pour SHGetFileInfo
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint SHGFI_TYPENAME = 0x000000400;

    // Flags pour SHGetStockIconInfo
    private const uint SHGSI_ICON = 0x000000100;
    private const uint SHGSI_LARGEICON = 0x000000000;
    private const uint SHGSI_SMALLICON = 0x000000001;

    // Stock icons IDs
    private const uint SIID_FOLDER = 3;
    private const uint SIID_DOCNOASSOC = 0;
    private const uint SIID_APPLICATION = 2;
    private const uint SIID_FIND = 22;
    private const uint SIID_HELP = 23;
    private const uint SIID_SETTINGS = 55;

    #endregion

    #region Cache

    private static readonly ConcurrentDictionary<string, ImageSource?> _iconCache = new();
    private static readonly ConcurrentDictionary<string, ImageSource?> _extensionCache = new();
    
    private const int MaxCacheSize = 500;

    #endregion

    #region Public Methods

    /// <summary>
    /// Extrait l'icône d'un fichier, dossier ou application Store.
    /// </summary>
    /// <param name="path">Chemin du fichier/dossier ou AppUserModelId pour les apps Store</param>
    /// <param name="largeIcon">True pour une grande icône (32x32), false pour petite (16x16)</param>
    /// <returns>ImageSource de l'icône ou null si échec</returns>
    public static ImageSource? GetIcon(string path, bool largeIcon = true)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        var cacheKey = $"{path}_{(largeIcon ? "L" : "S")}";
        
        // Vérifier le cache
        if (_iconCache.TryGetValue(cacheKey, out var cachedIcon))
            return cachedIcon;

        // Nettoyage du cache si trop grand
        if (_iconCache.Count > MaxCacheSize)
        {
            _iconCache.Clear();
        }

        ImageSource? icon = null;

        try
        {
            // Détecter si c'est un AppUserModelId (app Store/UWP)
            if (IsAppUserModelId(path))
            {
                icon = ExtractIconFromAppUserModelId(path, largeIcon);
            }
            // Pour les fichiers .lnk, essayer de résoudre la cible
            else if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var shortcutInfo = ShortcutHelper.ResolveShortcut(path);
                if (shortcutInfo != null && !string.IsNullOrEmpty(shortcutInfo.TargetPath))
                {
                    // D'abord essayer l'icône du raccourci lui-même
                    icon = ExtractIconFromPath(path, largeIcon);
                    
                    // Si pas d'icône, essayer la cible
                    if (icon == null && File.Exists(shortcutInfo.TargetPath))
                    {
                        icon = ExtractIconFromPath(shortcutInfo.TargetPath, largeIcon);
                    }
                }
            }
            
            // Extraction normale pour les fichiers
            icon ??= ExtractIconFromPath(path, largeIcon);
        }
        catch
        {
            // Fallback silencieux
        }

        _iconCache.TryAdd(cacheKey, icon);
        return icon;
    }
    
    /// <summary>
    /// Détecte si le chemin est un AppUserModelId (format des apps Store/UWP).
    /// </summary>
    private static bool IsAppUserModelId(string path)
    {
        // Les AppUserModelId contiennent généralement '!' et '_' mais pas de chemin de fichier
        // Exemples: "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"
        //           "Microsoft.MicrosoftEdge.Stable_8wekyb3d8bbwe!App"
        if (string.IsNullOrEmpty(path))
            return false;
            
        // Si c'est un chemin de fichier, ce n'est pas un AppUserModelId
        if (path.Contains(':') || path.StartsWith("\\\\") || path.StartsWith("/"))
            return false;
            
        // Les AppUserModelId contiennent typiquement '!' pour séparer le package de l'app
        // ou ont le format PackageFamilyName sans extension de fichier
        return path.Contains('!') || 
               (path.Contains('_') && !Path.HasExtension(path) && !File.Exists(path) && !Directory.Exists(path));
    }
    
    /// <summary>
    /// Extrait l'icône d'une application via son AppUserModelId.
    /// </summary>
    private static ImageSource? ExtractIconFromAppUserModelId(string appUserModelId, bool largeIcon)
    {
        try
        {
            // Construire le chemin shell:AppsFolder\AppUserModelId
            var shellPath = $"shell:AppsFolder\\{appUserModelId}";
            
            // Créer un IShellItem pour l'application
            SHCreateItemFromParsingName(shellPath, IntPtr.Zero, IID_IShellItem, out var shellItem);
            
            if (shellItem == null)
                return null;
                
            try
            {
                // Obtenir IShellItemImageFactory
                var imageFactory = (IShellItemImageFactory)shellItem;
                
                // Taille de l'icône
                var size = new SIZE(largeIcon ? 32 : 16, largeIcon ? 32 : 16);
                
                // Obtenir le bitmap
                var hr = imageFactory.GetImage(size, SIIGBF.SIIGBF_ICONONLY, out var hBitmap);
                
                if (hr != 0 || hBitmap == IntPtr.Zero)
                    return null;
                    
                try
                {
                    // Convertir HBITMAP en ImageSource
                    var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    
                    bitmapSource.Freeze();
                    return bitmapSource;
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(shellItem);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Obtient l'icône par défaut pour une extension de fichier.
    /// </summary>
    public static ImageSource? GetIconForExtension(string extension, bool largeIcon = true)
    {
        if (string.IsNullOrEmpty(extension))
            return null;

        if (!extension.StartsWith('.'))
            extension = "." + extension;

        var cacheKey = $"{extension}_{(largeIcon ? "L" : "S")}";
        
        if (_extensionCache.TryGetValue(cacheKey, out var cachedIcon))
            return cachedIcon;

        // Créer un fichier fictif pour obtenir l'icône de l'extension
        var tempFileName = $"temp{extension}";
        
        var shinfo = new SHFILEINFO();
        var flags = SHGFI_ICON | SHGFI_USEFILEATTRIBUTES | (largeIcon ? SHGFI_LARGEICON : SHGFI_SMALLICON);
        
        var result = SHGetFileInfo(tempFileName, 0x80, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);
        
        ImageSource? icon = null;
        
        if (result != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
        {
            icon = ConvertIconToImageSource(shinfo.hIcon);
            DestroyIcon(shinfo.hIcon);
        }

        _extensionCache.TryAdd(cacheKey, icon);
        return icon;
    }

    /// <summary>
    /// Obtient une icône stock Windows.
    /// </summary>
    public static ImageSource? GetStockIcon(StockIconType type, bool largeIcon = true)
    {
        var siid = type switch
        {
            StockIconType.Folder => SIID_FOLDER,
            StockIconType.Document => SIID_DOCNOASSOC,
            StockIconType.Application => SIID_APPLICATION,
            StockIconType.Search => SIID_FIND,
            StockIconType.Help => SIID_HELP,
            StockIconType.Settings => SIID_SETTINGS,
            _ => SIID_DOCNOASSOC
        };

        var sii = new SHSTOCKICONINFO { cbSize = (uint)Marshal.SizeOf<SHSTOCKICONINFO>() };
        var flags = SHGSI_ICON | (largeIcon ? SHGSI_LARGEICON : SHGSI_SMALLICON);
        
        if (SHGetStockIconInfo(siid, flags, ref sii) == 0 && sii.hIcon != IntPtr.Zero)
        {
            var icon = ConvertIconToImageSource(sii.hIcon);
            DestroyIcon(sii.hIcon);
            return icon;
        }

        return null;
    }

    /// <summary>
    /// Vide le cache d'icônes.
    /// </summary>
    public static void ClearCache()
    {
        _iconCache.Clear();
        _extensionCache.Clear();
    }

    /// <summary>
    /// Nombre d'icônes en cache.
    /// </summary>
    public static int CacheCount => _iconCache.Count + _extensionCache.Count;

    #endregion

    #region Private Methods

    private static ImageSource? ExtractIconFromPath(string path, bool largeIcon)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return null;

        var shinfo = new SHFILEINFO();
        var flags = SHGFI_ICON | (largeIcon ? SHGFI_LARGEICON : SHGFI_SMALLICON);
        
        var result = SHGetFileInfo(path, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);
        
        if (result == IntPtr.Zero || shinfo.hIcon == IntPtr.Zero)
            return null;

        try
        {
            return ConvertIconToImageSource(shinfo.hIcon);
        }
        finally
        {
            DestroyIcon(shinfo.hIcon);
        }
    }

    private static ImageSource? ConvertIconToImageSource(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero)
            return null;

        try
        {
            var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            
            // Freezer pour permettre l'utilisation cross-thread
            bitmapSource.Freeze();
            return bitmapSource;
        }
        catch
        {
            return null;
        }
    }

    #endregion
}

/// <summary>
/// Types d'icônes stock Windows.
/// </summary>
public enum StockIconType
{
    Folder,
    Document,
    Application,
    Search,
    Help,
    Settings
}
