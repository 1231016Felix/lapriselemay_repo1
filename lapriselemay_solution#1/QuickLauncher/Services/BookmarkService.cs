using System.IO;
using System.Text.Json;
using QuickLauncher.Models;

namespace QuickLauncher.Services;

/// <summary>
/// Service pour indexer les favoris des navigateurs (Chrome, Edge, Firefox).
/// </summary>
public static class BookmarkService
{
    #region Browser Paths
    
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string RoamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    
    // Chrome
    private static readonly string ChromeBookmarksPath = Path.Combine(
        LocalAppData, "Google", "Chrome", "User Data", "Default", "Bookmarks");
    
    // Edge
    private static readonly string EdgeBookmarksPath = Path.Combine(
        LocalAppData, "Microsoft", "Edge", "User Data", "Default", "Bookmarks");
    
    // Firefox (utilise un profil avec nom aléatoire)
    private static readonly string FirefoxProfilesPath = Path.Combine(
        RoamingAppData, "Mozilla", "Firefox", "Profiles");
    
    // Brave
    private static readonly string BraveBookmarksPath = Path.Combine(
        LocalAppData, "BraveSoftware", "Brave-Browser", "User Data", "Default", "Bookmarks");
    
    // Vivaldi
    private static readonly string VivaldiBookmarksPath = Path.Combine(
        LocalAppData, "Vivaldi", "User Data", "Default", "Bookmarks");
    
    #endregion
    
    /// <summary>
    /// Récupère tous les favoris de tous les navigateurs installés.
    /// </summary>
    public static List<SearchResult> GetAllBookmarks()
    {
        var bookmarks = new List<SearchResult>();
        
        // Chrome
        bookmarks.AddRange(GetChromiumBookmarks(ChromeBookmarksPath, "Chrome"));
        
        // Edge
        bookmarks.AddRange(GetChromiumBookmarks(EdgeBookmarksPath, "Edge"));
        
        // Brave
        bookmarks.AddRange(GetChromiumBookmarks(BraveBookmarksPath, "Brave"));
        
        // Vivaldi
        bookmarks.AddRange(GetChromiumBookmarks(VivaldiBookmarksPath, "Vivaldi"));
        
        // Firefox
        bookmarks.AddRange(GetFirefoxBookmarks());
        
        // Dédupliquer par URL
        return bookmarks
            .GroupBy(b => b.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }
    
    #region Chromium-based browsers (Chrome, Edge, Brave, Vivaldi)
    
    /// <summary>
    /// Lit les favoris d'un navigateur basé sur Chromium.
    /// Le format est JSON avec une structure roots > bookmark_bar/other/synced.
    /// </summary>
    private static List<SearchResult> GetChromiumBookmarks(string bookmarksPath, string browserName)
    {
        var results = new List<SearchResult>();
        
        if (!File.Exists(bookmarksPath))
            return results;
        
        try
        {
            var json = File.ReadAllText(bookmarksPath);
            using var doc = JsonDocument.Parse(json);
            
            var roots = doc.RootElement.GetProperty("roots");
            
            // Parcourir les différentes sections de favoris
            if (roots.TryGetProperty("bookmark_bar", out var bookmarkBar))
                ParseChromiumFolder(bookmarkBar, results, browserName, "Barre de favoris");
            
            if (roots.TryGetProperty("other", out var other))
                ParseChromiumFolder(other, results, browserName, "Autres favoris");
            
            if (roots.TryGetProperty("synced", out var synced))
                ParseChromiumFolder(synced, results, browserName, "Favoris synchronisés");
        }
        catch
        {
            // Silently fail - le fichier peut être verrouillé ou corrompu
        }
        
        return results;
    }
    
    private static void ParseChromiumFolder(JsonElement element, List<SearchResult> results, 
        string browserName, string folderPath)
    {
        if (!element.TryGetProperty("children", out var children))
            return;
        
        foreach (var child in children.EnumerateArray())
        {
            if (!child.TryGetProperty("type", out var typeElement))
                continue;
            
            var type = typeElement.GetString();
            
            if (type == "url")
            {
                // C'est un favori
                var name = child.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var url = child.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(url) && IsValidUrl(url))
                {
                    results.Add(new SearchResult
                    {
                        Name = name,
                        Path = url,
                        Description = $"{browserName} • {folderPath}",
                        Type = ResultType.Bookmark
                    });
                }
            }
            else if (type == "folder")
            {
                // C'est un dossier, parcourir récursivement
                var folderName = child.TryGetProperty("name", out var fn) ? fn.GetString() ?? "" : "";
                var newPath = string.IsNullOrEmpty(folderName) ? folderPath : $"{folderPath}/{folderName}";
                ParseChromiumFolder(child, results, browserName, newPath);
            }
        }
    }
    
    #endregion
    
    #region Firefox
    
    /// <summary>
    /// Lit les favoris de Firefox.
    /// Firefox stocke les favoris dans une base SQLite (places.sqlite).
    /// </summary>
    private static List<SearchResult> GetFirefoxBookmarks()
    {
        var results = new List<SearchResult>();
        
        if (!Directory.Exists(FirefoxProfilesPath))
            return results;
        
        try
        {
            // Trouver tous les profils Firefox
            var profiles = Directory.GetDirectories(FirefoxProfilesPath);
            
            foreach (var profile in profiles)
            {
                var placesDb = Path.Combine(profile, "places.sqlite");
                if (!File.Exists(placesDb))
                    continue;
                
                // Firefox verrouille le fichier, on doit le copier
                var tempDb = Path.Combine(Path.GetTempPath(), $"firefox_bookmarks_{Guid.NewGuid()}.sqlite");
                
                try
                {
                    File.Copy(placesDb, tempDb, overwrite: true);
                    results.AddRange(ReadFirefoxDatabase(tempDb));
                }
                finally
                {
                    try { File.Delete(tempDb); } catch { }
                }
            }
        }
        catch
        {
            // Silently fail
        }
        
        return results;
    }
    
    private static List<SearchResult> ReadFirefoxDatabase(string dbPath)
    {
        var results = new List<SearchResult>();
        
        try
        {
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();
            
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT b.title, p.url, parent.title as folder
                FROM moz_bookmarks b
                JOIN moz_places p ON b.fk = p.id
                LEFT JOIN moz_bookmarks parent ON b.parent = parent.id
                WHERE b.type = 1 
                  AND p.url IS NOT NULL 
                  AND p.url NOT LIKE 'place:%'
                  AND b.title IS NOT NULL
                  AND b.title != ''
                """;
            
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(0);
                var url = reader.GetString(1);
                var folder = reader.IsDBNull(2) ? "" : reader.GetString(2);
                
                if (IsValidUrl(url))
                {
                    var description = string.IsNullOrEmpty(folder) 
                        ? "Firefox" 
                        : $"Firefox • {folder}";
                    
                    results.Add(new SearchResult
                    {
                        Name = name,
                        Path = url,
                        Description = description,
                        Type = ResultType.Bookmark
                    });
                }
            }
        }
        catch
        {
            // Silently fail - la base peut être corrompue ou incompatible
        }
        
        return results;
    }
    
    #endregion
    
    #region Helpers
    
    /// <summary>
    /// Vérifie si l'URL est valide (http/https).
    /// </summary>
    private static bool IsValidUrl(string url)
    {
        return !string.IsNullOrEmpty(url) && 
               (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
    }
    
    /// <summary>
    /// Extrait le domaine d'une URL pour l'affichage.
    /// </summary>
    public static string GetDomain(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.Host;
        }
        catch
        {
            return url;
        }
    }
    
    #endregion
}
