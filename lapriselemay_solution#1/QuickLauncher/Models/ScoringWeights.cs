namespace QuickLauncher.Models;

/// <summary>
/// Poids configurables pour l'algorithme de scoring de recherche.
/// </summary>
public sealed class ScoringWeights
{
    public int ExactMatch { get; set; } = 1000;
    public int StartsWith { get; set; } = 800;
    public int Contains { get; set; } = 600;
    public int InitialsMatch { get; set; } = 500;
    public int SubsequenceMatch { get; set; } = 300;
    public int FuzzyMatchMultiplier { get; set; } = 250;
    public int MaxUsageBonus { get; set; } = 500;
    public int UsageBonusPerUse { get; set; } = 50;
    public int ExactWordBonus { get; set; } = 50;
    public double FuzzyMatchThreshold { get; set; } = 0.6;
    public int ConsecutiveMatchBonus { get; set; } = 10;
    public int WordBoundaryBonus { get; set; } = 20;
    public int FuzzyPerWordMultiplier { get; set; } = 500;
    public bool EnableRecencyBonus { get; set; } = true;
    public int MaxRecencyBonus { get; set; } = 150;
    public int RecencyDecayPerDay { get; set; } = 5;
    public bool EnablePathFuzzyMatch { get; set; } = true;
    public int PathExactSegmentMatch { get; set; } = 200;
    public int PathAllWordsMatchBonus { get; set; } = 100;
    
    public ScoringWeights Clone() => (ScoringWeights)MemberwiseClone();
}