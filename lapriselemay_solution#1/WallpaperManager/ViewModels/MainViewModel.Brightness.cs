using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WallpaperManager.Models;
using WallpaperManager.Services;

namespace WallpaperManager.ViewModels;

/// <summary>
/// Partie du MainViewModel dédiée à l'analyse de luminosité et rotation intelligente.
/// </summary>
public partial class MainViewModel
{
    // Collections système de luminosité
    private static readonly string DarkCollectionId = SystemCollectionIds.Dark;
    private static readonly string LightCollectionId = SystemCollectionIds.Light;
    private static readonly string NeutralCollectionId = SystemCollectionIds.Neutral;
    private static readonly string AnimatedCollectionId = SystemCollectionIds.Animated;
    
    private readonly Collection _darkCollection = new()
    {
        Id = DarkCollectionId,
        Name = "Sombres",
        Icon = "🌙"
    };
    
    private readonly Collection _lightCollection = new()
    {
        Id = LightCollectionId,
        Name = "Clairs",
        Icon = "☀️"
    };
    
    private readonly Collection _neutralCollection = new()
    {
        Id = NeutralCollectionId,
        Name = "Neutres",
        Icon = "⚖️"
    };
    
    private readonly Collection _animatedCollection = new()
    {
        Id = AnimatedCollectionId,
        Name = "Animés",
        Icon = "🎬"
    };
    
    private SmartRotationService? _smartRotationService;
    
    [ObservableProperty]
    private bool _isAnalyzingBrightness;
    
    [ObservableProperty]
    private int _brightnessAnalysisProgress;
    
    [ObservableProperty]
    private string _brightnessAnalysisStatus = string.Empty;
    
    [ObservableProperty]
    private bool _smartRotationEnabled;
    
    [ObservableProperty]
    private string _currentPeriodName = string.Empty;
    
    [ObservableProperty]
    private int _darkCount;
    
    [ObservableProperty]
    private int _lightCount;
    
    [ObservableProperty]
    private int _neutralCount;
    
    [ObservableProperty]
    private int _animatedCount;
    
    [ObservableProperty]
    private int _unanalyzedCount;
    
    // Paramètres horaires (format HH:mm pour l'UI)
    [ObservableProperty]
    private string _dayStartTime = "07:00";
    
    [ObservableProperty]
    private string _eveningStartTime = "18:00";
    
    [ObservableProperty]
    private string _nightStartTime = "21:00";
    
    /// <summary>
    /// Initialise le service de rotation intelligente.
    /// </summary>
    private void InitializeSmartRotation()
    {
        _smartRotationService = new SmartRotationService(
            GetWallpapersByCategory,
            ApplyWallpaperDirect);
        
        // Charger les paramètres
        var settings = SettingsService.Current;
        SmartRotationEnabled = settings.SmartRotationEnabled;
        DayStartTime = settings.SmartRotationDayStart.ToString(@"hh\:mm");
        EveningStartTime = settings.SmartRotationEveningStart.ToString(@"hh\:mm");
        NightStartTime = settings.SmartRotationNightStart.ToString(@"hh\:mm");
        
        // Appliquer les paramètres au service
        UpdateSmartRotationSettings();
        
        // Mettre à jour les compteurs
        UpdateBrightnessCounters();
        
        // Écouter les changements de période
        _smartRotationService.PeriodChanged += OnPeriodChanged;
        
        // Afficher la période actuelle
        UpdateCurrentPeriodDisplay();
        
        // Démarrer si activé
        if (SmartRotationEnabled)
        {
            _smartRotationService.Start();
        }
    }
    
    /// <summary>
    /// Met à jour l'affichage de la période actuelle.
    /// </summary>
    private void UpdateCurrentPeriodDisplay()
    {
        if (_smartRotationService == null) return;
        
        var period = _smartRotationService.GetCurrentPeriod();
        var icon = SmartRotationService.GetPeriodIcon(period);
        var name = SmartRotationService.GetPeriodName(period);
        CurrentPeriodName = $"{icon} {name}";
    }
    
