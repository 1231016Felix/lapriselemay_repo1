using CleanUninstaller.Models;

namespace CleanUninstaller.Services.Interfaces;

/// <summary>
/// Interface pour le scanner de programmes
/// </summary>
public interface IProgramScannerService
{
    Task<List<InstalledProgram>> ScanAllProgramsAsync(
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
