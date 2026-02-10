using FluentAssertions;
using QuickLauncher.Models;
using QuickLauncher.Services;

namespace QuickLauncher.Tests;

/// <summary>
/// Tests pour SettingsProvider : cache en mémoire, notifications, thread-safety.
/// </summary>
public sealed class SettingsProviderTests
{
    // ══════════════════════════════════════════════════════════
    //  Initialisation
    // ══════════════════════════════════════════════════════════
    
    [Fact]
    public void Current_AfterConstruction_ReturnsNonNull()
    {
        var provider = new SettingsProvider();
        
        provider.Current.Should().NotBeNull();
    }
    
    [Fact]
    public void Current_AfterConstruction_HasDefaults()
    {
        var provider = new SettingsProvider();
        
        provider.Current.Search.Should().NotBeNull();
        provider.Current.Appearance.Should().NotBeNull();
        provider.Current.Integrations.Should().NotBeNull();
        provider.Current.SystemCommands.Should().NotBeEmpty();
    }
    
    // ══════════════════════════════════════════════════════════
    //  Update
    // ══════════════════════════════════════════════════════════
    
    [Fact]
    public void Update_ModifiesCurrentSettings()
    {
        var provider = new SettingsProvider();
        
        provider.Update(s => s.Search.MaxResults = 99);
        
        provider.Current.Search.MaxResults.Should().Be(99);
    }
    
    [Fact]
    public void Update_FiresSettingsChanged()
    {
        var provider = new SettingsProvider();
        AppSettings? received = null;
        provider.SettingsChanged += (_, s) => received = s;
        
        provider.Update(s => s.Appearance.Theme = "Light");
        
        received.Should().NotBeNull();
        received!.Appearance.Theme.Should().Be("Light");
    }
    
    [Fact]
    public void Update_MultipleUpdates_AllApplied()
    {
        var provider = new SettingsProvider();
        
        provider.Update(s => s.Search.MaxResults = 10);
        provider.Update(s => s.Appearance.Theme = "Light");
        provider.Update(s => s.Integrations.WeatherCity = "Paris");
        
        provider.Current.Search.MaxResults.Should().Be(10);
        provider.Current.Appearance.Theme.Should().Be("Light");
        provider.Current.Integrations.WeatherCity.Should().Be("Paris");
    }
    
    // ══════════════════════════════════════════════════════════
    //  Save
    // ══════════════════════════════════════════════════════════
    
    [Fact]
    public void Save_FiresSettingsChanged()
    {
        var provider = new SettingsProvider();
        var eventFired = false;
        provider.SettingsChanged += (_, _) => eventFired = true;
        
        provider.Save();
        
        eventFired.Should().BeTrue();
    }
    
    // ══════════════════════════════════════════════════════════
    //  Reload
    // ══════════════════════════════════════════════════════════
    
    [Fact]
    public void Reload_FiresSettingsChanged()
    {
        var provider = new SettingsProvider();
        var eventFired = false;
        provider.SettingsChanged += (_, _) => eventFired = true;
        
        provider.Reload();
        
        eventFired.Should().BeTrue();
    }
    
    [Fact]
    public void Reload_ReturnsNonNullCurrent()
    {
        var provider = new SettingsProvider();
        
        provider.Reload();
        
        provider.Current.Should().NotBeNull();
    }
    
    // ══════════════════════════════════════════════════════════
    //  Proxy consistency via Provider
    // ══════════════════════════════════════════════════════════
    
    [Fact]
    public void Update_ViaProxy_ReflectedInSection()
    {
        var provider = new SettingsProvider();
        
        provider.Update(s => s.MaxResults = 42);
        
        provider.Current.Search.MaxResults.Should().Be(42);
        provider.Current.MaxResults.Should().Be(42);
    }
    
    [Fact]
    public void Update_ViaSection_ReflectedInProxy()
    {
        var provider = new SettingsProvider();
        
        provider.Update(s => s.Search.MaxResults = 77);
        
        provider.Current.MaxResults.Should().Be(77);
    }
    
    // ══════════════════════════════════════════════════════════
    //  Thread-safety (basique — vérifie que des accès concurrents ne crashent pas)
    // ══════════════════════════════════════════════════════════
    
    [Fact]
    public void ConcurrentAccess_DoesNotThrow()
    {
        var provider = new SettingsProvider();
        
        var action = () => Parallel.For(0, 100, i =>
        {
            provider.Update(s => s.Search.MaxResults = i);
            _ = provider.Current.Search.MaxResults;
        });
        
        action.Should().NotThrow();
    }
}
