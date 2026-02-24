namespace QuickLauncher.Models;

/// <summary>
/// Poids configurables pour l'algorithme de scoring de recherche.
/// Centralise TOUS les seuils et multiplicateurs utilisés par SearchAlgorithms et SearchService.
/// Point #7 : extraction des magic numbers.
/// </summary>
public sealed class ScoringWeights
{
    // === Scores de base par type de match ===
    public int ExactMatch { get; set; } = 1000;
    public int StartsWith { get; set; } = 800;
    public int Contains { get; set; } = 600;
    public int InitialsMatch { get; set; } = 500;
    public int SubsequenceMatch { get; set; } = 300;
    
    // === Fuzzy matching ===
    public int FuzzyMatchMultiplier { get; set; } = 250;
    public double FuzzyMatchThreshold { get; set; } = 0.6;
    public int FuzzyPerWordMultiplier { get; set; } = 500;
    
    /// <summary>Similarité attribuée quand le mot cible commence par le mot requête (ex: "fire" → "firefox").</summary>
    public double WordPrefixSimilarity { get; set; } = 0.95;
    
    /// <summary>Similarité attribuée quand le mot requête commence par le mot cible (requête plus longue).</summary>
    public double WordReversePrefixSimilarity { get; set; } = 0.85;
    
    /// <summary>Facteur de distance max pour le fuzzy par mot (proportion de la longueur du mot requête).</summary>
    public double FuzzyWordMaxDistanceFactor { get; set; } = 0.35;
    
    /// <summary>Bonus quand tous les mots de la requête ont matché en fuzzy per-word.</summary>
    public int FuzzyPerWordAllMatchedBonus { get; set; } = 30;
    
    /// <summary>Bonus quand le nombre de mots requête ≥ nombre de mots cible (match complet).</summary>
    public int FuzzyPerWordFullCoverageBonus { get; set; } = 20;
    
    /// <summary>Multiplicateur appliqué au score fuzzy global quand on matche un mot individuel de la cible.</summary>
    public double FuzzyGlobalWordMultiplier { get; set; } = 0.85;
    
    // === Bonus ===
    public int MaxUsageBonus { get; set; } = 500;
    public int UsageBonusPerUse { get; set; } = 50;
    public int ExactWordBonus { get; set; } = 50;
    public int ConsecutiveMatchBonus { get; set; } = 10;
    public int WordBoundaryBonus { get; set; } = 20;
    
    // === Recency ===
    public bool EnableRecencyBonus { get; set; } = true;
    public int MaxRecencyBonus { get; set; } = 150;
    public int RecencyDecayPerDay { get; set; } = 5;
    
    // === Path matching ===
    public bool EnablePathFuzzyMatch { get; set; } = true;
    public int PathExactSegmentMatch { get; set; } = 200;
    public int PathAllWordsMatchBonus { get; set; } = 100;
    
    /// <summary>Seuil de similarité minimum pour un match fuzzy sur un segment de chemin.</summary>
    public double PathFuzzySegmentThreshold { get; set; } = 0.7;
    
    // === Description matching ===
    
    /// <summary>Score pour un match exact de la requête dans la description, à une frontière de mot.</summary>
    public int DescriptionExactAtBoundary { get; set; } = 350;
    
    /// <summary>Score pour un match exact de la requête dans la description, pas à une frontière.</summary>
    public int DescriptionExactInline { get; set; } = 300;
    
    /// <summary>Score quand tous les mots de la requête matchent dans la description.</summary>
    public int DescriptionAllWordsMatch { get; set; } = 250;
    
    /// <summary>Score de base pour un match partiel de mots dans la description.</summary>
    public int DescriptionPartialWordBase { get; set; } = 100;
    
    /// <summary>Bonus par mot matché dans la description.</summary>
    public int DescriptionPartialWordBonus { get; set; } = 50;
    
    /// <summary>Multiplicateur pour le score fuzzy sur un mot de la description.</summary>
    public int DescriptionFuzzyMultiplier { get; set; } = 200;
    
    /// <summary>Multiplicateur appliqué au score description quand nameScore == 0 (résultat trouvé uniquement via description).</summary>
    public double DescriptionOnlyMultiplier { get; set; } = 0.6;
    
    /// <summary>Multiplicateur additif quand le nom a déjà un score et la description ajoute un bonus.</summary>
    public double DescriptionAdditiveMultiplier { get; set; } = 0.15;
    
    public ScoringWeights Clone() => (ScoringWeights)MemberwiseClone();
}
