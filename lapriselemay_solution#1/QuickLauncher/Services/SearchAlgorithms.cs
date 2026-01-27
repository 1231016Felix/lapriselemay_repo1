using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using QuickLauncher.Models;

namespace QuickLauncher.Services;

/// <summary>
/// Algorithmes de recherche avancée pour QuickLauncher.
/// Implémente la distance de Levenshtein, le fuzzy matching amélioré,
/// et le scoring basé sur la fréquence d'utilisation.
/// </summary>
public static class SearchAlgorithms
{
    /// <summary>
    /// Cache pour les calculs de distance de Levenshtein
    /// </summary>
    private static readonly ConcurrentDictionary<(string, string), int> _levenshteinCache = new();
    
    /// <summary>
    /// Limite du cache pour éviter une utilisation mémoire excessive
    /// </summary>
    private const int MaxCacheSize = 10000;

    #region Levenshtein Distance

    /// <summary>
    /// Calcule la distance de Levenshtein entre deux chaînes.
    /// Utilise un cache pour les résultats fréquents.
    /// </summary>
    /// <param name="source">Chaîne source</param>
    /// <param name="target">Chaîne cible</param>
    /// <returns>Distance d'édition minimale</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source)) return target?.Length ?? 0;
        if (string.IsNullOrEmpty(target)) return source.Length;
        
        // Normaliser pour le cache
        var s = source.ToLowerInvariant();
        var t = target.ToLowerInvariant();
        
        // Vérifier le cache
        var key = (s, t);
        if (_levenshteinCache.TryGetValue(key, out var cached))
            return cached;

        // Nettoyage périodique du cache
        if (_levenshteinCache.Count > MaxCacheSize)
        {
            _levenshteinCache.Clear();
        }

