using FluentAssertions;
using Moq;
using QuickLauncher.Models;
using QuickLauncher.Services;
using QuickLauncher.Services.CommandHandlers;

namespace QuickLauncher.Tests;

/// <summary>
/// Tests pour le CommandRouter et les CommandHandlers.
/// Vérifie le routing des commandes et le parsing des arguments.
/// </summary>
public sealed class CommandRouterTests
{
    private static AppSettings CreateSettings(
        bool weatherEnabled = true, string weatherPrefix = "weather",
        bool translateEnabled = true, string translatePrefix = "translate",
        bool aiEnabled = true, string aiPrefix = "ai",
        bool findEnabled = true, string findPrefix = "find")
    {
        return new AppSettings
        {
            SystemCommands =
            [
                new() { Type = SystemControlType.Weather, Prefix = weatherPrefix, IsEnabled = weatherEnabled },
                new() { Type = SystemControlType.Translate, Prefix = translatePrefix, IsEnabled = translateEnabled, RequiresArgument = true },
                new() { Type = SystemControlType.AiChat, Prefix = aiPrefix, IsEnabled = aiEnabled, RequiresArgument = true },
                new() { Type = SystemControlType.SystemSearch, Prefix = findPrefix, IsEnabled = findEnabled, RequiresArgument = true }
            ]
        };
    }
    
    private static ISettingsProvider MockProvider(AppSettings? settings = null)
    {
        var mock = new Mock<ISettingsProvider>();
        mock.Setup(p => p.Current).Returns(settings ?? CreateSettings());
        return mock.Object;
    }
    
    // ══════════════════════════════════════════════════════════
    //  CommandRouter.FindHandler
    // ══════════════════════════════════════════════════════════
    
    [Theory]
    [InlineData(":weather", typeof(WeatherCommandHandler))]
    [InlineData(":weather paris", typeof(WeatherCommandHandler))]
    [InlineData(":translate bonjour", typeof(TranslationCommandHandler))]
    [InlineData(":ai what is an API", typeof(AiCommandHandler))]
    [InlineData(":find test.txt", typeof(WindowsSearchCommandHandler))]
    public void FindHandler_RoutesToCorrectHandler(string query, Type expectedType)
    {
        var provider = MockProvider();
        var handlers = new ICommandHandler[]
        {
            new WeatherCommandHandler(null!, provider),
            new TranslationCommandHandler(null!, provider),
            new AiCommandHandler(null!, provider),
            new WindowsSearchCommandHandler(provider)
        };
        var router = new CommandRouter(handlers);
        
        var handler = router.FindHandler(query);
        
        handler.Should().NotBeNull();
        handler.Should().BeOfType(expectedType);
    }
    
    [Theory]
    [InlineData("firefox")]
    [InlineData(":settings")]
    [InlineData("")]
    [InlineData(":unknowncommand")]
    public void FindHandler_ReturnsNull_ForNonCommandQueries(string query)
    {
        var provider = MockProvider();
        var handlers = new ICommandHandler[]
        {
            new WeatherCommandHandler(null!, provider),
            new TranslationCommandHandler(null!, provider),
            new AiCommandHandler(null!, provider),
            new WindowsSearchCommandHandler(provider)
        };
        var router = new CommandRouter(handlers);
        
        router.FindHandler(query).Should().BeNull();
    }
    
    [Fact]
    public void FindHandler_RespectsDisabledCommands()
    {
        var settings = CreateSettings(weatherEnabled: false);
        var provider = MockProvider(settings);
        var handlers = new ICommandHandler[]
        {
            new WeatherCommandHandler(null!, provider)
        };
        var router = new CommandRouter(handlers);
        
        router.FindHandler(":weather").Should().BeNull();
    }
    
    [Fact]
    public void FindHandler_RespectsCustomPrefixes()
    {
        var settings = CreateSettings(weatherPrefix: "meteo");
        var provider = MockProvider(settings);
        var handlers = new ICommandHandler[]
        {
            new WeatherCommandHandler(null!, provider)
        };
        var router = new CommandRouter(handlers);
        
        router.FindHandler(":meteo").Should().NotBeNull();
        router.FindHandler(":weather").Should().BeNull();
    }
    
    // ══════════════════════════════════════════════════════════
    //  TranslationCommandHandler.ParseTranslationInput
    // ══════════════════════════════════════════════════════════
    
    [Theory]
    [InlineData("hello", "auto", "en", "hello")]                       // Texte simple → defaults
    [InlineData("fr hello", "auto", "fr", "hello")]                    // Code langue + texte
    [InlineData("fr>en bonjour", "fr", "en", "bonjour")]               // Source>Target + texte
    [InlineData("es>fr hola mundo", "es", "fr", "hola mundo")]         // Multi-mots
    public void ParseTranslationInput_ParsesCorrectly(string input, string expectedSrc, string expectedTgt, string expectedText)
    {
        var settings = new AppSettings
        {
            TranslateSourceLang = "auto",
            TranslateTargetLang = "en"
        };
        
        var (src, tgt, text) = TranslationCommandHandler.ParseTranslationInput(input, settings);
        
        src.Should().Be(expectedSrc);
        tgt.Should().Be(expectedTgt);
        text.Should().Be(expectedText);
    }
    
    [Fact]
    public void ParseTranslationInput_EmptyInput_ReturnsDefaults()
    {
        var settings = new AppSettings
        {
            TranslateSourceLang = "auto",
            TranslateTargetLang = "en"
        };
        
        var (src, tgt, text) = TranslationCommandHandler.ParseTranslationInput("", settings);
        
        src.Should().Be("auto");
        tgt.Should().Be("en");
        text.Should().BeEmpty();
    }
}
