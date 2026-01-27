using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using WallpaperManager.Models;

namespace WallpaperManager.Services;

/// <summary>
/// Service pour l'API Pixabay.
/// API gratuite : 100 requêtes/minute.
/// Note: Pixabay utilise la clé API dans les paramètres de requête, pas dans les headers.
/// </summary>
public sealed class PixabayService : BaseImageApiService
{
    private const string BaseUrl = "https://pixabay.com/api";
    
    protected override string ServiceName => "Pixabay";
    
    public override bool IsConfigured => !string.IsNullOrEmpty(SettingsService.Current.PixabayApiKey);
    
    protected override void EnsureAuthHeader()
    {
        // Pixabay utilise la clé API dans l'URL, pas dans les headers
        // Cette méthode est vide mais requise par la classe de base
    }
    
    private string GetApiKey() => SettingsService.Current.PixabayApiKey ?? string.Empty;
    
    public async Task<List<PixabayPhoto>> SearchPhotosAsync(
        string query,
        int page = 1,
        int perPage = 20,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (!IsConfigured) return [];
        
        try
        {
            var url = $"{BaseUrl}/?key={GetApiKey()}&q={Uri.EscapeDataString(query)}&page={page}&per_page={perPage}&orientation=horizontal&image_type=photo&min_width=1920&safesearch=true";
            
            using var response = await HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadFromJsonAsync<PixabaySearchResult>(cancellationToken: cancellationToken).ConfigureAwait(false);
            return result?.Hits ?? [];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur Pixabay search HTTP: {ex.Message}");
            return [];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur Pixabay search: {ex.Message}");
            return [];
        }
    }
    
    public async Task<List<PixabayPhoto>> GetPopularPhotosAsync(
        int page = 1,
        int perPage = 20,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (!IsConfigured) return [];
        
        try
        {
            var url = $"{BaseUrl}/?key={GetApiKey()}&page={page}&per_page={perPage}&orientation=horizontal&image_type=photo&min_width=1920&order=popular&safesearch=true";
            
            using var response = await HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadFromJsonAsync<PixabaySearchResult>(cancellationToken: cancellationToken).ConfigureAwait(false);
            return result?.Hits ?? [];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur Pixabay popular: {ex.Message}");
            return [];
        }
    }
    
    public async Task<string?> DownloadPhotoAsync(
        PixabayPhoto photo,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ThrowIfDisposed();
        
        // Pixabay peut avoir différentes extensions
        var imageUrl = photo.BestUrl;
        var extension = Path.GetExtension(new Uri(imageUrl).AbsolutePath);
        if (string.IsNullOrEmpty(extension)) extension = ".jpg";
        
        // Utiliser un ID avec extension pour Pixabay
        var photoIdWithExt = $"{photo.Id}{extension}".Replace(".jpg", ""); // La méthode de base ajoute .jpg
        
        return await DownloadImageAsync(imageUrl, photo.Id.ToString(), progress, cancellationToken).ConfigureAwait(false);
    }
    
    public static Wallpaper CreateWallpaperFromPhoto(PixabayPhoto photo, string localPath)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentException.ThrowIfNullOrEmpty(localPath);
        
        return new Wallpaper
        {
            Name = !string.IsNullOrEmpty(photo.Tags) 
                ? photo.Tags.Split(',')[0].Trim() 
                : $"Pixabay - {photo.Id}",
            FilePath = localPath,
            Type = WallpaperType.Static,
            Width = photo.ImageWidth,
            Height = photo.ImageHeight,
            Author = photo.User,
            AuthorUrl = $"https://pixabay.com/users/{photo.User}-{photo.UserId}/",
            SourceId = $"pixabay_{photo.Id}",
            Tags = photo.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        };
    }
}
