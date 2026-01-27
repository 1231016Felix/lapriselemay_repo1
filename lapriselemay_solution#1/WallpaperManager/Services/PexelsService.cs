using System.Net.Http;
using System.Net.Http.Json;
using WallpaperManager.Models;

namespace WallpaperManager.Services;

/// <summary>
/// Service pour l'API Pexels.
/// API gratuite : 200 requêtes/heure, 20 000/mois.
/// </summary>
public sealed class PexelsService : BaseImageApiService
{
    private const string BaseUrl = "https://api.pexels.com/v1";
    
    protected override string ServiceName => "Pexels";
    
    public override bool IsConfigured => !string.IsNullOrEmpty(SettingsService.Current.PexelsApiKey);
    
    protected override void EnsureAuthHeader()
    {
        var apiKey = SettingsService.Current.PexelsApiKey;
        if (string.IsNullOrEmpty(apiKey)) return;
        
        if (CachedApiKey == apiKey) return;
        
        CachedApiKey = apiKey;
        HttpClient.DefaultRequestHeaders.Remove("Authorization");
        HttpClient.DefaultRequestHeaders.Add("Authorization", apiKey);
    }
    
    public async Task<List<PexelsPhoto>> SearchPhotosAsync(
        string query,
        int page = 1,
        int perPage = 20,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (!IsConfigured) return [];
        
        EnsureAuthHeader();
        
        try
        {
            var url = $"{BaseUrl}/search?query={Uri.EscapeDataString(query)}&page={page}&per_page={perPage}&orientation=landscape";
            
            using var response = await HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadFromJsonAsync<PexelsSearchResult>(cancellationToken: cancellationToken).ConfigureAwait(false);
            return result?.Photos ?? [];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur Pexels search HTTP: {ex.Message}");
            return [];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur Pexels search: {ex.Message}");
            return [];
        }
    }
    
    public async Task<List<PexelsPhoto>> GetCuratedPhotosAsync(
        int page = 1,
        int perPage = 20,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (!IsConfigured) return [];
        
        EnsureAuthHeader();
        
        try
        {
            var url = $"{BaseUrl}/curated?page={page}&per_page={perPage}";
            
            using var response = await HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadFromJsonAsync<PexelsSearchResult>(cancellationToken: cancellationToken).ConfigureAwait(false);
            return result?.Photos ?? [];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur Pexels curated: {ex.Message}");
            return [];
        }
    }
    
    public async Task<string?> DownloadPhotoAsync(
        PexelsPhoto photo,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ThrowIfDisposed();
        
        return await DownloadImageAsync(photo.Src.Original, photo.Id.ToString(), progress, cancellationToken).ConfigureAwait(false);
    }
    
    public static Wallpaper CreateWallpaperFromPhoto(PexelsPhoto photo, string localPath)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentException.ThrowIfNullOrEmpty(localPath);
        
        return new Wallpaper
        {
            Name = photo.Alt ?? $"Pexels - {photo.Id}",
            FilePath = localPath,
            Type = WallpaperType.Static,
            Width = photo.Width,
            Height = photo.Height,
            Author = photo.Photographer,
            AuthorUrl = photo.PhotographerUrl,
            SourceId = $"pexels_{photo.Id}"
        };
    }
}
