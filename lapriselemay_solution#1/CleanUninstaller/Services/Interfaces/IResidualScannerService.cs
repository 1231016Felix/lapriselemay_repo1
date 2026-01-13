using CleanUninstaller.Models;

namespace CleanUninstaller.Services.Interfaces;

/// <summary>
/// Interface pour le scanner de résidus
/// </summary>
public interface IResidualScannerService
{
    /// <summary>
    /// Scan complet des résidus pour un programme
    /// </summary>
    Task<List<ResidualItem>> ScanResidualsAsync(
        InstalledProgram program,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Alias pour ScanResidualsAsync
    /// </summary>
    Task<List<ResidualItem>> ScanAsync(
        InstalledProgram program,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
