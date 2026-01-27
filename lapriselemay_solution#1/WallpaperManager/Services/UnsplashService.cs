using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using WallpaperManager.Models;

namespace WallpaperManager.Services;

/// <summary>
/// Service pour l'API Unsplash.
/// </summary>
public sealed class UnsplashService : BaseImageApiService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };
    
    private const string BaseUrl = "https://api.unsplash.com";
    
    protected override string ServiceName => "Unsplash";
    
    public override bool IsConfigured => !string.IsNullOrEmpty(SettingsService.Current.UnsplashApiKey);
    
    protected override void EnsureAuthHeader()
    {
        var apiKey = SettingsService.Current.UnsplashApiKey;
        if (string.IsNullOrEmpty(apiKey)) return;
        
        // Ne mettre à jour que si la clé a changé
        if (CachedApiKey == apiKey) return;
        
        CachedApiKey = apiKey;
        HttpClient.DefaultRequestHeaders.Remove("Authorization");
        HttpClient.DefaultRequestHeaders.Add("Authorization", $"Client-ID {apiKey}");
    }
    
    public async Task<List<UnsplashPhoto>> SearchPhotosAsync(
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
            var url = $"{BaseUrl}/search/photos?query={Uri.EscapeDataString(query)}&page={page}&per_page={perPage}&orientation=landscape";
            
            using var response = await HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadFromJsonAsync<UnsplashSearchResult>(JsonOptions, cancellationToken).ConfigureAwait(false);
            return result?.Results ?? [];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur Unsplash search HTTP: {ex.Message}");
            return [];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur Unsplash search: {ex.Message}");
            return [];
        }
    }
    
    public async Task<List<UnsplashPhoto>> GetRandomPhotosAsync(
        int count = 10, 
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (!IsConfigured) return [];
        
        EnsureAuthHeader();
        
        try
        {
            var url = $"{BaseUrl}/photos/random?count={Math.Min(count, 30)}&orientation=landscape";
            if (!string.IsNullOrEmpty(query))
                url = string.Concat(url, "&query=", Uri.EscapeDataString(query));
            
            using var response = await HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            
            return await response.Content.ReadFromJsonAsync<List<UnsplashPhoto>>(JsonOptions, cancellationToken).ConfigureAwait(false) ?? [];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur Unsplash random HTTP: {ex.Message}");
            return [];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur Unsplash random: {ex.Message}");
            return [];
        }
    }
    
    public async Task<string?> DownloadPhotoAsync(
        UnsplashPhoto photo, 
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ThrowIfDisposed();
        
        EnsureAuthHeader();
        
        // Notifier Unsplash du téléchargement (requis par leur API)
        if (!string.IsNullOrEmpty(photo.Links.DownloadLocation))
        {
            try 
            { 
                using var _ = await HttpClient.GetAsync(photo.Links.DownloadLocation, cancellationToken).ConfigureAwait(false); 
            }
            catch 
            { 
                // Ignorer les erreurs de tracking 
            }
        }
        
        return await DownloadImageAsync(photo.Urls.Full, photo.Id, progress, cancellationToken).ConfigureAwait(false);
    }
    
    public static Wallpaper CreateWallpaperFromPhoto(UnsplashPhoto photo, string localPath)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentException.ThrowIfNullOrEmpty(localPath);
        
        return new Wallpaper
        {
            Name = photo.Description ?? photo.AltDescription ?? $"Unsplash - {photo.Id}",
            FilePath = localPath,
            Type = WallpaperType.Static,
            Width = photo.Width,
            Height = photo.Height,
            UnsplashId = photo.Id,
            SourceId = $"unsplash_{photo.Id}",
            Author = photo.User.Name,
            AuthorUrl = photo.User.Links.Html,
            Tags = photo.Tags.Select(t => t.Title).ToArray()
        };
    }
}
