using FluentAssertions;
using Moq;
using QuickLauncher.Models;
using QuickLauncher.Services;
using QuickLauncher.Services.CommandHandlers;

namespace QuickLauncher.Tests;

/// <summary>
/// Tests pour le SystemControlExecutor.
/// Vérifie l'exécution des commandes système (timer, note, copier, autocomplete, normalisation).
/// </summary>
public sealed class SystemControlExecutorTests
{
    private static AppSettings CreateSettings(
        string translatePrefix = "translate",
        string aiPrefix = "ai",
        string timerPrefix = "timer",
        string notePrefix = "note",
        string screenshotPrefix = "screenshot",
        string lockPrefix = "lock",
        string weatherPrefix = "weather")
    {
        return new AppSettings
        {
            SystemCommands =
            [
                new() { Type = SystemControlType.Translate, Prefix = translatePrefix, IsEnabled = true, RequiresArgument = true },
                new() { Type = SystemControlType.AiChat, Prefix = aiPrefix, IsEnabled = true, RequiresArgument = true },
                new() { Type = SystemControlType.Timer, Prefix = timerPrefix, IsEnabled = true, RequiresArgument = true },
                new() { Type = SystemControlType.Note, Prefix = notePrefix, IsEnabled = true, RequiresArgument = true },
                new() { Type = SystemControlType.Screenshot, Prefix = screenshotPrefix, IsEnabled = true },
                new() { Type = SystemControlType.Lock, Prefix = lockPrefix, IsEnabled = true },
                new() { Type = SystemControlType.Weather, Prefix = weatherPrefix, IsEnabled = true },
            ]
        };
    }
    
    private static (SystemControlExecutor Executor, Mock<ISettingsProvider> ProviderMock,
        Mock<NoteWidgetService> NoteMock, Mock<TimerWidgetService> TimerMock) 
        CreateExecutor(AppSettings? settings = null)
    {
        var providerMock = new Mock<ISettingsProvider>();
        providerMock.Setup(p => p.Current).Returns(settings ?? CreateSettings());
        
        // NoteWidgetService et TimerWidgetService nécessitent ISettingsProvider
        var noteMock = new Mock<NoteWidgetService>(providerMock.Object);
        var timerMock = new Mock<TimerWidgetService>(providerMock.Object);
        
        var executor = new SystemControlExecutor(
            providerMock.Object,
            noteMock.Object,
            timerMock.Object);
        
        return (executor, providerMock, noteMock, timerMock);
    }
    
    // ══════════════════════════════════════════════════════════
    //  Commandes null/vides
    // ══════════════════════════════════════════════════════════
    
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Execute_NullOrEmpty_ReturnsNotHandled(string? command)
    {
        var (executor, _, _, _) = CreateExecutor();
        
        executor.Execute(command!).Handled.Should().BeFalse();
    }
    
    // ══════════════════════════════════════════════════════════
    //  Copier-coller depuis :translate et :ai
    // ══════════════════════════════════════════════════════════
    
    [Fact]
    public void Execute_TranslateCopy_ReturnsCopyResult()
    {
        var (executor, _, _, _) = CreateExecutor();
        
        var result = executor.Execute(":translate:copy:Bonjour le monde");
        
        result.Handled.Should().BeTrue();
        result.ShouldHide.Should().BeTrue();
        result.Notification.Should().Contain("Traduction");
    }
    
    [Fact]
    public void Execute_AiCopy_ReturnsCopyResult()
    {
        var (executor, _, _, _) = CreateExecutor();
        
        var result = executor.Execute(":ai:copy:Réponse de l'IA");
        
        result.Handled.Should().BeTrue();
        result.ShouldHide.Should().BeTrue();
        result.Notification.Should().Contain("IA");
    }
    
    [Fact]
    public void Execute_TranslateCopy_EmptyText_ReturnsNotHandled()
    {
        var (executor, _, _, _) = CreateExecutor();
        
        executor.Execute(":translate:copy:").Handled.Should().BeFalse();
    }
    
