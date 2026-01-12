using System.Buffers;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using WallpaperManager.Models;

namespace WallpaperManager.Services;

public sealed class UnsplashService : IDisposable
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };
    private const string BaseUrl = "https://api.unsplash.com";
    private const int BufferSize = 81920; // 80KB buffer
    private volatile bool _disposed;
    private string? _cachedApiKey;
    
    public UnsplashService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }
    
    private void EnsureAuthHeader()
    {
        var apiKey = SettingsService.Current.UnsplashApiKey;
        if (string.IsNullOrEmpty(apiKey)) return;
        
        // Ne mettre à jour que si la clé a changé
        if (_cachedApiKey == apiKey) return;
        
        _cachedApiKey = apiKey;
        _httpClient.DefaultRequestHeaders.Remove("Authorization");
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Client-ID {apiKey}");
    }
    
    public async Task<List<UnsplashPhoto>> SearchPhotosAsync(
        string query, 
        int page = 1, 
        int perPage = 20,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (string.IsNullOrEmpty(SettingsService.Current.UnsplashApiKey))
            return [];
        
        EnsureAuthHeader();
        
        try
        {
            var url = $"{BaseUrl}/search/photos?query={Uri.EscapeDataString(query)}&page={page}&per_page={perPage}&orientation=landscape";
            
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
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
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (string.IsNullOrEmpty(SettingsService.Current.UnsplashApiKey))
            return [];
        
        EnsureAuthHeader();
        
        try
        {
            var url = $"{BaseUrl}/photos/random?count={Math.Min(count, 30)}&orientation=landscape";
            if (!string.IsNullOrEmpty(query))
                url = string.Concat(url, "&query=", Uri.EscapeDataString(query));
            
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
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
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        EnsureAuthHeader();
        
        try
        {
            // Notifier Unsplash du téléchargement (requis par leur API)
            if (!string.IsNullOrEmpty(photo.Links.DownloadLocation))
            {
                try 
                { 
                    using var _ = await _httpClient.GetAsync(photo.Links.DownloadLocation, cancellationToken).ConfigureAwait(false); 
                }
                catch { /* Ignorer les erreurs de tracking */ }
            }
            
            // Télécharger l'image
            var imageUrl = photo.Urls.Full;
            var fileName = $"unsplash_{photo.Id}.jpg";
            var filePath = Path.Combine(SettingsService.Current.WallpaperFolder, fileName);
            
            // Vérifier si déjà téléchargé
            if (File.Exists(filePath))
            {
                progress?.Report(100);
                return filePath;
            }
            
            using var response = await _httpClient.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            
            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            
            // Utiliser ArrayPool pour éviter les allocations
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                var bytesRead = 0L;
                
                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var fileStream = new FileStream(
                    filePath, 
                    FileMode.Create, 
                    FileAccess.Write, 
                    FileShare.None, 
                    BufferSize, 
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                
                int read;
                while ((read = await contentStream.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    bytesRead += read;
                    
                    if (totalBytes > 0)
                    {
                        var percentage = (int)((bytesRead * 100) / totalBytes);
                        progress?.Report(percentage);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
            
            return filePath;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur téléchargement Unsplash HTTP: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur téléchargement Unsplash: {ex.Message}");
            return null;
        }
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
            Author = photo.User.Name,
            AuthorUrl = photo.User.Links.Html,
            Tags = photo.Tags.Select(t => t.Title).ToArray()
        };
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }
}
