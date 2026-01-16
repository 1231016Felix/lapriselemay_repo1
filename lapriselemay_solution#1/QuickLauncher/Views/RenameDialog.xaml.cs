using System.Windows;

namespace QuickLauncher.Views;

public partial class RenameDialog : Window
{
    public string NewName { get; private set; } = string.Empty;

    public RenameDialog(string currentName)
    {
        InitializeComponent();
        
        NameTextBox.Text = currentName;
        NewName = currentName;
        
        Loaded += (_, _) =>
        {
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
        
        NameTextBox.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        };
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        NewName = NameTextBox.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
