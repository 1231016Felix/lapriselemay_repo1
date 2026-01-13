using CleanUninstaller.Models;

namespace CleanUninstaller.Services.Interfaces;

/// <summary>
/// Interface pour le service de registre
/// </summary>
public interface IRegistryService
{
    Task<List<InstalledProgram>> GetInstalledProgramsAsync(
        IProgress<ScanProgress>? progress = null, 
        CancellationToken cancellationToken = default);
    
    bool KeyExists(string keyPath);
    void DeleteKey(string keyPath);
    
    Task<int> CalculateMissingSizesAsync(
        List<InstalledProgram> programs, 
        IProgress<ScanProgress>? progress = null, 
        CancellationToken cancellationToken = default);
}
