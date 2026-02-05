using System.Buffers;
using System.Runtime.CompilerServices;
using QuickLauncher.Models;

namespace QuickLauncher.Services;

/// <summary>
/// Algorithmes de recherche avancée pour QuickLauncher.
/// Implémente la distance de Levenshtein, le fuzzy matching amélioré,
/// et le scoring basé sur la fréquence d'utilisation.
/// 
/// Optimisations v2:
/// - Cache LRU au lieu de ConcurrentDictionary+Clear() brutal
/// - Levenshtein avec ArrayPool (zéro allocation sur le heap pour les petites chaînes)
/// - ReadOnlySpan&lt;char&gt; pour éviter les allocations de sous-chaînes
/// </summary>
public static class SearchAlgorithms
{
    /// <summary>
    /// Cache LRU thread-safe pour les calculs de distance de Levenshtein.
    /// Évince progressivement les entrées les moins utilisées au lieu de tout supprimer d'un coup.
    /// </summary>
    private static readonly LruCache<(string, string), int> _levenshteinCache = new(10_000);

    #region Levenshtein Distance

    /// <summary>
    /// Calcule la distance de Levenshtein entre deux chaînes.
    /// Utilise un cache LRU pour les résultats fréquents.
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
        
        // Vérifier le cache LRU
        var key = (s, t);
        if (_levenshteinCache.TryGet(key, out var cached))
            return cached;

