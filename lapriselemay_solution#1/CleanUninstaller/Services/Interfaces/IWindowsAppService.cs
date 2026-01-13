using CleanUninstaller.Models;

namespace CleanUninstaller.Services.Interfaces;

/// <summary>
/// Interface pour le service des apps Windows Store
/// </summary>
public interface IWindowsAppService
{
    Task<List<InstalledProgram>> GetStoreAppsAsync(CancellationToken cancellationToken = default);
    Task<bool> UninstallStoreAppAsync(string packageFullName, CancellationToken cancellationToken = default);
}
