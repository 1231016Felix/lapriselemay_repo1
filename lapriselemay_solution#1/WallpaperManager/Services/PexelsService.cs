using System.Buffers;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using WallpaperManager.Models;

namespace WallpaperManager.Services;

/// <summary>
/// Service pour l'API Pexels.
/// API gratuite : 200 requêtes/heure, 20 000/mois.
/// </summary>
public sealed class PexelsService : IDisposable
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "https://api.pexels.com/v1";
    private const int BufferSize = 81920;
    private volatile bool _disposed;
    private string? _cachedApiKey;
    
    public PexelsService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }
    
    private void EnsureAuthHeader()
    {
        var apiKey = SettingsService.Current.PexelsApiKey;
        if (string.IsNullOrEmpty(apiKey)) return;
        
        if (_cachedApiKey == apiKey) return;
        
        _cachedApiKey = apiKey;
        _httpClient.DefaultRequestHeaders.Remove("Authorization");
        _httpClient.DefaultRequestHeaders.Add("Authorization", apiKey);
    }
    
    public bool IsConfigured => !string.IsNullOrEmpty(SettingsService.Current.PexelsApiKey);
    
    public async Task<List<PexelsPhoto>> SearchPhotosAsync(
        string query,
        int page = 1,
        int perPage = 20,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (!IsConfigured) return [];
        
        EnsureAuthHeader();
        
        try
        {
            var url = $"{BaseUrl}/search?query={Uri.EscapeDataString(query)}&page={page}&per_page={perPage}&orientation=landscape";
            
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
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
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (!IsConfigured) return [];
        
        EnsureAuthHeader();
        
        try
        {
            var url = $"{BaseUrl}/curated?page={page}&per_page={perPage}";
            
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
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
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        try
        {
            var imageUrl = photo.Src.Original;
            var fileName = $"pexels_{photo.Id}.jpg";
            var filePath = Path.Combine(SettingsService.Current.WallpaperFolder, fileName);
            
            if (File.Exists(filePath))
            {
                progress?.Report(100);
                return filePath;
            }
            
            // Créer le dossier si nécessaire
            var folder = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                Directory.CreateDirectory(folder);
            
            using var response = await _httpClient.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            
            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            
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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur téléchargement Pexels: {ex.Message}");
            return null;
        }
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
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }
}