    private void OnPeriodChanged(object? sender, DayPeriod period)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            UpdateCurrentPeriodDisplay();
            var category = SmartRotationService.GetCategoryForPeriod(period);
            StatusMessage = $"Période changée: {SmartRotationService.GetPeriodName(period)} ({ImageBrightnessAnalyzer.GetCategoryName(category)})";
        });
    }
    
    partial void OnSmartRotationEnabledChanged(bool value)
    {
        if (_smartRotationService == null) return;
        
        SettingsService.Current.SmartRotationEnabled = value;
        SettingsService.Save();
        
        _smartRotationService.Settings.Enabled = value;
        
        if (value)
        {
            // Désactiver automatiquement la rotation automatique de la bibliothèque
            if (IsRotationEnabled)
            {
                IsRotationEnabled = false;
            }
            
            _smartRotationService.Start();
            StatusMessage = "Rotation intelligente activée (rotation automatique désactivée)";
        }
        else
        {
            _smartRotationService.Stop();
            StatusMessage = "Rotation intelligente désactivée";
        }
        
        // Notifier l'UI que l'état du toggle de rotation automatique peut avoir changé
        OnPropertyChanged(nameof(IsRotationToggleEnabled));
    }
    
    /// <summary>
    /// Indique si le toggle de rotation automatique peut être modifié.
    /// Désactivé quand la rotation intelligente est active.
    /// </summary>
    public bool IsRotationToggleEnabled => !SmartRotationEnabled;
    
    /// <summary>
    /// Met à jour les paramètres du service de rotation.
    /// </summary>
    private void UpdateSmartRotationSettings()
    {
        if (_smartRotationService == null) return;
        
        if (TimeSpan.TryParse(DayStartTime, out var dayStart))
            _smartRotationService.Settings.DayStartTime = dayStart;
        
        if (TimeSpan.TryParse(EveningStartTime, out var eveningStart))
            _smartRotationService.Settings.EveningStartTime = eveningStart;
        
        if (TimeSpan.TryParse(NightStartTime, out var nightStart))
            _smartRotationService.Settings.NightStartTime = nightStart;
        
        _smartRotationService.Settings.Enabled = SmartRotationEnabled;
        
        UpdateCurrentPeriodDisplay();
    }
    
    [RelayCommand]
    private void SaveSmartRotationTimes()
    {
        if (TimeSpan.TryParse(DayStartTime, out var dayStart))
            SettingsService.Current.SmartRotationDayStart = dayStart;
        
        if (TimeSpan.TryParse(EveningStartTime, out var eveningStart))
            SettingsService.Current.SmartRotationEveningStart = eveningStart;
        
        if (TimeSpan.TryParse(NightStartTime, out var nightStart))
            SettingsService.Current.SmartRotationNightStart = nightStart;
        
        SettingsService.Save();
        UpdateSmartRotationSettings();
        
        StatusMessage = "Horaires de rotation intelligente sauvegardés";
    }
    
    /// <summary>
    /// Met à jour les compteurs de luminosité.
    /// </summary>
    private void UpdateBrightnessCounters()
    {
        DarkCount = _allWallpapers.Count(w => w.BrightnessCategory == BrightnessCategory.Dark);
        LightCount = _allWallpapers.Count(w => w.BrightnessCategory == BrightnessCategory.Light);
        NeutralCount = _allWallpapers.Count(w => w.BrightnessCategory == BrightnessCategory.Neutral);
        AnimatedCount = _allWallpapers.Count(w => w.Type == WallpaperType.Animated || w.Type == WallpaperType.Video);
        // Non analysés = images statiques sans catégorie de luminosité
        UnanalyzedCount = _allWallpapers.Count(w => w.BrightnessCategory == null && w.Type == WallpaperType.Static);
        
        // Mettre à jour les collections virtuelles
        _darkCollection.WallpaperIds = _allWallpapers
            .Where(w => w.BrightnessCategory == BrightnessCategory.Dark)
            .Select(w => w.Id)
            .ToList();
        
        _lightCollection.WallpaperIds = _allWallpapers
            .Where(w => w.BrightnessCategory == BrightnessCategory.Light)
            .Select(w => w.Id)
            .ToList();
        
        _neutralCollection.WallpaperIds = _allWallpapers
            .Where(w => w.BrightnessCategory == BrightnessCategory.Neutral)
            .Select(w => w.Id)
            .ToList();
        
        _animatedCollection.WallpaperIds = _allWallpapers
            .Where(w => w.Type == WallpaperType.Animated || w.Type == WallpaperType.Video)
            .Select(w => w.Id)
            .ToList();
        
        // Rafraîchir l'affichage des collections de luminosité dans la liste
        RefreshBrightnessCollectionsDisplay();
    }
    
    /// <summary>
    /// Rafraîchit l'affichage des collections de luminosité dans la liste des collections.
    /// </summary>
    private void RefreshBrightnessCollectionsDisplay()
    {
        // Forcer le rafraîchissement des compteurs dans la liste
        foreach (var collection in new[] { _darkCollection, _lightCollection, _neutralCollection, _animatedCollection })
        {
            var index = Collections.IndexOf(collection);
            if (index >= 0)
            {
                Collections.RemoveAt(index);
                Collections.Insert(index, collection);
            }
        }
        
        // Si une collection de luminosité ou animée est sélectionnée, rafraîchir son contenu
        if (SelectedCollection != null && (SystemCollectionIds.IsBrightnessCollection(SelectedCollection.Id) || SelectedCollection.Id == AnimatedCollectionId))
        {
            RefreshCollectionWallpapers();
        }
    }
    
    /// <summary>
    /// Obtient les wallpapers d'une catégorie de luminosité.
    /// </summary>
    private List<Wallpaper> GetWallpapersByCategory(BrightnessCategory category)
    {
        return _allWallpapers.Where(w => w.BrightnessCategory == category).ToList();
    }
    
    /// <summary>
    /// Analyse la luminosité de tous les fonds d'écran non analysés.
    /// </summary>
    [RelayCommand]
    private async Task AnalyzeBrightnessAsync()
    {
        if (IsAnalyzingBrightness) return;
        
        var toAnalyze = _allWallpapers
            .Where(w => w.BrightnessCategory == null && w.Type == WallpaperType.Static && w.Exists)
            .ToList();
        
        if (toAnalyze.Count == 0)
        {
            StatusMessage = "Tous les fonds d'écran sont déjà analysés";
            return;
        }
        
        IsAnalyzingBrightness = true;
        BrightnessAnalysisProgress = 0;
        BrightnessAnalysisStatus = $"Analyse de {toAnalyze.Count} images...";
        
        try
        {
            var progress = new Progress<int>(p =>
            {
                BrightnessAnalysisProgress = p;
                BrightnessAnalysisStatus = $"Analyse en cours... {p}%";
            });
            
            var results = await ImageBrightnessAnalyzer.AnalyzeBatchAsync(
                toAnalyze.Select(w => w.FilePath),
                progress);
            
            // Appliquer les résultats
            var darkAdded = 0;
            var lightAdded = 0;
            var neutralAdded = 0;
            
            foreach (var wallpaper in toAnalyze)
            {
                if (results.TryGetValue(wallpaper.FilePath, out var result))
                {
                    // Mapper la catégorie du service vers le modèle
                    wallpaper.BrightnessCategory = result.Category;
                    wallpaper.AverageBrightness = result.AverageBrightness;
                    
                    switch (result.Category)
                    {
                        case BrightnessCategory.Dark:
                            darkAdded++;
                            break;
                        case BrightnessCategory.Light:
                            lightAdded++;
                            break;
                        case BrightnessCategory.Neutral:
                            neutralAdded++;
                            break;
                    }
                }
            }
            
            // Sauvegarder
            SettingsService.MarkDirty();
            SettingsService.Save();
            
            // Mettre à jour les compteurs
            UpdateBrightnessCounters();
            
            // Rafraîchir les collections si nécessaire
            if (SelectedCollection != null && SystemCollectionIds.IsBrightnessCollection(SelectedCollection.Id))
            {
                RefreshCollectionWallpapers();
            }
            
            BrightnessAnalysisStatus = "Analyse terminée!";
            StatusMessage = $"Analyse terminée: {darkAdded} sombres, {lightAdded} clairs, {neutralAdded} neutres";
        }
        catch (Exception ex)
        {
            BrightnessAnalysisStatus = $"Erreur: {ex.Message}";
            StatusMessage = $"Erreur lors de l'analyse: {ex.Message}";
        }
        finally
        {
            IsAnalyzingBrightness = false;
        }
    }
    
    /// <summary>
    /// Réanalyse tous les fonds d'écran (même ceux déjà analysés).
    /// </summary>
    [RelayCommand]
    private async Task ReanalyzeAllBrightnessAsync()
    {
        // Réinitialiser toutes les analyses
        foreach (var wallpaper in _allWallpapers)
        {
            wallpaper.BrightnessCategory = null;
            wallpaper.AverageBrightness = null;
        }
        
        UpdateBrightnessCounters();
        
        // Lancer l'analyse complète
        await AnalyzeBrightnessAsync();
    }
    
    /// <summary>
    /// Applique un fond d'écran aléatoire de la période actuelle.
    /// </summary>
    [RelayCommand]
    private void ApplyRandomFromCurrentPeriod()
    {
        _smartRotationService?.ApplyRandomFromCurrentPeriod();
    }
    
    /// <summary>
    /// Vérifie si la collection sélectionnée est une collection de luminosité.
    /// </summary>
    public bool IsSelectedCollectionBrightness => 
        SelectedCollection != null && SystemCollectionIds.IsBrightnessCollection(SelectedCollection.Id);
    
    /// <summary>
    /// Nettoie le service de rotation intelligente.
    /// </summary>
    private void CleanupSmartRotation()
    {
        if (_smartRotationService != null)
        {
            _smartRotationService.PeriodChanged -= OnPeriodChanged;
            _smartRotationService.Dispose();
            _smartRotationService = null;
        }
    }
    
    /// <summary>
    /// Analyse automatiquement les nouvelles images ajoutées.
    /// Appelé après l'ajout de nouveaux wallpapers.
    /// </summary>
    internal async Task AnalyzeNewWallpapersAsync(IEnumerable<Wallpaper> newWallpapers)
    {
        var toAnalyze = newWallpapers
            .Where(w => w.BrightnessCategory == null && w.Type == WallpaperType.Static && w.Exists)
            .ToList();
        
        if (toAnalyze.Count == 0) return;
        
        try
        {
            var results = await ImageBrightnessAnalyzer.AnalyzeBatchAsync(
                toAnalyze.Select(w => w.FilePath));
            
            foreach (var wallpaper in toAnalyze)
            {
                if (results.TryGetValue(wallpaper.FilePath, out var result))
                {
                    wallpaper.BrightnessCategory = result.Category;
                    wallpaper.AverageBrightness = result.AverageBrightness;
                }
            }
            
            // Sauvegarder et mettre à jour
            SettingsService.MarkDirty();
            SettingsService.Save();
            UpdateBrightnessCounters();
            
            System.Diagnostics.Debug.WriteLine($"Analyse automatique: {toAnalyze.Count} image(s) analysée(s)");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur analyse automatique: {ex.Message}");
        }
    }
}
