using System.Windows;
using System.Windows.Input;
using QuickLauncher.Services;

namespace QuickLauncher.Views;

/// <summary>
/// Widget de note flottant sur le bureau.
/// </summary>
public partial class NoteWidget : Window
{
    private readonly int _noteId;
    private readonly Action<int>? _onClose;
    
    public string NoteText
    {
        get => NoteContent.Text;
        set => NoteContent.Text = value;
    }
    
    public int NoteId => _noteId;
    
    public NoteWidget(int noteId, string content, Action<int>? onClose = null)
    {
        InitializeComponent();
        
        _noteId = noteId;
        _onClose = onClose;
        NoteContent.Text = content;
        
        // Positionner en bas à droite par défaut
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - 300;
        Top = workArea.Bottom - 150;
        
        // Attacher au bureau pour que la note reste sur le bureau
        DesktopAttachHelper.AttachToDesktop(this);
    }
    
    /// <summary>
    /// Permet de déplacer la fenêtre en cliquant sur la barre de titre ou le contenu.
    /// </summary>
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Ne pas drag si on clique sur un bouton
        if (e.Source is System.Windows.Controls.Button)
            return;
            
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
            // Sauvegarder la position après le déplacement
            Services.NoteWidgetService.Instance.SaveWidgetPosition(_noteId, Left, Top);
        }
    }
    
    /// <summary>
    /// Copie le contenu de la note dans le presse-papiers.
    /// </summary>
    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        System.Windows.Clipboard.SetText(NoteContent.Text);
    }
    
    /// <summary>
    /// Ferme le widget et supprime la note.
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // Empêcher la propagation
        _onClose?.Invoke(_noteId);
        Close();
    }
    
    /// <summary>
    /// Définit la position du widget.
    /// </summary>
    public void SetPosition(double left, double top)
    {
        Left = left;
        Top = top;
    }
}