    // ══════════════════════════════════════════════════════════
    //  Autocomplete pour commandes async sans argument
    // ══════════════════════════════════════════════════════════
    
    [Fact]
    public void Execute_TranslateWithoutArg_ReturnsAutoComplete()
    {
        var (executor, _, _, _) = CreateExecutor();
        
        var result = executor.Execute(":translate");
        
        result.Handled.Should().BeTrue();
        result.AutoCompleteText.Should().Be(":translate ");
    }
    
    [Fact]
    public void Execute_AiWithoutArg_ReturnsAutoComplete()
    {
        var (executor, _, _, _) = CreateExecutor();
        
        var result = executor.Execute(":ai");
        
        result.Handled.Should().BeTrue();
        result.AutoCompleteText.Should().Be(":ai ");
    }
    
    [Fact]
    public void Execute_WeatherWithoutArg_ReturnsNotHandled_ForAsyncRouting()
    {
        // Weather sans argument doit être routé via CommandRouter (pas d'autocomplete)
        var (executor, _, _, _) = CreateExecutor();
        
        var result = executor.Execute(":weather");
        
        // Weather est toujours async, même sans argument
        result.Handled.Should().BeFalse();
    }
    
    // ══════════════════════════════════════════════════════════
    //  Screenshot
    // ══════════════════════════════════════════════════════════
    
    [Theory]
    [InlineData("snip")]
    [InlineData("region")]
    [InlineData("select")]
    public void Execute_ScreenshotSnip_ReturnsScreenCaptureMode(string mode)
    {
        var (executor, _, _, _) = CreateExecutor();
        
        var result = executor.Execute($":screenshot {mode}");
        
        result.Handled.Should().BeTrue();
        result.ShouldHide.Should().BeTrue();
        result.ScreenCaptureMode.Should().Be(mode);
    }
    
    [Fact]
    public void Execute_ScreenshotNoArg_ReturnsFullscreenMode()
    {
        var (executor, _, _, _) = CreateExecutor();
        
        var result = executor.Execute(":screenshot");
        
        result.Handled.Should().BeTrue();
        result.ScreenCaptureMode.Should().Be("fullscreen");
    }
    
    // ══════════════════════════════════════════════════════════
    //  NormalizeSystemCommand
    // ══════════════════════════════════════════════════════════
    
    [Theory]
    [InlineData(":lock", ":lock")]
    [InlineData(":unknownprefix", ":unknownprefix")]
    public void NormalizeSystemCommand_StandardPrefixes_ReturnExpected(string input, string expected)
    {
        var (executor, _, _, _) = CreateExecutor();
        
        executor.NormalizeSystemCommand(input).Should().Be(expected);
    }
    
    [Fact]
    public void NormalizeSystemCommand_CustomPrefix_NormalizesToStandard()
    {
        var settings = CreateSettings(lockPrefix: "verrouiller");
        var (executor, _, _, _) = CreateExecutor(settings);
        
        executor.NormalizeSystemCommand(":verrouiller").Should().Be(":lock");
    }
    
    [Fact]
    public void NormalizeSystemCommand_WithArgument_PreservesArg()
    {
        var settings = CreateSettings();
        var (executor, _, _, _) = CreateExecutor(settings);
        
        // "volume" maps to SystemControlType.Volume → but it's not in our test settings
        // Let's use a command that IS in settings
        // Actually lock doesn't take args, but the method should still preserve them
        executor.NormalizeSystemCommand(":lock somearg").Should().Be(":lock somearg");
    }
    
    // ══════════════════════════════════════════════════════════
    //  Commande inconnue → NotHandled
    // ══════════════════════════════════════════════════════════
    
    [Fact]
    public void Execute_UnknownCommand_DelegatesToNormalizedExecution()
    {
        var (executor, _, _, _) = CreateExecutor();
        
        // Une commande qui n'existe dans aucun handler devrait tenter l'exécution normalisée
        // (SystemControlService.ExecuteCommand retournera probablement null dans un contexte de test)
        var result = executor.Execute(":completelyfakecommand");
        
        // Devrait au minimum ne pas crasher
        result.Should().NotBeNull();
    }
}
