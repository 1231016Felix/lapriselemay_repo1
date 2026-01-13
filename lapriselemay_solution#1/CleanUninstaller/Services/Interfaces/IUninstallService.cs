using CleanUninstaller.Models;

namespace CleanUninstaller.Services.Interfaces;

/// <summary>
/// Interface pour le service de désinstallation
/// </summary>
public interface IUninstallService
{
    Task<UninstallResult> UninstallAsync(
        InstalledProgram program,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<UninstallResult> UninstallSilentAsync(
        InstalledProgram program,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<UninstallResult> ForceUninstallAsync(
        InstalledProgram program,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<List<UninstallResult>> BatchUninstallAsync(
        IEnumerable<InstalledProgram> programs,
        bool silent = true,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<UninstallResult> UninstallProgramAsync(
        InstalledProgram program,
        bool silent = true,
        bool scanResiduals = true,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<bool> CreateRestorePointAsync(string description);

    Task<CleanupResult> CleanupResidualsAsync(
        IEnumerable<ResidualItem> residuals,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
