using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WallpaperManager.Models;
using WallpaperManager.Native;
using WallpaperManager.Services;

namespace WallpaperManager.ViewModels;

public partial class MainViewModel
{
    // === PROPRIÉTÉS DYNAMIQUES AVANCÉES ===
    
    /// <summary>
    /// Indique si le wallpaper dynamique sélectionné est actuellement actif
    /// </summary>
    public bool IsSelectedDynamicActive => 
        App.IsInitialized && 
        SelectedDynamicWallpaper != null && 
        App.DynamicService.ActiveWallpaper?.Id == SelectedDynamicWallpaper.Id;
    
    /// <summary>
    /// ID du wallpaper dynamique actuellement actif
    /// </summary>
    public string? ActiveDynamicWallpaperId => 
        App.IsInitialized ? App.DynamicService.ActiveWallpaper?.Id : null;
    
    /// <summary>
    /// Heure de prévisualisation (slider)
    /// </summary>
    [ObservableProperty]
    private double _previewHour = DateTime.Now.Hour + DateTime.Now.Minute / 60.0;
    
    /// <summary>
    /// Modes disponibles pour les wallpapers dynamiques
    /// </summary>
    public DynamicMode[] DynamicModes { get; } = Enum.GetValues<DynamicMode>();
    
    /// <summary>
    /// Types de transition disponibles
    /// </summary>
    public DynamicTransitionType[] DynamicTransitionTypes { get; } = Enum.GetValues<DynamicTransitionType>();
    
    partial void OnSelectedDynamicWallpaperChanged(DynamicWallpaper? value)
    {
        OnPropertyChanged(nameof(IsSelectedDynamicActive));
    }
    
    // === COMMANDES VARIANTES ===
    
    /// <summary>
    /// Ajoute une nouvelle variante au wallpaper dynamique sélectionné
    /// </summary>
    [RelayCommand]
    private void AddVariant()
    {
        if (SelectedDynamicWallpaper == null) return;
        
        // Trouver une heure libre (essayer toutes les 3 heures)
        var existingHours = SelectedDynamicWallpaper.Variants.Select(v => v.StartTime.Hours).ToHashSet();
        var newHour = 12; // Par défaut midi
        
        for (int h = 0; h < 24; h += 3)
        {
            if (!existingHours.Contains(h))
            {
                newHour = h;
                break;
            }
        }
        
        var variant = SelectedDynamicWallpaper.AddVariant(TimeSpan.FromHours(newHour));
        
        SettingsService.MarkDirty();
        SettingsService.Save();
        
        StatusMessage = $"Nouvelle période ajoutée à {variant.StartTimeFormatted}";
    }
    
    /// <summary>
    /// Supprime une variante spécifique
    /// </summary>
    [RelayCommand]
    private void RemoveVariant(TimeVariant? variant)
    {
        if (variant == null || SelectedDynamicWallpaper == null) return;
        
        if (SelectedDynamicWallpaper.Variants.Count <= 1)
        {
            StatusMessage = "Impossible de supprimer la dernière période";
            return;
        }
        
        var label = variant.Label ?? variant.StartTimeFormatted;
        SelectedDynamicWallpaper.RemoveVariant(variant);
        
        SettingsService.MarkDirty();
        SettingsService.Save();
        
        // Rafraîchir si actif
        if (IsSelectedDynamicActive)
        {
            App.DynamicService.Refresh();
        }
        
        StatusMessage = $"Période '{label}' supprimée";
    }
    
    /// <summary>
    /// Efface l'image d'une variante
    /// </summary>
    [RelayCommand]
    private void ClearVariantImage(TimeVariant? variant)
    {
        if (variant == null || SelectedDynamicWallpaper == null) return;
        
        variant.FilePath = string.Empty;
        
        SettingsService.MarkDirty();
        SettingsService.Save();
        
        StatusMessage = $"Image effacée pour {variant.Label ?? variant.StartTimeFormatted}";
    }
    
    // === SÉLECTION DEPUIS LA BIBLIOTHÈQUE ===
    
