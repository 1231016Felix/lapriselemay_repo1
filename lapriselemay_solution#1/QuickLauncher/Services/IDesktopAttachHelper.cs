using System.Windows;

namespace QuickLauncher.Services;

/// <summary>
/// Abstraction pour l'attachement de fenêtres WPF au bureau Windows.
/// Les fenêtres attachées restent visibles sur le bureau comme des gadgets
/// et suivent les transitions Show Desktop / restauration.
/// </summary>
public interface IDesktopAttachHelper : IDisposable
{
    /// <summary>
    /// Attache une fenêtre WPF au bureau Windows.
    /// La fenêtre sera visible sur le bureau et se comportera comme un gadget.
    /// </summary>
    void AttachToDesktop(Window window);

    /// <summary>
    /// Force l'affichage de tous les widgets attachés.
    /// </summary>
    void ShowAllWidgets();
}
