using QuickLauncher.Models;

namespace QuickLauncher.Services;

/// <summary>
/// Utilitaire de déduplication des résultats de recherche.
/// Centralise la logique partagée entre IndexingService et SearchService
/// pour éviter les incohérences.
/// </summary>
public static class DeduplicationHelper
{
    /// <summary>
    /// Retourne une catégorie de type pour la déduplication des résultats.
    /// Application, StoreApp, SystemControl et AppControl sont fusionnés car un même programme
    /// peut apparaître via shell:AppsFolder (StoreApp), un raccourci .lnk (Application),
    /// WindowsSettingsProvider (SystemControl) ou les commandes internes (AppControl).
    /// </summary>
    public static ResultType GetCategory(ResultType type) => type switch
    {
        ResultType.StoreApp => ResultType.Application,
        ResultType.SystemControl => ResultType.Application,
        ResultType.AppControl => ResultType.Application,
        _ => type
    };
}
