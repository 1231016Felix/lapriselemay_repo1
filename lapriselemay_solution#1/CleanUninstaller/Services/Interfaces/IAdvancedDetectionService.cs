using CleanUninstaller.Models;

namespace CleanUninstaller.Services.Interfaces;

/// <summary>
/// Interface pour le service de détection avancée
/// </summary>
public interface IAdvancedDetectionService
{
    Task<List<ResidualItem>> DeepScanAsync(
        InstalledProgram program,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
