using System.Buffers;
using System.IO;
using System.Net.Http;

namespace WallpaperManager.Services;

/// <summary>
/// Classe de base pour les services d'API d'images (Unsplash, Pexels, Pixabay).
/// Fournit la logique commune de téléchargement avec progression.
/// </summary>
public abstract class BaseImageApiService : IDisposable
{
    protected readonly HttpClient HttpClient;
    protected const int BufferSize = 81920; // 80KB buffer
    protected volatile bool Disposed;
    protected string? CachedApiKey;
    
    protected BaseImageApiService()
    {
        HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }
    
    /// <summary>
    /// Nom du service pour les logs et les noms de fichiers.
    /// </summary>
    protected abstract string ServiceName { get; }
    
    /// <summary>
    /// Vérifie si le service est configuré (clé API présente).
    /// </summary>
    public abstract bool IsConfigured { get; }
    
    /// <summary>
    /// Met à jour le header d'authentification si nécessaire.
    /// </summary>
    protected abstract void EnsureAuthHeader();
    
    /// <summary>
    /// Télécharge une image avec rapport de progression.
    /// </summary>
    /// <param name="imageUrl">URL de l'image à télécharger</param>
    /// <param name="photoId">ID unique de la photo pour le nom de fichier</param>
    /// <param name="progress">Rapport de progression (0-100)</param>
    /// <param name="cancellationToken">Token d'annulation</param>
    /// <returns>Chemin local du fichier téléchargé, ou null en cas d'erreur</returns>
    protected async Task<string?> DownloadImageAsync(
        string imageUrl,
        string photoId,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var fileName = $"{ServiceName.ToLowerInvariant()}_{photoId}.jpg";
        var filePath = Path.Combine(SettingsService.Current.WallpaperFolder, fileName);
        
        // Vérifier si déjà téléchargé
        if (File.Exists(filePath))
        {
            progress?.Report(100);
            return filePath;
        }
        
        // Créer le dossier si nécessaire
        var folder = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            Directory.CreateDirectory(folder);
        
        try
        {
            using var response = await HttpClient.GetAsync(
                imageUrl, 
                HttpCompletionOption.ResponseHeadersRead, 
                cancellationToken).ConfigureAwait(false);
            
            response.EnsureSuccessStatusCode();
            
            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            
            // Utiliser ArrayPool pour éviter les allocations
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                var bytesRead = 0L;
                
                await using var contentStream = await response.Content
                    .ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                    
                await using var fileStream = new FileStream(
                    filePath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    BufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                
                int read;
                while ((read = await contentStream.ReadAsync(
                    buffer.AsMemory(0, BufferSize), 
                    cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(
                        buffer.AsMemory(0, read), 
                        cancellationToken).ConfigureAwait(false);
                    
                    bytesRead += read;
                    
                    if (totalBytes > 0)
                    {
                        var percentage = (int)((bytesRead * 100) / totalBytes);
                        progress?.Report(percentage);
                    }
                }
                
                progress?.Report(100);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
            
            return filePath;
        }
        catch (OperationCanceledException)
        {
            // Nettoyer le fichier partiel en cas d'annulation
            TryDeleteFile(filePath);
            throw;
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur téléchargement {ServiceName} HTTP: {ex.Message}");
            TryDeleteFile(filePath);
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur téléchargement {ServiceName}: {ex.Message}");
            TryDeleteFile(filePath);
            return null;
        }
    }
    
    /// <summary>
    /// Tente de supprimer un fichier (pour nettoyer les téléchargements partiels).
    /// </summary>
    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch
        {
            // Ignorer les erreurs de suppression
        }
    }
    
    /// <summary>
    /// Vérifie que le service n'est pas disposé.
    /// </summary>
    protected void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
    }
    
    public virtual void Dispose()
    {
        if (Disposed) return;
        Disposed = true;
        HttpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
