using CleanUninstaller.Models;

namespace CleanUninstaller.Services.Interfaces;

/// <summary>
/// Interface pour le moniteur d'installation
/// </summary>
public interface IInstallationMonitorService
{
    bool IsMonitoring { get; }
    void StartMonitoring();
    void StopMonitoring();
    
    Task<InstallationSnapshot> TakeSnapshotAsync(CancellationToken cancellationToken = default);
    
    Task<List<SystemChange>> CompareSnapshotsAsync(
        InstallationSnapshot before,
        InstallationSnapshot after,
        CancellationToken cancellationToken = default);
}
