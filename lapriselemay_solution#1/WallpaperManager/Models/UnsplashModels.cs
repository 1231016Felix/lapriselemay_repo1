using System.Text.Json.Serialization;

namespace WallpaperManager.Models;

public class UnsplashPhoto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    
    [JsonPropertyName("width")]
    public int Width { get; set; }
    
    [JsonPropertyName("height")]
    public int Height { get; set; }
    
    [JsonPropertyName("color")]
    public string? Color { get; set; }
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("alt_description")]
    public string? AltDescription { get; set; }
    
    [JsonPropertyName("urls")]
    public UnsplashUrls Urls { get; set; } = new();
    
    [JsonPropertyName("user")]
    public UnsplashUser User { get; set; } = new();
    
    [JsonPropertyName("links")]
    public UnsplashLinks Links { get; set; } = new();
    
    [JsonPropertyName("tags")]
    public List<UnsplashTag> Tags { get; set; } = [];
}

public class UnsplashUrls
{
    [JsonPropertyName("raw")]
    public string Raw { get; set; } = string.Empty;
    
    [JsonPropertyName("full")]
    public string Full { get; set; } = string.Empty;
    
    [JsonPropertyName("regular")]
    public string Regular { get; set; } = string.Empty;
    
    [JsonPropertyName("small")]
    public string Small { get; set; } = string.Empty;
    
    [JsonPropertyName("thumb")]
    public string Thumb { get; set; } = string.Empty;
}

public class UnsplashUser
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;
    
    [JsonPropertyName("links")]
    public UnsplashUserLinks Links { get; set; } = new();
}

public class UnsplashUserLinks
{
    [JsonPropertyName("html")]
    public string Html { get; set; } = string.Empty;
}

public class UnsplashLinks
{
    [JsonPropertyName("download")]
    public string Download { get; set; } = string.Empty;
    
    [JsonPropertyName("download_location")]
    public string DownloadLocation { get; set; } = string.Empty;
    
    [JsonPropertyName("html")]
    public string Html { get; set; } = string.Empty;
}

public class UnsplashTag
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
}

public class UnsplashSearchResult
{
    [JsonPropertyName("total")]
    public int Total { get; set; }
    
    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }
    
    [JsonPropertyName("results")]
    public List<UnsplashPhoto> Results { get; set; } = [];
}
