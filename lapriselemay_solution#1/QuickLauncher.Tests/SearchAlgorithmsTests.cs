using FluentAssertions;
using QuickLauncher.Models;
using QuickLauncher.Services;

namespace QuickLauncher.Tests;

/// <summary>
/// Tests paramétrés pour les algorithmes de recherche.
/// Couvre : Damerau-Levenshtein, fuzzy scoring, initiales, sous-séquences, path matching.
/// </summary>
public sealed class SearchAlgorithmsTests
{
    // ══════════════════════════════════════════════════════════
    //  Damerau-Levenshtein Distance
    // ══════════════════════════════════════════════════════════
    
    [Theory]
    [InlineData("", "", 0)]
    [InlineData("abc", "", 3)]
    [InlineData("", "abc", 3)]
    [InlineData("abc", "abc", 0)]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("firefox", "firfox", 1)]   // Transposition : ir → ri
    [InlineData("ab", "ba", 1)]             // Transposition simple
    [InlineData("abc", "bac", 1)]           // Transposition au début
    [InlineData("abc", "acb", 1)]           // Transposition à la fin
    public void DamerauLevenshteinDistance_ReturnsExpected(string source, string target, int expected)
    {
        SearchAlgorithms.DamerauLevenshteinDistance(source, target)
            .Should().Be(expected);
    }
    
    [Fact]
    public void DamerauLevenshteinDistance_IsSymmetric()
    {
        SearchAlgorithms.DamerauLevenshteinDistance("firefox", "firfox")
            .Should().Be(SearchAlgorithms.DamerauLevenshteinDistance("firfox", "firefox"));
    }
    
    [Fact]
    public void DamerauLevenshteinDistance_IsCaseInsensitive()
    {
        SearchAlgorithms.DamerauLevenshteinDistance("Firefox", "firefox")
            .Should().Be(0);
    }
    
    // ══════════════════════════════════════════════════════════
    //  Levenshtein Similarity
    // ══════════════════════════════════════════════════════════
    
    [Theory]
    [InlineData("abc", "abc", 1.0)]
    [InlineData("abc", "abd", 0.666)]  // Distance 1 / max 3
    [InlineData("", "", 1.0)]
    [InlineData("", "abc", 0.0)]
    public void LevenshteinSimilarity_ReturnsExpectedRange(string a, string b, double minExpected)
    {
        SearchAlgorithms.LevenshteinSimilarity(a, b)
            .Should().BeGreaterThanOrEqualTo(minExpected - 0.01);
    }
    
    // ══════════════════════════════════════════════════════════
    //  Fuzzy Score — Correspondances principales
    // ══════════════════════════════════════════════════════════
    
    [Theory]
    [InlineData("visual studio", "Visual Studio", 1000)]         // Exact match
    [InlineData("Visual Studio", "Visual Studio Code", 800)]     // StartsWith ≥ 800
    [InlineData("studio", "Visual Studio", 600)]                 // Contains ≥ 600
    [InlineData("vs", "Visual Studio", 500)]                     // Initials ≥ 500
    public void CalculateFuzzyScore_MatchHierarchy(string query, string target, int minScore)
    {
        SearchAlgorithms.CalculateFuzzyScore(query, target)
            .Should().BeGreaterThanOrEqualTo(minScore);
    }
    
    [Fact]
    public void CalculateFuzzyScore_ExactMatchBetterThanStartsWith()
    {
        var exact = SearchAlgorithms.CalculateFuzzyScore("notepad", "Notepad");
        var starts = SearchAlgorithms.CalculateFuzzyScore("notepad", "Notepad++");
        
        exact.Should().BeGreaterThan(starts);
    }
    
    [Fact]
    public void CalculateFuzzyScore_StartsWithBetterThanContains()
    {
        var starts = SearchAlgorithms.CalculateFuzzyScore("visual", "Visual Studio");
        var contains = SearchAlgorithms.CalculateFuzzyScore("studio", "Visual Studio");
        
        starts.Should().BeGreaterThan(contains);
    }
    
    // ══════════════════════════════════════════════════════════
    //  Fuzzy Per-Word — Tolérance aux typos
    // ══════════════════════════════════════════════════════════
    
    [Theory]
    [InlineData("firfox", "Firefox")]              // Transposition
    [InlineData("chorme", "Google Chrome")]         // Typo
    [InlineData("visul studo", "Visual Studio")]    // Multi-mots avec typos
    public void CalculateFuzzyScore_ToleratesTypos(string query, string target)
    {
        SearchAlgorithms.CalculateFuzzyScore(query, target)
            .Should().BeGreaterThan(0, $"'{query}' devrait matcher '{target}' malgré la typo");
    }
    
    [Fact]
    public void CalculateFuzzyScore_ZeroForCompletelyDifferent()
    {
        SearchAlgorithms.CalculateFuzzyScore("zzzzzzz", "Firefox")
            .Should().Be(0);
    }
    
    // ══════════════════════════════════════════════════════════
    //  Abréviations communes
    // ══════════════════════════════════════════════════════════
    
    [Theory]
    [InlineData("vs", "Visual Studio")]
    [InlineData("vscode", "Visual Studio Code")]
    [InlineData("ff", "Mozilla Firefox")]
    [InlineData("gc", "Google Chrome")]
    [InlineData("cmd", "Command Prompt")]
    [InlineData("npp", "Notepad++")]
    public void CalculateFuzzyScore_AbbreviationsMatchTargets(string abbreviation, string target)
    {
        SearchAlgorithms.CalculateFuzzyScore(abbreviation, target)
            .Should().BeGreaterThan(0, $"L'abréviation '{abbreviation}' devrait matcher '{target}'");
    }
    
    // ══════════════════════════════════════════════════════════
    //  Initials Matching
    // ══════════════════════════════════════════════════════════
    
    [Theory]
    [InlineData("vs", "visual studio", true)]
    [InlineData("gc", "google chrome", true)]
    [InlineData("vsc", "visual studio code", true)]
    [InlineData("xyz", "visual studio", false)]
    [InlineData("abcdef", "ab", false)]  // Query plus longue que les initiales
    public void MatchesInitials_ReturnsExpected(string query, string target, bool expected)
    {
        SearchAlgorithms.MatchesInitials(query, target)
            .Should().Be(expected);
    }
    
    // ══════════════════════════════════════════════════════════
    //  Subsequence Matching
    // ══════════════════════════════════════════════════════════
    
    [Theory]
    [InlineData("vsc", "visual studio code", true)]
    [InlineData("abc", "aXbXc", true)]
    [InlineData("acb", "abc", false)]  // Pas dans l'ordre
    public void IsSubsequenceMatch_ReturnsExpected(string query, string target, bool expected)
    {
        var weights = new ScoringWeights();
        SearchAlgorithms.IsSubsequenceMatch(query, target, weights, out _)
            .Should().Be(expected);
    }
    
    // ══════════════════════════════════════════════════════════
    //  Usage Bonus
    // ══════════════════════════════════════════════════════════
    
    [Fact]
    public void CalculateFuzzyScore_UsageBonusIncreasesScore()
    {
        var withoutUsage = SearchAlgorithms.CalculateFuzzyScore("firefox", "Firefox", 0);
        var withUsage = SearchAlgorithms.CalculateFuzzyScore("firefox", "Firefox", 10);
        
        withUsage.Should().BeGreaterThan(withoutUsage);
    }
    
    [Fact]
    public void CalculateFuzzyScore_UsageBonusIsCapped()
    {
        var weights = new ScoringWeights();
        var highUsage = SearchAlgorithms.CalculateFuzzyScore("firefox", "Firefox", 1000, weights);
        var maxBonus = SearchAlgorithms.CalculateFuzzyScore("firefox", "Firefox", 0, weights) + weights.MaxUsageBonus;
        
        highUsage.Should().BeLessThanOrEqualTo(maxBonus);
    }
    
    // ══════════════════════════════════════════════════════════
    //  Recency Bonus
    // ══════════════════════════════════════════════════════════
    
    [Fact]
    public void CalculateFuzzyScore_RecentUsageBoostsScore()
    {
        var weights = new ScoringWeights { EnableRecencyBonus = true, MaxRecencyBonus = 150 };
        var today = DateTime.UtcNow;
        var monthAgo = DateTime.UtcNow.AddDays(-30);
        
        var recentScore = SearchAlgorithms.CalculateFuzzyScore("firefox", "Firefox", 0, today, weights);
        var oldScore = SearchAlgorithms.CalculateFuzzyScore("firefox", "Firefox", 0, monthAgo, weights);
        
        recentScore.Should().BeGreaterThan(oldScore);
    }
    
    // ══════════════════════════════════════════════════════════
    //  Path Fuzzy Matching
    // ══════════════════════════════════════════════════════════
    
    [Theory]
    [InlineData("proj quick", @"C:\Projects\QuickLauncher\app.exe")]
    [InlineData("documents", @"C:\Users\Felix\Documents\report.docx")]
    public void CalculatePathFuzzyScore_MatchesPathSegments(string query, string path)
    {
        SearchAlgorithms.CalculatePathFuzzyScore(query, path)
            .Should().BeGreaterThan(0, $"'{query}' devrait matcher le chemin '{path}'");
    }
    
    [Theory]
    [InlineData("zzz xxx", @"C:\Projects\QuickLauncher\app.exe")]
    public void CalculatePathFuzzyScore_ZeroForNonMatching(string query, string path)
    {
        SearchAlgorithms.CalculatePathFuzzyScore(query, path)
            .Should().Be(0);
    }
    
    // ══════════════════════════════════════════════════════════
    //  Description Matching
    // ══════════════════════════════════════════════════════════
    
    [Theory]
    [InlineData("DNS", "Adaptateurs réseau, IP, DNS", 300)]
    [InlineData("réseau", "Adaptateurs réseau, IP, DNS", 300)]
    public void CalculateDescriptionScore_MatchesKeywords(string query, string desc, int minScore)
    {
        SearchAlgorithms.CalculateDescriptionScore(query, desc)
            .Should().BeGreaterThanOrEqualTo(minScore);
    }
    
    // ══════════════════════════════════════════════════════════
    //  StripEmojis
    // ══════════════════════════════════════════════════════════
    
    [Theory]
    [InlineData("⚙️ Paramètres Windows", "Paramètres Windows")]
    [InlineData("🔊 Volume", "Volume")]
    [InlineData("Texte sans emojis", "Texte sans emojis")]
    [InlineData("", "")]
    public void StripEmojis_RemovesDecorations(string input, string expected)
    {
        SearchAlgorithms.StripEmojis(input).Should().Be(expected);
    }
    
    // ══════════════════════════════════════════════════════════
    //  Scoring Weights — Paramétrage custom
    // ══════════════════════════════════════════════════════════
    
    [Fact]
    public void CalculateFuzzyScore_RespectsCustomWeights()
    {
        var defaultWeights = new ScoringWeights();
        var boostedWeights = new ScoringWeights { ExactMatch = 5000 };
        
        var defaultScore = SearchAlgorithms.CalculateFuzzyScore("firefox", "Firefox", 0, defaultWeights);
        var boostedScore = SearchAlgorithms.CalculateFuzzyScore("firefox", "Firefox", 0, boostedWeights);
        
        boostedScore.Should().BeGreaterThan(defaultScore);
    }
    
    // ══════════════════════════════════════════════════════════
    //  Cache
    // ══════════════════════════════════════════════════════════
    
    [Fact]
    public void Cache_SecondCallReturnsSameResult()
    {
        SearchAlgorithms.ClearCache();
        
        var first = SearchAlgorithms.DamerauLevenshteinDistance("hello", "helo");
        var second = SearchAlgorithms.DamerauLevenshteinDistance("hello", "helo");
        
        first.Should().Be(second);
        SearchAlgorithms.CacheCount.Should().BeGreaterThan(0);
    }
}