        var result = ComputeLevenshteinDistance(s, t);
        _levenshteinCache.TryAdd(key, result);
        return result;
    }

    /// <summary>
    /// Implémentation optimisée de Levenshtein avec un seul tableau (O(n) espace)
    /// </summary>
    private static int ComputeLevenshteinDistance(string source, string target)
    {
        var sourceLength = source.Length;
        var targetLength = target.Length;

        // Optimisation: si une chaîne est vide
        if (sourceLength == 0) return targetLength;
        if (targetLength == 0) return sourceLength;

        // Optimisation: chaînes identiques
        if (source == target) return 0;

        // Optimisation: préfixe commun
        var prefixLength = 0;
        while (prefixLength < sourceLength && prefixLength < targetLength && 
               source[prefixLength] == target[prefixLength])
        {
            prefixLength++;
        }

        // Optimisation: suffixe commun
        var suffixLength = 0;
        while (suffixLength < sourceLength - prefixLength && 
               suffixLength < targetLength - prefixLength &&
               source[sourceLength - 1 - suffixLength] == target[targetLength - 1 - suffixLength])
        {
            suffixLength++;
        }

        // Ajuster les longueurs
        sourceLength -= prefixLength + suffixLength;
        targetLength -= prefixLength + suffixLength;

        if (sourceLength == 0) return targetLength;
        if (targetLength == 0) return sourceLength;

        // Algorithme optimisé avec un seul tableau
        var previousRow = new int[targetLength + 1];
        var currentRow = new int[targetLength + 1];

        for (var j = 0; j <= targetLength; j++)
            previousRow[j] = j;

        for (var i = 1; i <= sourceLength; i++)
        {
            currentRow[0] = i;
            var sourceChar = source[prefixLength + i - 1];

            for (var j = 1; j <= targetLength; j++)
            {
                var targetChar = target[prefixLength + j - 1];
                var cost = sourceChar == targetChar ? 0 : 1;

                currentRow[j] = Math.Min(
                    Math.Min(currentRow[j - 1] + 1, previousRow[j] + 1),
                    previousRow[j - 1] + cost);
            }

            (previousRow, currentRow) = (currentRow, previousRow);
        }

        return previousRow[targetLength];
    }

    /// <summary>
    /// Calcule la similarité entre deux chaînes (0.0 à 1.0)
    /// </summary>
    public static double LevenshteinSimilarity(string source, string target)
    {
        if (string.IsNullOrEmpty(source) && string.IsNullOrEmpty(target)) return 1.0;
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)) return 0.0;

        var distance = LevenshteinDistance(source, target);
        var maxLength = Math.Max(source.Length, target.Length);
        
        return 1.0 - (double)distance / maxLength;
    }

    #endregion

    #region Fuzzy Matching Amélioré
    
    /// <summary>
    /// Instance des poids par défaut utilisée lorsqu'aucun poids n'est fourni.
    /// </summary>
    private static readonly ScoringWeights DefaultWeights = new();

    /// <summary>
    /// Effectue un fuzzy match amélioré avec scoring détaillé.
    /// Utilise les poids par défaut.
    /// </summary>
    /// <param name="query">Requête de recherche</param>
    /// <param name="target">Texte cible</param>
    /// <param name="useCount">Nombre d'utilisations (pour le scoring)</param>
    /// <returns>Score de correspondance (0 = pas de match, plus c'est haut mieux c'est)</returns>
    public static int CalculateFuzzyScore(string query, string target, int useCount = 0)
    {
        return CalculateFuzzyScore(query, target, useCount, DefaultWeights);
    }
    
    /// <summary>
    /// Effectue un fuzzy match amélioré avec scoring détaillé et poids configurables.
    /// </summary>
    /// <param name="query">Requête de recherche</param>
    /// <param name="target">Texte cible</param>
    /// <param name="useCount">Nombre d'utilisations (pour le scoring)</param>
    /// <param name="weights">Poids de scoring configurables</param>
    /// <returns>Score de correspondance (0 = pas de match, plus c'est haut mieux c'est)</returns>
    public static int CalculateFuzzyScore(string query, string target, int useCount, ScoringWeights weights)
    {
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(target))
            return 0;
        
        // Utiliser les poids par défaut si null
        weights ??= DefaultWeights;

        var queryLower = query.ToLowerInvariant();
        var targetLower = target.ToLowerInvariant();

        // Score de base
        int score = 0;

        // 1. Correspondance exacte (score maximum)
        if (targetLower == queryLower)
        {
            score = weights.ExactMatch;
        }
        // 2. Commence par la requête
        else if (targetLower.StartsWith(queryLower))
        {
            score = weights.StartsWith + (queryLower.Length * 10); // Bonus pour les correspondances plus longues
        }
        // 3. Contient la requête
        else if (targetLower.Contains(queryLower))
        {
            var index = targetLower.IndexOf(queryLower);
            score = weights.Contains - (index * 5); // Pénalité si la correspondance est plus loin
        }
        // 4. Match par initiales (ex: "vs" -> "Visual Studio")
        else if (MatchesInitials(queryLower, targetLower))
        {
            score = weights.InitialsMatch;
        }
        // 5. Match par sous-séquence (tous les caractères présents dans l'ordre)
        else if (IsSubsequenceMatch(queryLower, targetLower, weights, out var matchScore))
        {
            score = weights.SubsequenceMatch + matchScore;
        }
        // 6. Similarité de Levenshtein (pour les fautes de frappe)
        else
        {
            var similarity = LevenshteinSimilarity(queryLower, targetLower);
            if (similarity >= weights.FuzzyMatchThreshold)
            {
                score = (int)(similarity * weights.FuzzyMatchMultiplier);
            }
            else if (queryLower.Length >= 3)
            {
                // Essayer avec des parties du mot
                var words = targetLower.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
                foreach (var word in words)
                {
                    var wordSimilarity = LevenshteinSimilarity(queryLower, word);
                    if (wordSimilarity >= weights.FuzzyMatchThreshold + 0.1) // Seuil légèrement plus élevé pour les mots partiels
                    {
                        score = Math.Max(score, (int)(wordSimilarity * weights.FuzzyMatchMultiplier * 0.8));
                    }
                }
            }
        }

        // 7. Bonus basé sur la fréquence d'utilisation
        if (score > 0 && useCount > 0)
        {
            score += Math.Min(useCount * weights.UsageBonusPerUse, weights.MaxUsageBonus);
        }

        // 8. Bonus pour les mots qui matchent exactement
        if (score > 0)
        {
            var queryWords = queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var targetWords = targetLower.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var qWord in queryWords)
            {
                if (targetWords.Any(tw => tw == qWord))
                {
                    score += weights.ExactWordBonus;
                }
            }
        }

        return score;
    }

    /// <summary>
    /// Vérifie si la requête correspond aux initiales du texte cible.
    /// Ex: "vs" -> "Visual Studio", "np" -> "Notepad++"
    /// </summary>
    private static bool MatchesInitials(string query, string target)
    {
        var words = target.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < query.Length) return false;

        // Initiales strictes
        var initials = string.Concat(words.Where(w => w.Length > 0).Select(w => char.ToLower(w[0])));
        if (initials.StartsWith(query)) return true;

        // Initiales avec majuscules internes (CamelCase)
        var camelInitials = new System.Text.StringBuilder();
        foreach (var word in words)
        {
            camelInitials.Append(char.ToLower(word[0]));
            for (int i = 1; i < word.Length; i++)
            {
                if (char.IsUpper(word[i]))
                    camelInitials.Append(char.ToLower(word[i]));
            }
        }
        
        return camelInitials.ToString().Contains(query);
    }

    /// <summary>
    /// Vérifie si la requête est une sous-séquence du texte cible.
    /// Ex: "vsc" -> "Visual Studio Code"
    /// Utilise les poids par défaut.
    /// </summary>
    private static bool IsSubsequenceMatch(string query, string target, out int matchScore)
    {
        return IsSubsequenceMatch(query, target, DefaultWeights, out matchScore);
    }
    
    /// <summary>
    /// Vérifie si la requête est une sous-séquence du texte cible avec poids configurables.
    /// Ex: "vsc" -> "Visual Studio Code"
    /// </summary>
    private static bool IsSubsequenceMatch(string query, string target, ScoringWeights weights, out int matchScore)
    {
        matchScore = 0;
        var queryIndex = 0;
        var consecutiveMatches = 0;
        var lastMatchIndex = -2;

        for (var i = 0; i < target.Length && queryIndex < query.Length; i++)
        {
            if (target[i] == query[queryIndex])
            {
                queryIndex++;
                
                // Bonus pour les correspondances consécutives
                if (i == lastMatchIndex + 1)
                {
                    consecutiveMatches++;
                    matchScore += consecutiveMatches * weights.ConsecutiveMatchBonus;
                }
                else
                {
                    consecutiveMatches = 0;
                }
                
                // Bonus si le match est au début d'un mot
                if (i == 0 || !char.IsLetterOrDigit(target[i - 1]))
                {
                    matchScore += weights.WordBoundaryBonus;
                }
                
                lastMatchIndex = i;
            }
        }

        if (queryIndex == query.Length)
        {
            // Bonus basé sur la compacité du match
            var spread = lastMatchIndex - (target.Length - query.Length);
            matchScore += Math.Max(0, 50 - spread);
            return true;
        }

        matchScore = 0;
        return false;
    }

    #endregion

    #region Utilitaires

    /// <summary>
    /// Vide le cache de Levenshtein
    /// </summary>
    public static void ClearCache()
    {
        _levenshteinCache.Clear();
    }

    /// <summary>
    /// Nombre d'entrées dans le cache
    /// </summary>
    public static int CacheCount => _levenshteinCache.Count;

    #endregion
}
