using System.Buffers;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using WallpaperManager.Models;

namespace WallpaperManager.Services;

/// <summary>
/// Service pour l'API Pixabay.
/// API gratuite : 100 requêtes/minute.
/// </summary>
public sealed class PixabayService : IDisposable
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "https://pixabay.com/api";
    private const int BufferSize = 81920;
    private volatile bool _disposed;
    
    public PixabayService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }
    
    public bool IsConfigured => !string.IsNullOrEmpty(SettingsService.Current.PixabayApiKey);
    
    public async Task<List<PixabayPhoto>> SearchPhotosAsync(
        string query,
        int page = 1,
        int perPage = 20,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (!IsConfigured) return [];
        
        try
        {
            var apiKey = SettingsService.Current.PixabayApiKey;
            var url = $"{BaseUrl}/?key={apiKey}&q={Uri.EscapeDataString(query)}&page={page}&per_page={perPage}&orientation=horizontal&image_type=photo&min_width=1920&safesearch=true";
            
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
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
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (!IsConfigured) return [];
        
        try
        {
            var apiKey = SettingsService.Current.PixabayApiKey;
            var url = $"{BaseUrl}/?key={apiKey}&page={page}&per_page={perPage}&orientation=horizontal&image_type=photo&min_width=1920&order=popular&safesearch=true";
            
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
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
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        try
        {
            var imageUrl = photo.BestUrl;
            var extension = Path.GetExtension(new Uri(imageUrl).AbsolutePath);
            if (string.IsNullOrEmpty(extension)) extension = ".jpg";
            
            var fileName = $"pixabay_{photo.Id}{extension}";
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
            System.Diagnostics.Debug.WriteLine($"Erreur téléchargement Pixabay: {ex.Message}");
            return null;
        }
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
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }
}
