using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Management.Deployment;

namespace QuickLauncher.Services;

/// <summary>
/// Service d'extraction des icônes natives des fichiers et applications.
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        out IShellItemImageFactory ppv);

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
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
        public SIZE(int width, int height) { cx = width; cy = height; }
    }

    [Flags]
    private enum SIIGBF : uint
    {
        SIIGBF_RESIZETOFIT = 0x00,
        SIIGBF_BIGGERSIZEOK = 0x01,
        SIIGBF_MEMORYONLY = 0x02,
        SIIGBF_ICONONLY = 0x04,
        SIIGBF_THUMBNAILONLY = 0x08,
        SIIGBF_INCACHEONLY = 0x10
    }

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

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

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_SMALLICON = 0x000000001;

    #endregion

    #region Cache
    private static readonly ConcurrentDictionary<string, ImageSource?> _iconCache = new();
    private const int MaxCacheSize = 500;
    private static bool _persistentCacheInitialized;
    #endregion

    /// <summary>
    /// Initialise le cache persistant. Doit être appelé au démarrage.
    /// </summary>
    public static void InitializePersistentCache()
    {
        if (_persistentCacheInitialized) return;
        _persistentCacheInitialized = true;
        IconCacheService.Initialize();
    }

    public static ImageSource? GetIcon(string path, bool largeIcon = true)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        var cacheKey = $"{path}_{(largeIcon ? "L" : "S")}";
        
        // 1. Cache mémoire rapide
        if (_iconCache.TryGetValue(cacheKey, out var cachedIcon))
        {
            Debug.WriteLine($"[IconExtractor] Memory cache hit for: {path}");
            return cachedIcon;
        }
        
        // 2. Cache persistant sur disque
        var persistentIcon = IconCacheService.TryGetFromCache(path, largeIcon);
        if (persistentIcon != null)
        {
            _iconCache.TryAdd(cacheKey, persistentIcon);
            return persistentIcon;
        }

        if (_iconCache.Count > MaxCacheSize)
            _iconCache.Clear();

        ImageSource? icon = null;
        var size = largeIcon ? 48 : 24;

        try
        {
            Debug.WriteLine($"[IconExtractor] GetIcon called for: {path}");
            
            // 1. D'abord, essayer de résoudre les chemins avec Known Folder GUID
            if (path.StartsWith("{") && path.Contains("}\\"))
            {
                var resolvedPath = ResolveKnownFolderPath(path);
                if (!string.IsNullOrEmpty(resolvedPath))
                {
                    Debug.WriteLine($"[IconExtractor] Resolved Known Folder: {path} -> {resolvedPath}");
                    icon = ExtractIconFromFile(resolvedPath, largeIcon);
                    if (icon != null)
                    {
                        Debug.WriteLine($"[IconExtractor] SUCCESS via Known Folder resolution for: {path}");
                        _iconCache.TryAdd(cacheKey, icon);
                        return icon;
                    }
                }
                // Fallback: essayer via ShellItem même si la résolution a échoué
                Debug.WriteLine($"[IconExtractor] Known Folder resolution failed, trying ShellItem for: {path}");
                icon = ExtractIconFromShellItem(path, size);
                if (icon != null)
                {
                    Debug.WriteLine($"[IconExtractor] SUCCESS via ShellItem for Known Folder path: {path}");
                    _iconCache.TryAdd(cacheKey, icon);
                    return icon;
                }
            }
            
            if (IsAppUserModelId(path))
            {
                Debug.WriteLine($"[IconExtractor] Processing AppUserModelId: {path}");
                
                // Pour les apps UWP (avec !), essayer d'abord les méthodes de package (plus fiables)
                if (path.Contains('!'))
                {
                    // 1. Essayer via PackageManager (le plus fiable pour UWP)
                    icon = ExtractIconFromPackage(path, size);
                    
                    // 2. Fallback: chercher dans les répertoires d'installation
                    if (icon == null)
                    {
                        icon = ExtractIconFromPackageInstallPath(path, size);
                    }
                }
                
                // 3. IShellItemImageFactory (fonctionne pour Win32 et parfois UWP)
                if (icon == null)
                {
                    icon = ExtractIconFromShellItem(path, size);
                }
                
                if (icon != null)
                {
                    Debug.WriteLine($"[IconExtractor] SUCCESS for: {path}");
                }
                else
                {
                    Debug.WriteLine($"[IconExtractor] FAILED - No icon found for: {path}");
                }
            }
            else if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine($"[IconExtractor] Processing .lnk: {path}");
                var shortcutInfo = ShortcutHelper.ResolveShortcut(path);
                if (shortcutInfo != null && !string.IsNullOrEmpty(shortcutInfo.TargetPath) && File.Exists(shortcutInfo.TargetPath))
                {
                    Debug.WriteLine($"[IconExtractor] Shortcut target: {shortcutInfo.TargetPath}");
                    icon = ExtractIconFromFile(shortcutInfo.TargetPath, largeIcon);
                }
                icon ??= ExtractIconFromFile(path, largeIcon);
                Debug.WriteLine($"[IconExtractor] .lnk result: {(icon != null ? "OK" : "NULL")}");
            }
            else if (File.Exists(path) || Directory.Exists(path))
            {
                Debug.WriteLine($"[IconExtractor] Processing file/dir: {path}");
                icon = ExtractIconFromFile(path, largeIcon);
                Debug.WriteLine($"[IconExtractor] File result: {(icon != null ? "OK" : "NULL")}");
            }
            else
            {
                Debug.WriteLine($"[IconExtractor] Path not recognized: {path}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[IconExtractor] Error for '{path}': {ex.Message}");
        }

        _iconCache.TryAdd(cacheKey, icon);
        
        // 3. Sauvegarder dans le cache persistant pour les prochains démarrages
        if (icon != null)
        {
            IconCacheService.SaveToCache(path, icon, largeIcon);
        }
        
        return icon;
    }

    /// <summary>
    /// Dictionnaire des Known Folder GUIDs vers leurs chemins réels
    /// </summary>
    private static readonly Dictionary<string, Environment.SpecialFolder?> KnownFolderGuids = new(StringComparer.OrdinalIgnoreCase)
    {
        { "{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}", null }, // System32 -> use GetFolderPath special
        { "{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}", Environment.SpecialFolder.ProgramFiles },
        { "{D65231B0-B2F1-4857-A4CE-A8E7C6EA7D27}", Environment.SpecialFolder.ProgramFilesX86 },
        { "{905e63b6-c1bf-494e-b29c-65b732d3d21a}", Environment.SpecialFolder.ProgramFiles }, // Program Files
        { "{6D809377-6AF0-444b-8957-A3773F02200E}", Environment.SpecialFolder.ProgramFiles }, // Program Files (alternate)
        { "{F38BF404-1D43-42F2-9305-67DE0B28FC23}", Environment.SpecialFolder.Windows }, // Windows folder
    };

    /// <summary>
    /// Résout un chemin contenant un Known Folder GUID vers un chemin réel
    /// </summary>
    private static string? ResolveKnownFolderPath(string path)
    {
        // Pattern: {GUID}\relative\path
        if (!path.StartsWith("{")) return null;
        
        var closeBrace = path.IndexOf('}');
        if (closeBrace < 0) return null;
        
        var guid = path[..(closeBrace + 1)];
        var relativePath = path[(closeBrace + 1)..].TrimStart('\\', '/');
        
        string? basePath = null;
        
        // Cas spécial pour System32
        if (guid.Equals("{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}", StringComparison.OrdinalIgnoreCase))
        {
            basePath = Environment.GetFolderPath(Environment.SpecialFolder.System);
        }
        else if (KnownFolderGuids.TryGetValue(guid, out var specialFolder) && specialFolder.HasValue)
        {
            basePath = Environment.GetFolderPath(specialFolder.Value);
        }
        
        if (string.IsNullOrEmpty(basePath)) return null;
        
        var fullPath = Path.Combine(basePath, relativePath);
        return File.Exists(fullPath) ? fullPath : null;
    }

    /// <summary>
    /// Vérifie si le chemin est un AppUserModelId (provenant de shell:AppsFolder)
    /// </summary>
    private static bool IsAppUserModelId(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        
        // Chemins Windows classiques avec lettre de lecteur
        if (path.Length >= 2 && path[1] == ':') return false;
        
        // Chemins UNC
        if (path.StartsWith("\\\\") || path.StartsWith("//")) return false;
        
        // Apps UWP avec ! (ex: Microsoft.Windows.Photos_8wekyb3d8bbwe!App)
        if (path.Contains('!')) return true;
        
        // Apps Squirrel/Electron (ex: com.squirrel.AnthropicClaude.claude)
        if (path.StartsWith("com.squirrel.", StringComparison.OrdinalIgnoreCase)) return true;
        
        // Apps auto-générées Visual Studio (ex: Microsoft.AutoGenerated.{GUID})
        if (path.StartsWith("Microsoft.AutoGenerated.", StringComparison.OrdinalIgnoreCase)) return true;
        
        // Chemins avec Known Folder GUID (ex: {1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\osk.exe)
        if (path.StartsWith("{") && path.Contains("}\\")) return true;
        
        // AppUserModelId avec underscore (ex: Microsoft.WindowsCalculator_8wekyb3d8bbwe)
        if (path.Contains('_') && !Path.HasExtension(path) && !File.Exists(path)) return true;
        
        // Autres chemins sans extension qui ne sont pas des fichiers/dossiers existants
        if (!Path.HasExtension(path) && !File.Exists(path) && !Directory.Exists(path)) return true;
        
        return false;
    }

    /// <summary>
    /// Extrait l'icône depuis le package UWP/MSIX
    /// </summary>
    private static ImageSource? ExtractIconFromPackage(string appUserModelId, int size)
    {
        try
        {
            var bangIndex = appUserModelId.IndexOf('!');
            if (bangIndex <= 0) return null;

            var packageFamilyName = appUserModelId[..bangIndex];
            Debug.WriteLine($"[IconExtractor] Looking for package: {packageFamilyName}");
            
            var packageManager = new PackageManager();
            var packages = packageManager.FindPackagesForUser(string.Empty)
                .Where(p => p.Id.FamilyName.Equals(packageFamilyName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var package in packages)
            {
                try
                {
                    var installPath = package.InstalledPath;
                    if (string.IsNullOrEmpty(installPath)) continue;

                    Debug.WriteLine($"[IconExtractor] Package found at: {installPath}");

                    // Lire le manifest pour trouver le logo
                    var manifestPath = Path.Combine(installPath, "AppxManifest.xml");
                    if (File.Exists(manifestPath))
                    {
                        var logoPath = FindLogoInManifest(manifestPath, installPath);
                        if (!string.IsNullOrEmpty(logoPath) && File.Exists(logoPath))
                        {
                            Debug.WriteLine($"[IconExtractor] Using logo from manifest: {logoPath}");
                            return LoadImageFromFile(logoPath, size);
                        }
                    }

                    // Fallback: chercher dans Assets
                    var assetsPath = Path.Combine(installPath, "Assets");
                    if (Directory.Exists(assetsPath))
                    {
                        var logoPath = FindBestLogo(assetsPath, size);
                        if (!string.IsNullOrEmpty(logoPath))
                        {
                            Debug.WriteLine($"[IconExtractor] Using logo from Assets: {logoPath}");
                            return LoadImageFromFile(logoPath, size);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[IconExtractor] Error with package: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[IconExtractor] ExtractIconFromPackage error: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Fallback: cherche l'icône directement dans les répertoires d'installation connus
    /// </summary>
    private static ImageSource? ExtractIconFromPackageInstallPath(string appUserModelId, int size)
    {
        try
        {
            var bangIndex = appUserModelId.IndexOf('!');
            if (bangIndex <= 0) return null;

            var packageFamilyName = appUserModelId[..bangIndex];
            
            // Chemins possibles pour les apps Windows
            var searchPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps"),
                @"C:\Windows\SystemApps",
                @"C:\Program Files\WindowsApps"
            };

            foreach (var searchPath in searchPaths)
            {
                if (!Directory.Exists(searchPath)) continue;
                
                try
                {
                    // Chercher un dossier qui correspond au package family name
                    var matchingDirs = Directory.GetDirectories(searchPath, $"{packageFamilyName.Split('_')[0]}*", SearchOption.TopDirectoryOnly);
                    
                    foreach (var dir in matchingDirs)
                    {
                        var assetsPath = Path.Combine(dir, "Assets");
                        if (Directory.Exists(assetsPath))
                        {
                            var logoPath = FindBestLogo(assetsPath, size);
                            if (!string.IsNullOrEmpty(logoPath))
                            {
                                Debug.WriteLine($"[IconExtractor] Found icon via install path: {logoPath}");
                                return LoadImageFromFile(logoPath, size);
                            }
                        }
                        
                        // Essayer aussi à la racine du package
                        var rootLogos = Directory.GetFiles(dir, "*.png", SearchOption.TopDirectoryOnly)
                            .Where(f => f.Contains("Logo", StringComparison.OrdinalIgnoreCase))
                            .FirstOrDefault();
                        
                        if (!string.IsNullOrEmpty(rootLogos))
                        {
                            Debug.WriteLine($"[IconExtractor] Found icon at package root: {rootLogos}");
                            return LoadImageFromFile(rootLogos, size);
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Skip inaccessible directories
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[IconExtractor] ExtractIconFromPackageInstallPath error: {ex.Message}");
        }
        return null;
    }

    private static string? FindLogoInManifest(string manifestPath, string installPath)
    {
        try
        {
            var content = File.ReadAllText(manifestPath);
            
            // Chercher Square44x44Logo ou Square150x150Logo
            var patterns = new[] { "Square44x44Logo", "Square150x150Logo", "StoreLogo" };
            foreach (var pattern in patterns)
            {
                var attr = $"{pattern}=\"";
                var idx = content.IndexOf(attr);
                if (idx >= 0)
                {
                    idx += attr.Length;
                    var endIdx = content.IndexOf('"', idx);
                    if (endIdx > idx)
                    {
                        var relativePath = content[idx..endIdx];
                        var baseDir = Path.GetDirectoryName(relativePath) ?? "";
                        var baseName = Path.GetFileNameWithoutExtension(relativePath);
                        var ext = Path.GetExtension(relativePath);
                        var assetsDir = Path.Combine(installPath, baseDir);

                        // Chercher les variantes
                        var variants = new[]
                        {
                            $"{baseName}.targetsize-48{ext}",
                            $"{baseName}.targetsize-48_altform-unplated{ext}",
                            $"{baseName}.targetsize-32{ext}",
                            $"{baseName}.targetsize-32_altform-unplated{ext}",
                            $"{baseName}.scale-200{ext}",
                            $"{baseName}.scale-150{ext}",
                            $"{baseName}.scale-100{ext}",
                            $"{baseName}{ext}"
                        };

                        foreach (var variant in variants)
                        {
                            var fullPath = Path.Combine(assetsDir, variant);
                            if (File.Exists(fullPath))
                                return fullPath;
                        }

                        // Essayer le chemin original
                        var originalPath = Path.Combine(installPath, relativePath);
                        if (File.Exists(originalPath))
                            return originalPath;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    private static string? FindBestLogo(string assetsPath, int targetSize)
    {
        try
        {
            var files = Directory.GetFiles(assetsPath, "*.png", SearchOption.AllDirectories);
            
            // Préférer les fichiers avec targetsize ou scale
            var preferred = files
                .Where(f => 
                    f.Contains("Square44x44", StringComparison.OrdinalIgnoreCase) ||
                    f.Contains("Square150x150", StringComparison.OrdinalIgnoreCase) ||
                    f.Contains("AppList", StringComparison.OrdinalIgnoreCase) ||
                    f.Contains("StoreLogo", StringComparison.OrdinalIgnoreCase) ||
                    f.Contains("Logo", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.Contains($"targetsize-{targetSize}", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(f => f.Contains("targetsize-48", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(f => f.Contains("targetsize-32", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(f => f.Contains("scale-200", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(f => f.Contains("scale-150", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(f => f.Contains("Square44x44", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            // Si rien trouvé, prendre n'importe quel PNG qui ressemble à un logo
            if (preferred == null)
            {
                preferred = files
                    .Where(f => !f.Contains("contrast", StringComparison.OrdinalIgnoreCase) &&
                                !f.Contains("Wide", StringComparison.OrdinalIgnoreCase) &&
                                !f.Contains("Splash", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.Contains("Logo", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();
            }

            return preferred;
        }
        catch { }
        return null;
    }

    private static ImageSource? LoadImageFromFile(string filePath, int size)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = size;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[IconExtractor] LoadImageFromFile error: {ex.Message}");
            return null;
        }
    }

    private static ImageSource? ExtractIconFromShellItem(string appUserModelId, int size)
    {
        IShellItemImageFactory? factory = null;
        try
        {
            var shellPath = $"shell:AppsFolder\\{appUserModelId}";
            Debug.WriteLine($"[IconExtractor] Trying ShellItem: {shellPath}");
            
            var hr = SHCreateItemFromParsingName(
                shellPath,
                IntPtr.Zero,
                typeof(IShellItemImageFactory).GUID,
                out factory);

            if (hr != 0)
            {
                Debug.WriteLine($"[IconExtractor] SHCreateItemFromParsingName failed with hr=0x{hr:X8}");
                return null;
            }
            
            if (factory == null)
                return null;

            // Essayer plusieurs combinaisons de tailles et flags
            var sizesToTry = new[] { size, 48, 32, 64, 256 };
            var flagsToTry = new[] { 
                SIIGBF.SIIGBF_ICONONLY,
                SIIGBF.SIIGBF_BIGGERSIZEOK | SIIGBF.SIIGBF_ICONONLY,
                SIIGBF.SIIGBF_BIGGERSIZEOK,
                SIIGBF.SIIGBF_RESIZETOFIT,
                SIIGBF.SIIGBF_RESIZETOFIT | SIIGBF.SIIGBF_ICONONLY
            };

            foreach (var trySize in sizesToTry.Distinct())
            {
                foreach (var flags in flagsToTry)
                {
                    hr = factory.GetImage(new SIZE(trySize, trySize), flags, out var hBitmap);
                    
                    if (hr == 0 && hBitmap != IntPtr.Zero)
                    {
                        try
                        {
                            using var bitmap = Image.FromHbitmap(hBitmap);
                            
                            // Vérifier que l'image n'est pas vide
                            if (bitmap.Width < 4 || bitmap.Height < 4)
                            {
                                continue;
                            }
                            
                            // Vérifier si c'est l'icône générique
                            if (IsGenericIcon(bitmap))
                            {
                                Debug.WriteLine($"[IconExtractor] Generic icon detected, trying next...");
                                continue;
                            }

                            using var ms = new MemoryStream();
                            bitmap.Save(ms, ImageFormat.Png);
                            ms.Position = 0;

                            var bitmapImage = new BitmapImage();
                            bitmapImage.BeginInit();
                            bitmapImage.StreamSource = ms;
                            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                            bitmapImage.EndInit();
                            bitmapImage.Freeze();
                            
                            Debug.WriteLine($"[IconExtractor] ShellItem succeeded with size={trySize}, flags={flags}");
                            return bitmapImage;
                        }
                        finally
                        {
                            DeleteObject(hBitmap);
                        }
                    }
                }
            }
            
            Debug.WriteLine($"[IconExtractor] ShellItem: all combinations failed");
        }
        catch (COMException ex)
        {
            Debug.WriteLine($"[IconExtractor] ShellItem COMException: 0x{ex.HResult:X8}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[IconExtractor] ShellItem error: {ex.Message}");
        }
        finally
        {
            if (factory != null)
            {
                try { Marshal.ReleaseComObject(factory); } catch { }
            }
        }
        return null;
    }

    /// <summary>
    /// Détecte si l'icône est l'icône générique Windows (majoritairement blanche/grise)
    /// </summary>
    private static bool IsGenericIcon(Bitmap bitmap)
    {
        // Désactivé temporairement - causait des faux positifs
        return false;
        
        /*
        try
        {
            int whiteGrayCount = 0;
            int colorCount = 0;
            int totalPixels = 0;

            // Échantillonner quelques pixels
            for (int x = 0; x < bitmap.Width; x += 4)
            {
                for (int y = 0; y < bitmap.Height; y += 4)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (pixel.A < 50) continue; // Ignorer les pixels transparents
                    
                    totalPixels++;
                    
                    // Vérifier si le pixel est blanc/gris (R ≈ G ≈ B et valeurs hautes)
                    var isGrayish = Math.Abs(pixel.R - pixel.G) < 30 && 
                                    Math.Abs(pixel.G - pixel.B) < 30 &&
                                    Math.Abs(pixel.R - pixel.B) < 30;
                    
                    if (isGrayish && pixel.R > 150)
                        whiteGrayCount++;
                    else if (!isGrayish || pixel.R < 100)
                        colorCount++;
                }
            }

            if (totalPixels == 0) return true;

            // Si plus de 80% des pixels sont blanc/gris, c'est probablement l'icône générique
            var ratio = (double)whiteGrayCount / totalPixels;
            var hasColors = colorCount > totalPixels * 0.1;
            
            return ratio > 0.8 && !hasColors;
        }
        catch
        {
            return false;
        }
        */
    }

    private static ImageSource? ExtractIconFromFile(string filePath, bool largeIcon)
    {
        if (!File.Exists(filePath) && !Directory.Exists(filePath))
            return null;

        var shinfo = new SHFILEINFO();
        var flags = SHGFI_ICON | (largeIcon ? SHGFI_LARGEICON : SHGFI_SMALLICON);
        
        var result = SHGetFileInfo(filePath, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);
        
        if (result == IntPtr.Zero || shinfo.hIcon == IntPtr.Zero)
            return null;

        try
        {
            var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                shinfo.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmapSource.Freeze();
            return bitmapSource;
        }
        catch
        {
            return null;
        }
        finally
        {
            DestroyIcon(shinfo.hIcon);
        }
    }

    public static void ClearCache() => _iconCache.Clear();
    public static int CacheCount => _iconCache.Count;
}

public enum StockIconType { Folder, Document, Application, Search, Help, Settings }
