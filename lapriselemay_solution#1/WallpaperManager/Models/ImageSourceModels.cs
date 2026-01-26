using System.Text.Json.Serialization;

namespace WallpaperManager.Models;

#region Pexels

public class PexelsPhoto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("width")]
    public int Width { get; set; }
    
    [JsonPropertyName("height")]
    public int Height { get; set; }
    
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
    
    [JsonPropertyName("photographer")]
    public string Photographer { get; set; } = string.Empty;
    
    [JsonPropertyName("photographer_url")]
    public string PhotographerUrl { get; set; } = string.Empty;
    
    [JsonPropertyName("avg_color")]
    public string? AvgColor { get; set; }
    
    [JsonPropertyName("src")]
    public PexelsSrc Src { get; set; } = new();
    
    [JsonPropertyName("alt")]
    public string? Alt { get; set; }
}

public class PexelsSrc
{
    [JsonPropertyName("original")]
    public string Original { get; set; } = string.Empty;
    
    [JsonPropertyName("large2x")]
    public string Large2x { get; set; } = string.Empty;
    
    [JsonPropertyName("large")]
    public string Large { get; set; } = string.Empty;
    
    [JsonPropertyName("medium")]
    public string Medium { get; set; } = string.Empty;
    
    [JsonPropertyName("small")]
    public string Small { get; set; } = string.Empty;
    
    [JsonPropertyName("portrait")]
    public string Portrait { get; set; } = string.Empty;
    
    [JsonPropertyName("landscape")]
    public string Landscape { get; set; } = string.Empty;
    
    [JsonPropertyName("tiny")]
    public string Tiny { get; set; } = string.Empty;
}

public class PexelsSearchResult
{
    [JsonPropertyName("total_results")]
    public int TotalResults { get; set; }
    
    [JsonPropertyName("page")]
    public int Page { get; set; }
    
    [JsonPropertyName("per_page")]
    public int PerPage { get; set; }
    
    [JsonPropertyName("photos")]
    public List<PexelsPhoto> Photos { get; set; } = [];
    
    [JsonPropertyName("next_page")]
    public string? NextPage { get; set; }
}

#endregion

#region Pixabay

public class PixabayPhoto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("pageURL")]
    public string PageUrl { get; set; } = string.Empty;
    
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
    
    [JsonPropertyName("tags")]
    public string Tags { get; set; } = string.Empty;
    
    [JsonPropertyName("previewURL")]
    public string PreviewUrl { get; set; } = string.Empty;
    
    [JsonPropertyName("previewWidth")]
    public int PreviewWidth { get; set; }
    
    [JsonPropertyName("previewHeight")]
    public int PreviewHeight { get; set; }
    
    [JsonPropertyName("webformatURL")]
    public string WebformatUrl { get; set; } = string.Empty;
    
    [JsonPropertyName("webformatWidth")]
    public int WebformatWidth { get; set; }
    
    [JsonPropertyName("webformatHeight")]
    public int WebformatHeight { get; set; }
    
    [JsonPropertyName("largeImageURL")]
    public string LargeImageUrl { get; set; } = string.Empty;
    
    [JsonPropertyName("fullHDURL")]
    public string? FullHdUrl { get; set; }
    
    [JsonPropertyName("imageURL")]
    public string? ImageUrl { get; set; }
    
    [JsonPropertyName("imageWidth")]
    public int ImageWidth { get; set; }
    
    [JsonPropertyName("imageHeight")]
    public int ImageHeight { get; set; }
    
    [JsonPropertyName("imageSize")]
    public long ImageSize { get; set; }
    
    [JsonPropertyName("views")]
    public int Views { get; set; }
    
    [JsonPropertyName("downloads")]
    public int Downloads { get; set; }
    
    [JsonPropertyName("likes")]
    public int Likes { get; set; }
    
    [JsonPropertyName("user")]
    public string User { get; set; } = string.Empty;
    
    [JsonPropertyName("user_id")]
    public int UserId { get; set; }
    
    [JsonPropertyName("userImageURL")]
    public string UserImageUrl { get; set; } = string.Empty;
    
    // Propriétés calculées pour l'UI
    public string BestUrl => FullHdUrl ?? LargeImageUrl ?? WebformatUrl;
    public string ThumbnailUrl => PreviewUrl;
}

public class PixabaySearchResult
{
    [JsonPropertyName("total")]
    public int Total { get; set; }
    
    [JsonPropertyName("totalHits")]
    public int TotalHits { get; set; }
    
    [JsonPropertyName("hits")]
    public List<PixabayPhoto> Hits { get; set; } = [];
}

#endregion

#region Interface commune pour l'UI

/// <summary>
/// Interface commune pour afficher les photos de différentes sources.
/// </summary>
public interface IPhotoResult
{
    string Id { get; }
    string ThumbnailUrl { get; }
    string FullUrl { get; }
    string Author { get; }
    string AuthorUrl { get; }
    int Width { get; }
    int Height { get; }
    string Source { get; }
}

public class PexelsPhotoWrapper : IPhotoResult
{
    private readonly PexelsPhoto _photo;
    
    public PexelsPhotoWrapper(PexelsPhoto photo) => _photo = photo;
    
    public string Id => $"pexels_{_photo.Id}";
    public string ThumbnailUrl => _photo.Src.Medium;
    public string FullUrl => _photo.Src.Original;
    public string Author => _photo.Photographer;
    public string AuthorUrl => _photo.PhotographerUrl;
    public int Width => _photo.Width;
    public int Height => _photo.Height;
    public string Source => "Pexels";
    
    public PexelsPhoto Original => _photo;
}

public class PixabayPhotoWrapper : IPhotoResult
{
    private readonly PixabayPhoto _photo;
    
    public PixabayPhotoWrapper(PixabayPhoto photo) => _photo = photo;
    
    public string Id => $"pixabay_{_photo.Id}";
    public string ThumbnailUrl => _photo.PreviewUrl;
    public string FullUrl => _photo.BestUrl;
    public string Author => _photo.User;
    public string AuthorUrl => $"https://pixabay.com/users/{_photo.User}-{_photo.UserId}/";
    public int Width => _photo.ImageWidth;
    public int Height => _photo.ImageHeight;
    public string Source => "Pixabay";
    
    public PixabayPhoto Original => _photo;
}

#endregion
