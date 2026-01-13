using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CleanUninstaller.Models;
using CleanUninstaller.Services.Interfaces;
using CleanUninstaller.Views;

namespace CleanUninstaller.Services;

/// <summary>
/// Implémentation du service de dialogues utilisant WinUI 3.
/// </summary>
public sealed class DialogService : IDialogService
{
    private readonly Func<XamlRoot?> _xamlRootProvider;

    /// <summary>
    /// Crée une instance du service de dialogues.
    /// </summary>
    /// <param name="xamlRootProvider">Fonction retournant le XamlRoot courant</param>
    public DialogService(Func<XamlRoot?> xamlRootProvider)
    {
        _xamlRootProvider = xamlRootProvider ?? throw new ArgumentNullException(nameof(xamlRootProvider));
    }

    /// <inheritdoc/>
    public async Task<(bool DeletionPerformed, List<ResidualItem> Residuals)> ShowResidualScanDialogAsync(InstalledProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        
        var xamlRoot = _xamlRootProvider();
        if (xamlRoot == null)
        {
            return (false, []);
        }

        var dialog = new ResidualScanDialog(program)
        {
            XamlRoot = xamlRoot
        };

        await dialog.ShowAsync();

        return (dialog.DeletionPerformed, dialog.Residuals.ToList());
    }

    /// <inheritdoc/>
    public async Task<bool> ShowConfirmationAsync(string title, string message, string primaryButton = "OK", string? secondaryButton = null)
    {
        var xamlRoot = _xamlRootProvider();
        if (xamlRoot == null)
        {
            return false;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = primaryButton,
            CloseButtonText = secondaryButton ?? "Annuler",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    /// <inheritdoc/>
    public async Task ShowInfoAsync(string title, string message)
    {
        var xamlRoot = _xamlRootProvider();
        if (xamlRoot == null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot
        };

        await dialog.ShowAsync();
    }

    /// <inheritdoc/>
    public async Task ShowErrorAsync(string title, string message)
    {
        var xamlRoot = _xamlRootProvider();
        if (xamlRoot == null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot
        };

        await dialog.ShowAsync();
    }
}