    /// <summary>
    /// Permet de sélectionner une image depuis la bibliothèque existante
    /// </summary>
    [RelayCommand]
    private void SetVariantFromLibrary(TimeVariant? variant)
    {
        if (variant == null || SelectedDynamicWallpaper == null) return;
        
        // Si un wallpaper est sélectionné dans la bibliothèque, l'utiliser
        if (SelectedWallpaper != null && SelectedWallpaper.Type == WallpaperType.Static)
        {
            var index = SelectedDynamicWallpaper.Variants.IndexOf(variant);
            if (index >= 0)
            {
                SelectedDynamicWallpaper.Variants[index].FilePath = SelectedWallpaper.FilePath;
            }
            
            SettingsService.MarkDirty();
            SettingsService.Save();
            
            if (IsSelectedDynamicActive)
            {
                App.DynamicService.Refresh();
            }
            
            StatusMessage = $"Image '{SelectedWallpaper.DisplayName}' assignée à {variant.Label ?? variant.StartTimeFormatted}";
        }
        else
        {
            StatusMessage = "Sélectionnez d'abord une image statique dans la bibliothèque";
        }
    }
    
    // === DUPLICATION ===
    
    /// <summary>
    /// Duplique le wallpaper dynamique sélectionné
    /// </summary>
    [RelayCommand]
    private void DuplicateDynamicWallpaper()
    {
        if (SelectedDynamicWallpaper == null) return;
        
        var clone = SelectedDynamicWallpaper.Clone();
        
        SettingsService.AddDynamicWallpaper(clone);
        DynamicWallpapers.Add(clone);
        SelectedDynamicWallpaper = clone;
        SettingsService.Save();
        
        StatusMessage = $"'{clone.Name}' créé";
    }
    
    // === PRÉVISUALISATION ===
    
    /// <summary>
    /// Prévisualise le wallpaper pour une heure spécifique
    /// </summary>
    [RelayCommand]
    private void PreviewAtTime()
    {
        if (SelectedDynamicWallpaper == null) return;
        
        var time = TimeSpan.FromHours(PreviewHour);
        var variant = SelectedDynamicWallpaper.GetVariantForTime(time);
        
        if (variant == null || !variant.Exists)
        {
            StatusMessage = $"Aucune image configurée pour {time.Hours:D2}:{time.Minutes:D2}";
            return;
        }
        
        try
        {
            WallpaperApi.SetWallpaper(variant.FilePath, WallpaperStyle.Fill);
            StatusMessage = $"Prévisualisation: {variant.Label ?? variant.StartTimeFormatted} ({time.Hours:D2}:{time.Minutes:D2})";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur: {ex.Message}";
        }
    }
    
    /// <summary>
    /// Prévisualise une variante spécifique
    /// </summary>
    [RelayCommand]
    private void PreviewVariant(TimeVariant? variant)
    {
        if (variant == null || !variant.Exists) return;
        
        try
        {
            WallpaperApi.SetWallpaper(variant.FilePath, WallpaperStyle.Fill);
            StatusMessage = $"Prévisualisation: {variant.Label ?? variant.StartTimeFormatted}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur: {ex.Message}";
        }
    }
    
    // === IMPORT/EXPORT ===
    
    /// <summary>
    /// Exporte le wallpaper dynamique sélectionné
    /// </summary>
    [RelayCommand]
    private async Task ExportDynamicWallpaperAsync()
    {
        if (SelectedDynamicWallpaper == null) return;
        
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Dynamic Wallpaper Pack|*.dwp|JSON|*.json",
            FileName = $"{SelectedDynamicWallpaper.Name}.dwp",
            Title = "Exporter le wallpaper dynamique"
        };
        
        if (dialog.ShowDialog() != true) return;
        
        IsLoading = true;
        StatusMessage = "Export en cours...";
        