        var result = ComputeLevenshteinDistance(s.AsSpan(), t.AsSpan());
        _levenshteinCache.Set(key, result);
        return result;
    }

    /// <summary>
    /// Implémentation optimisée de Levenshtein avec :
    /// - Span&lt;char&gt; pour éviter les copies
    /// - ArrayPool pour les tableaux temporaires (zéro allocation GC pour chaînes courtes)
    /// - Optimisations préfixe/suffixe commun
    /// </summary>
    internal static int ComputeLevenshteinDistance(ReadOnlySpan<char> source, ReadOnlySpan<char> target)
    {
        var sourceLength = source.Length;
        var targetLength = target.Length;

        if (sourceLength == 0) return targetLength;
        if (targetLength == 0) return sourceLength;
        if (source.SequenceEqual(target)) return 0;

        // Optimisation: préfixe commun
        var prefixLength = 0;
        var minLen = Math.Min(sourceLength, targetLength);
        while (prefixLength < minLen && source[prefixLength] == target[prefixLength])
            prefixLength++;

        // Optimisation: suffixe commun
        var suffixLength = 0;
        while (suffixLength < sourceLength - prefixLength && 
               suffixLength < targetLength - prefixLength &&
               source[sourceLength - 1 - suffixLength] == target[targetLength - 1 - suffixLength])
            suffixLength++;

        // Ajuster en découpant la zone utile
        var srcSlice = source.Slice(prefixLength, sourceLength - prefixLength - suffixLength);
        var tgtSlice = target.Slice(prefixLength, targetLength - prefixLength - suffixLength);
        
        var sLen = srcSlice.Length;
        var tLen = tgtSlice.Length;

        if (sLen == 0) return tLen;
        if (tLen == 0) return sLen;

        // Utiliser ArrayPool pour éviter les allocations GC sur le heap
        // 2 lignes de (tLen + 1) entiers chacune
        var poolSize = (tLen + 1) * 2;
        var pool = ArrayPool<int>.Shared.Rent(poolSize);
        
        try
        {
            var previousRow = pool.AsSpan(0, tLen + 1);
            var currentRow = pool.AsSpan(tLen + 1, tLen + 1);

            for (var j = 0; j <= tLen; j++)
                previousRow[j] = j;

            for (var i = 1; i <= sLen; i++)
            {
                currentRow[0] = i;
                var sourceChar = srcSlice[i - 1];

                for (var j = 1; j <= tLen; j++)
                {
                    var cost = sourceChar == tgtSlice[j - 1] ? 0 : 1;

                    currentRow[j] = Math.Min(
                        Math.Min(currentRow[j - 1] + 1, previousRow[j] + 1),
                        previousRow[j - 1] + cost);
                }

                // Swap les lignes (copie de référence via Span)
                var temp = previousRow;
                previousRow = currentRow;
                currentRow = temp;
            }

            return previousRow[tLen];
        }
        finally
        {
            ArrayPool<int>.Shared.Return(pool);
        }
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
    /// Dictionnaire d'abréviations communes pour améliorer la recherche.
    /// Permet de trouver "Visual Studio" en tapant "vs", etc.
    /// </summary>
    private static readonly Dictionary<string, string[]> CommonAbbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        // Développement
        ["vs"] = ["visual studio", "visual studio code"],
        ["vsc"] = ["visual studio code"],
        ["vscode"] = ["visual studio code"],
        ["code"] = ["visual studio code"],
        ["np"] = ["notepad"],
        ["npp"] = ["notepad++"],
        ["notepad"] = ["notepad++"],
        ["sublime"] = ["sublime text"],
        ["st"] = ["sublime text"],
        ["idea"] = ["intellij"],
        ["pycharm"] = ["pycharm"],
        ["rider"] = ["rider"],
        ["ws"] = ["webstorm"],
        
        // Navigateurs
        ["ff"] = ["firefox", "mozilla firefox"],
        ["chrome"] = ["google chrome", "chrome"],
        ["gc"] = ["google chrome"],
        ["edge"] = ["microsoft edge"],
        ["brave"] = ["brave browser"],
        ["opera"] = ["opera gx", "opera browser"],
        
        // Microsoft Office
        ["word"] = ["microsoft word"],
        ["excel"] = ["microsoft excel"],
        ["ppt"] = ["powerpoint", "microsoft powerpoint"],
        ["powerpoint"] = ["microsoft powerpoint"],
        ["outlook"] = ["microsoft outlook"],
        ["onenote"] = ["microsoft onenote"],
        ["teams"] = ["microsoft teams"],
        
        // Adobe
        ["ps"] = ["photoshop", "adobe photoshop"],
        ["ai"] = ["illustrator", "adobe illustrator"],
        ["ae"] = ["after effects", "adobe after effects"],
        ["pr"] = ["premiere", "premiere pro", "adobe premiere"],
        ["id"] = ["indesign", "adobe indesign"],
        ["lr"] = ["lightroom", "adobe lightroom"],
        ["xd"] = ["adobe xd"],
        ["acrobat"] = ["adobe acrobat"],
        
        // Utilitaires Windows
        ["cmd"] = ["command prompt", "invite de commandes", "cmd.exe"],
        ["wt"] = ["windows terminal", "terminal"],
        ["term"] = ["windows terminal", "terminal"],
        ["calc"] = ["calculator", "calculatrice"],
        ["paint"] = ["mspaint", "paint 3d"],
        ["explorer"] = ["file explorer", "explorateur de fichiers"],
        ["snip"] = ["snipping tool", "outil capture"],
        ["task"] = ["task manager", "gestionnaire des tâches", "taskmgr"],
        
        // Autres applications courantes
        ["vlc"] = ["vlc media player"],
        ["obs"] = ["obs studio"],
        ["discord"] = ["discord"],
        ["slack"] = ["slack"],
        ["zoom"] = ["zoom"],
        ["spotify"] = ["spotify"],
        ["steam"] = ["steam"],
        ["git"] = ["git bash", "github desktop"],
        ["gh"] = ["github desktop"],
        ["docker"] = ["docker desktop"],
        ["postman"] = ["postman"],
        ["insomnia"] = ["insomnia"],
        ["figma"] = ["figma"],
        ["notion"] = ["notion"],
        ["evernote"] = ["evernote"],
        ["7z"] = ["7-zip"],
        ["zip"] = ["7-zip", "winzip", "winrar"],
        ["rar"] = ["winrar"],
    };

    /// <summary>
    /// Effectue un fuzzy match amélioré avec scoring détaillé.
    /// Utilise les poids par défaut.
    /// </summary>
    public static int CalculateFuzzyScore(string query, string target, int useCount = 0)
    {
        return CalculateFuzzyScore(query, target, useCount, null, DefaultWeights);
    }
    
    /// <summary>
    /// Effectue un fuzzy match amélioré avec scoring détaillé et poids configurables.
    /// </summary>
    public static int CalculateFuzzyScore(string query, string target, int useCount, ScoringWeights weights)
    {
        return CalculateFuzzyScore(query, target, useCount, null, weights);
    }
    
    /// <summary>
    /// Effectue un fuzzy match amélioré avec scoring détaillé, poids configurables et recency decay.
    /// </summary>
    public static int CalculateFuzzyScore(string query, string target, int useCount, DateTime? lastUsed, ScoringWeights weights)
    {
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(target))
            return 0;
        
        weights ??= DefaultWeights;

        var queryLower = query.ToLowerInvariant();
        var targetLower = target.ToLowerInvariant();

        int score = 0;
        
        // 0. Vérifier les abréviations communes (priorité haute)
        if (CommonAbbreviations.TryGetValue(queryLower, out var possibleTargets))
        {
            foreach (var possibleTarget in possibleTargets)
            {
                if (targetLower.Contains(possibleTarget))
                {
                    score = weights.ExactMatch - 100;
                    break;
                }
            }
        }
        
        // Si correspondance d'abréviation trouvée, ajouter les bonus et retourner
        if (score > 0)
        {
            if (useCount > 0)
                score += Math.Min(useCount * weights.UsageBonusPerUse, weights.MaxUsageBonus);
            
            if (weights.EnableRecencyBonus && lastUsed.HasValue && lastUsed.Value > DateTime.MinValue)
            {
                var daysSinceLastUse = (DateTime.UtcNow - lastUsed.Value).TotalDays;
                score += Math.Max(0, weights.MaxRecencyBonus - (int)(daysSinceLastUse * weights.RecencyDecayPerDay));
            }
            return score;
        }

        // 1. Correspondance exacte (score maximum)
        if (targetLower == queryLower)
        {
            score = weights.ExactMatch;
        }
        // 2. Commence par la requête
        else if (targetLower.StartsWith(queryLower))
        {
            score = weights.StartsWith + (queryLower.Length * 10);
        }
        // 3. Contient la requête
        else if (targetLower.Contains(queryLower))
        {
            var index = targetLower.IndexOf(queryLower);
            score = weights.Contains - (index * 5);
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
                    if (wordSimilarity >= weights.FuzzyMatchThreshold + 0.1)
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
        
        // 8. Bonus de recency (items récemment utilisés remontent)
        if (score > 0 && weights.EnableRecencyBonus && lastUsed.HasValue && lastUsed.Value > DateTime.MinValue)
        {
            var daysSinceLastUse = (DateTime.UtcNow - lastUsed.Value).TotalDays;
            var recencyBonus = Math.Max(0, weights.MaxRecencyBonus - (int)(daysSinceLastUse * weights.RecencyDecayPerDay));
            score += recencyBonus;
        }

        // 9. Bonus pour les mots qui matchent exactement
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
    internal static bool MatchesInitials(string query, string target)
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
    /// Vérifie si la requête est une sous-séquence du texte cible avec poids configurables.
    /// Ex: "vsc" -> "Visual Studio Code"
    /// </summary>
    internal static bool IsSubsequenceMatch(string query, string target, ScoringWeights weights, out int matchScore)
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
                
                if (i == lastMatchIndex + 1)
                {
                    consecutiveMatches++;
                    matchScore += consecutiveMatches * weights.ConsecutiveMatchBonus;
                }
                else
                {
                    consecutiveMatches = 0;
                }
                
                if (i == 0 || !char.IsLetterOrDigit(target[i - 1]))
                {
                    matchScore += weights.WordBoundaryBonus;
                }
                
                lastMatchIndex = i;
            }
        }

        if (queryIndex == query.Length)
        {
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
