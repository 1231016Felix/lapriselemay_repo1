using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CleanUninstaller.Models;
using System.Collections.ObjectModel;

namespace CleanUninstaller.Views;

public sealed partial class ChangesDetailDialog : ContentDialog
{
    private readonly MonitoredInstallation _installation;
    private readonly ObservableCollection<SystemChange> _filteredChanges;
    private SystemChangeCategory? _categoryFilter;
    private ChangeType? _changeTypeFilter;
    private string _searchText = "";

    public ChangesDetailDialog(MonitoredInstallation installation)
    {
        this.InitializeComponent();
        _installation = installation;
        _filteredChanges = new ObservableCollection<SystemChange>(installation.Changes);
        ChangesListView.ItemsSource = _filteredChanges;

        UpdateStatistics();
        UpdateSelection();

        // S'abonner aux changements de sélection
        foreach (var change in _installation.Changes)
        {
            change.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SystemChange.IsSelected))
                {
                    UpdateSelection();
                }
            };
        }
    }

    private void UpdateStatistics()
    {
        var stats = _installation.Statistics;
        FilesCountText.Text = (stats.FilesCreated + stats.FilesModified + stats.FoldersCreated).ToString();
        RegistryCountText.Text = (stats.RegistryKeysCreated + stats.RegistryValuesCreated + stats.RegistryValuesModified).ToString();
        ServicesCountText.Text = stats.ServicesCreated.ToString();
        OthersCountText.Text = (stats.ScheduledTasksCreated + stats.FirewallRulesCreated + 
                               stats.StartupEntriesCreated + stats.DriversCreated + 
                               stats.ComObjectsCreated + stats.FontsCreated + 
                               stats.ShellExtensionsCreated).ToString();
    }

    private void UpdateSelection()
    {
        var selected = _installation.Changes.Where(c => c.IsSelected).ToList();
        SelectedCountRun.Text = selected.Count.ToString();
        SelectedSizeRun.Text = FormatSize(selected.Sum(c => c.Size));
    }

    private void ApplyFilters()
    {
        _filteredChanges.Clear();

        var filtered = _installation.Changes.AsEnumerable();

        // Filtre par catégorie
        if (_categoryFilter.HasValue)
        {
            filtered = filtered.Where(c => c.Category == _categoryFilter.Value);
        }

        // Filtre par type de changement
        if (_changeTypeFilter.HasValue)
        {
            filtered = filtered.Where(c => c.ChangeType == _changeTypeFilter.Value);
        }

        // Filtre par recherche
        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            filtered = filtered.Where(c => 
                c.Path.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                c.Description.Contains(_searchText, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var change in filtered)
        {
            _filteredChanges.Add(change);
        }
    }

    private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryFilter.SelectedItem is ComboBoxItem item)
        {
            var tag = item.Tag?.ToString();
            _categoryFilter = string.IsNullOrEmpty(tag) 
                ? null 
                : Enum.Parse<SystemChangeCategory>(tag);
            ApplyFilters();
        }
    }

    private void ChangeTypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChangeTypeFilter.SelectedItem is ComboBoxItem item)
        {
            var tag = item.Tag?.ToString();
            _changeTypeFilter = string.IsNullOrEmpty(tag) 
                ? null 
                : Enum.Parse<ChangeType>(tag);
            ApplyFilters();
        }
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            _searchText = sender.Text;
            ApplyFilters();
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var change in _filteredChanges)
        {
            change.IsSelected = true;
        }
    }

    private void DeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var change in _filteredChanges)
        {
            change.IsSelected = false;
        }
    }

    private void SelectFilesOnly_Click(object sender, RoutedEventArgs e)
    {
        foreach (var change in _installation.Changes)
        {
            change.IsSelected = change.Category is SystemChangeCategory.File 
                               or SystemChangeCategory.Folder;
        }
    }

    private void SelectRegistryOnly_Click(object sender, RoutedEventArgs e)
    {
        foreach (var change in _installation.Changes)
        {
            change.IsSelected = change.Category is SystemChangeCategory.RegistryKey 
                               or SystemChangeCategory.RegistryValue;
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 o";

        string[] suffixes = ["o", "Ko", "Mo", "Go"];
        var i = 0;
        double size = bytes;

        while (size >= 1024 && i < suffixes.Length - 1)
        {
            size /= 1024;
            i++;
        }

        return $"{size:N1} {suffixes[i]}";
    }
}
