using System.Collections;
using System.Diagnostics;
using System.Reflection;

namespace QuickLauncher.Services;

/// <summary>
/// Vérification par réflexion que Clone() produit une copie profonde correcte.
/// Actif uniquement en Debug — coût nul en Release.
/// 
/// Parcourt toutes les propriétés publiques de type référence (List, Dictionary, objets)
/// et vérifie que l'instance clonée ne partage aucune référence avec l'originale.
/// Cela attrape automatiquement les oublis quand une nouvelle propriété List/Dict
/// est ajoutée à une classe settings sans mettre à jour Clone().
/// </summary>
internal static class CloneVerifier
{
    /// <summary>
    /// Vérifie qu'aucune propriété référence n'est partagée entre original et clone.
    /// Ne fait rien en Release (conditionnel via [Conditional]).
    /// </summary>
    [Conditional("DEBUG")]
    public static void AssertDeepClone<T>(T original, T clone, string context = "") where T : class
    {
        if (ReferenceEquals(original, clone))
        {
            Debug.Fail($"[CloneVerifier] {typeof(T).Name}: Clone() a retourné la même instance ! {context}");
            return;
        }

        foreach (var prop in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead) continue;
            
            var propType = prop.PropertyType;

            // Ignorer les types valeur et les strings (immuables)
            if (propType.IsValueType || propType == typeof(string)) continue;

            object? origVal, cloneVal;
            try
            {
                origVal = prop.GetValue(original);
                cloneVal = prop.GetValue(clone);
            }
            catch
            {
                continue; // Propriété indexée ou inaccessible
            }

            // Les deux null → OK
            if (origVal == null && cloneVal == null) continue;

            // Un seul null → suspect mais pas forcément un bug
            if (origVal == null || cloneVal == null) continue;

            // Vérifier que les collections ne partagent pas la même référence
            if (origVal is IList || origVal is IDictionary || IsKnownMutableType(propType))
            {
                if (ReferenceEquals(origVal, cloneVal))
                {
                    Debug.Fail(
                        $"[CloneVerifier] {typeof(T).Name}.{prop.Name} ({propType.Name}): " +
                        $"référence partagée après Clone() ! La propriété doit être clonée. {context}");
                }
            }
        }
    }

    /// <summary>
    /// Types mutables connus dans le modèle settings qui doivent être clonés.
    /// </summary>
    private static bool IsKnownMutableType(Type type) =>
        type.IsGenericType && (
            type.GetGenericTypeDefinition() == typeof(List<>) ||
            type.GetGenericTypeDefinition() == typeof(Dictionary<,>));
}