        try
        {
            var export = new DynamicWallpaperExport
            {
                Name = SelectedDynamicWallpaper.Name,
                Description = SelectedDynamicWallpaper.Description,
                Mode = SelectedDynamicWallpaper.Mode,
                TransitionType = SelectedDynamicWallpaper.TransitionType,
                TransitionDuration = SelectedDynamicWallpaper.TransitionDuration,
                Latitude = SelectedDynamicWallpaper.Latitude,
                Longitude = SelectedDynamicWallpaper.Longitude
            };
            
            foreach (var variant in SelectedDynamicWallpaper.Variants)
            {
                var variantExport = new TimeVariantExport
                {
                    StartTime = variant.StartTime,
                    Label = variant.Label,
                    FileName = variant.FileName
                };
                
                // Inclure les données de l'image si elle existe
                if (variant.Exists)
                {
                    variantExport.ImageData = await File.ReadAllBytesAsync(variant.FilePath);
                }
                
                export.Variants.Add(variantExport);
            }
            
            var pack = new DynamicWallpaperPack
            {
                Author = Environment.UserName,
                Wallpapers = [export]
            };
            
            var json = JsonSerializer.Serialize(pack, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(dialog.FileName, json);
            
            StatusMessage = $"Exporté vers {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur d'export: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    /// <summary>
    /// Importe un wallpaper dynamique depuis un fichier
    /// </summary>
    [RelayCommand]
    private async Task ImportDynamicWallpaperAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Dynamic Wallpaper Pack|*.dwp|JSON|*.json|Tous|*.*",
            Title = "Importer un wallpaper dynamique"
        };
        
        if (dialog.ShowDialog() != true) return;
        
        IsLoading = true;
        StatusMessage = "Import en cours...";
        
        try
        {
            var json = await File.ReadAllTextAsync(dialog.FileName);
            var pack = JsonSerializer.Deserialize<DynamicWallpaperPack>(json);
            
            if (pack?.Wallpapers == null || pack.Wallpapers.Count == 0)
            {
                StatusMessage = "Fichier invalide ou vide";
                return;
            }
            
            var importFolder = Path.Combine(SettingsService.Current.WallpaperFolder, "DynamicImport");
            if (!Directory.Exists(importFolder))
                Directory.CreateDirectory(importFolder);
            
            var importedCount = 0;
            
            foreach (var export in pack.Wallpapers)
            {
                var dynamic = new DynamicWallpaper
                {
                    Name = $"{export.Name} (importé)",
                    Description = export.Description,
                    Mode = export.Mode,
                    TransitionType = export.TransitionType,
                    TransitionDuration = export.TransitionDuration,
                    Latitude = export.Latitude,
                    Longitude = export.Longitude
                };
                
                foreach (var variantExport in export.Variants)
                {
                    var variant = new TimeVariant
                    {
                        StartTime = variantExport.StartTime,
                        Label = variantExport.Label
                    };
                    
                    // Sauvegarder l'image si présente
                    if (variantExport.ImageData != null && !string.IsNullOrEmpty(variantExport.FileName))
                    {
                        var targetPath = Path.Combine(importFolder, $"{dynamic.Id}_{variantExport.FileName}");
                        await File.WriteAllBytesAsync(targetPath, variantExport.ImageData);
                        variant.FilePath = targetPath;
                    }
                    
                    dynamic.Variants.Add(variant);
                }
                
                SettingsService.AddDynamicWallpaper(dynamic);
                DynamicWallpapers.Add(dynamic);
                importedCount++;
            }
            
            SettingsService.Save();
            StatusMessage = $"{importedCount} wallpaper(s) dynamique(s) importé(s)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur d'import: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    // === MODE SOLEIL ===
    
    /// <summary>
    /// Crée un wallpaper dynamique basé sur le soleil
    /// </summary>
    [RelayCommand]
    private void CreateSunBasedDynamic()
    {
        var dynamic = new DynamicWallpaper
        {
            Name = $"Wallpaper Soleil {DynamicWallpapers.Count + 1}",
            Mode = DynamicMode.SunBased,
            Description = "Change selon le lever/coucher du soleil"
        };
        
        // Utiliser SunCalculatorService pour des calculs précis
        var sunTimes = SunCalculatorService.CalculateToday(dynamic.Latitude, dynamic.Longitude);
        
        // Créer les variantes basées sur le soleil avec des labels cohérents
        dynamic.Variants.Add(new TimeVariant { StartTime = sunTimes.Dawn, Label = "Aube" });
        dynamic.Variants.Add(new TimeVariant { StartTime = sunTimes.Sunrise, Label = "Lever" });
        dynamic.Variants.Add(new TimeVariant { StartTime = sunTimes.SolarNoon, Label = "Midi" });
        dynamic.Variants.Add(new TimeVariant { StartTime = sunTimes.Sunset, Label = "Coucher" });
        dynamic.Variants.Add(new TimeVariant { StartTime = sunTimes.Dusk, Label = "Crépuscule" });
        dynamic.Variants.Add(new TimeVariant { StartTime = sunTimes.Dusk.Add(TimeSpan.FromHours(1)), Label = "Nuit" });
        
        SettingsService.AddDynamicWallpaper(dynamic);
        DynamicWallpapers.Add(dynamic);
        SelectedDynamicWallpaper = dynamic;
        SettingsService.Save();
        
        StatusMessage = $"Wallpaper soleil créé - Lever: {sunTimes.Sunrise:hh\\:mm}, Coucher: {sunTimes.Sunset:hh\\:mm}";
    }
    
    /// <summary>
    /// Recalcule les heures du soleil pour le wallpaper sélectionné
    /// </summary>
    [RelayCommand]
    private void RecalculateSunTimes()
    {
        if (SelectedDynamicWallpaper == null || SelectedDynamicWallpaper.Mode != DynamicMode.SunBased)
        {
            StatusMessage = "Sélectionnez un wallpaper en mode Soleil";
            return;
        }
        
        // Utiliser SunCalculatorService pour des calculs précis
        var sunTimes = SunCalculatorService.CalculateToday(
            SelectedDynamicWallpaper.Latitude, 
            SelectedDynamicWallpaper.Longitude);
        
        // Mettre à jour les variantes selon leur label (même mapping que DynamicWallpaperService)
        foreach (var variant in SelectedDynamicWallpaper.Variants)
        {
            variant.StartTime = variant.Label?.ToLowerInvariant() switch
            {
                "aube" or "dawn" => sunTimes.Dawn,
                "lever" or "lever du soleil" or "sunrise" => sunTimes.Sunrise,
                "midi" or "noon" => sunTimes.SolarNoon,
                "heure dorée" or "golden hour" => sunTimes.GoldenHourPM,
                "coucher" or "coucher du soleil" or "sunset" => sunTimes.Sunset,
                "crépuscule" or "dusk" => sunTimes.Dusk,
                "nuit" or "night" => sunTimes.Dusk.Add(TimeSpan.FromHours(1)),
                _ => variant.StartTime
            };
        }
        
        SelectedDynamicWallpaper.SortVariants();
        SettingsService.MarkDirty();
        SettingsService.Save();
        
        if (IsSelectedDynamicActive)
        {
            App.DynamicService.Refresh();
        }
        
        StatusMessage = $"Heures recalculées - Lever: {sunTimes.Sunrise:hh\\:mm}, Coucher: {sunTimes.Sunset:hh\\:mm}";
    }
    // === RAFRAÎCHISSEMENT UI ===
    
    /// <summary>
    /// Rafraîchit l'indicateur de wallpaper actif
    /// </summary>
    public void RefreshDynamicActiveState()
    {
        OnPropertyChanged(nameof(IsSelectedDynamicActive));
        OnPropertyChanged(nameof(ActiveDynamicWallpaperId));
    }
    
    /// <summary>
    /// Vérifie si un wallpaper dynamique spécifique est actif
    /// </summary>
    public bool IsDynamicWallpaperActive(string id)
    {
        return App.IsInitialized && App.DynamicService.ActiveWallpaper?.Id == id;
    }
}
