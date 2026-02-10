using FluentAssertions;
using QuickLauncher.Models;

namespace QuickLauncher.Tests;

/// <summary>
/// Tests pour AppSettings : sections, proxies de compatibilité, migration.
/// </summary>
public sealed class AppSettingsTests
{
    // ══════════════════════════════════════════════════════════
    //  Proxies de compatibilité
    // ══════════════════════════════════════════════════════════
    
    [Fact]
    public void Proxy_MaxResults_DelegatesToSearchSection()
    {
        var settings = new AppSettings();
        
        settings.MaxResults = 42;
        
        settings.Search.MaxResults.Should().Be(42);
        settings.MaxResults.Should().Be(42);
    }
    
    [Fact]
    public void Proxy_Theme_DelegatesToAppearanceSection()
    {
        var settings = new AppSettings();
        
        settings.Theme = "Light";
        
        settings.Appearance.Theme.Should().Be("Light");
        settings.Theme.Should().Be("Light");
    }
    
    [Fact]
    public void Proxy_WeatherCity_DelegatesToIntegrationsSection()
    {
        var settings = new AppSettings();
        
        settings.WeatherCity = "Paris";
        
        settings.Integrations.WeatherCity.Should().Be("Paris");
        settings.WeatherCity.Should().Be("Paris");
    }
    
    // ══════════════════════════════════════════════════════════
    //  Section Search — PinnedItems
    // ══════════════════════════════════════════════════════════
    
    [Fact]
    public void PinItem_AddsToPinnedItems()
    {
        var settings = new AppSettings();
        
        settings.PinItem("Notepad", @"C:\notepad.exe", ResultType.Application, "📝");
        
        settings.PinnedItems.Should().HaveCount(1);
        settings.PinnedItems[0].Name.Should().Be("Notepad");
    }
    
    [Fact]
    public void PinItem_DoesNotDuplicate()
    {
        var settings = new AppSettings();
        
        settings.PinItem("Notepad", @"C:\notepad.exe", ResultType.Application);
        settings.PinItem("Notepad", @"C:\notepad.exe", ResultType.Application);
        
        settings.PinnedItems.Should().HaveCount(1);
    }
    
    [Fact]
    public void UnpinItem_RemovesAndReorders()
    {
        var settings = new AppSettings();
        settings.PinItem("A", @"C:\a.exe", ResultType.Application);
        settings.PinItem("B", @"C:\b.exe", ResultType.Application);
        settings.PinItem("C", @"C:\c.exe", ResultType.Application);
        
        settings.UnpinItem(@"C:\b.exe");
        
        settings.PinnedItems.Should().HaveCount(2);
        settings.PinnedItems[0].Order.Should().Be(0);
        settings.PinnedItems[1].Order.Should().Be(1);
    }
    
    [Fact]
    public void IsPinned_ReturnsTrueForPinnedItem()
    {
        var settings = new AppSettings();
        settings.PinItem("Notepad", @"C:\notepad.exe", ResultType.Application);
        
        settings.IsPinned(@"C:\notepad.exe").Should().BeTrue();
        settings.IsPinned(@"C:\other.exe").Should().BeFalse();
    }
    
    // ══════════════════════════════════════════════════════════
    //  Section Search — History
    // ══════════════════════════════════════════════════════════
    
    [Fact]
    public void AddToSearchHistory_InsertsAtFront()
    {
        var settings = new AppSettings();
        settings.EnableSearchHistory = true;
        
        settings.AddToSearchHistory(new HistoryItem { Name = "First", Path = @"C:\first.exe" });
        settings.AddToSearchHistory(new HistoryItem { Name = "Second", Path = @"C:\second.exe" });
        
        settings.SearchHistory[0].Name.Should().Be("Second");
    }
    
    [Fact]
    public void AddToSearchHistory_RemovesDuplicates()
    {
        var settings = new AppSettings();
        settings.EnableSearchHistory = true;
        
        settings.AddToSearchHistory(new HistoryItem { Name = "App", Path = @"C:\app.exe" });
        settings.AddToSearchHistory(new HistoryItem { Name = "Other", Path = @"C:\other.exe" });
        settings.AddToSearchHistory(new HistoryItem { Name = "App Updated", Path = @"C:\app.exe" });
        
        settings.SearchHistory.Should().HaveCount(2);
        settings.SearchHistory[0].Name.Should().Be("App Updated");
    }
    
    [Fact]
    public void AddToSearchHistory_RespectsMaxLimit()
    {
        var settings = new AppSettings();
        settings.EnableSearchHistory = true;
        settings.MaxSearchHistory = 3;
        
        for (var i = 0; i < 5; i++)
            settings.AddToSearchHistory(new HistoryItem { Name = $"Item{i}", Path = $@"C:\item{i}.exe" });
        
        settings.SearchHistory.Should().HaveCount(3);
    }
    
    // ══════════════════════════════════════════════════════════
    //  Sections — Default values
    // ══════════════════════════════════════════════════════════
    
    [Fact]
    public void NewSettings_HasCorrectDefaults()
    {
        var settings = new AppSettings();
        
        // Search defaults
        settings.Search.MaxResults.Should().Be(Constants.DefaultMaxResults);
        settings.Search.EnableSearchHistory.Should().BeTrue();
        settings.Search.IndexedFolders.Should().NotBeEmpty();
        
        // Appearance defaults
        settings.Appearance.Theme.Should().Be("Dark");
        settings.Appearance.EnableAnimations.Should().BeTrue();
        settings.Appearance.AnimationDurationMs.Should().Be(140);
        
        // Integration defaults
        settings.Integrations.WeatherCity.Should().Be("Montreal");
        settings.Integrations.AiModel.Should().Be("gpt-4o-mini");
        
        // Root defaults
        settings.SystemCommands.Should().NotBeEmpty();
        settings.StartWithWindows.Should().BeTrue();
    }
    
    // ══════════════════════════════════════════════════════════
    //  ScoringWeights
    // ══════════════════════════════════════════════════════════
    
    [Fact]
    public void ScoringWeights_ResetToDefaults_RestoresAllValues()
    {
        var weights = new ScoringWeights { ExactMatch = 9999, Contains = 1 };
        
        weights.ResetToDefaults();
        
        weights.ExactMatch.Should().Be(1000);
        weights.Contains.Should().Be(600);
    }
    
    [Fact]
    public void ScoringWeights_Clone_ProducesIndependentCopy()
    {
        var original = new ScoringWeights { ExactMatch = 500 };
        var clone = original.Clone();
        
        clone.ExactMatch = 999;
        
        original.ExactMatch.Should().Be(500);
        clone.ExactMatch.Should().Be(999);
    }
}
