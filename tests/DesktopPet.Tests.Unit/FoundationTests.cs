using System.Text.Json;
using DesktopPet.Application.Commands;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Localization;
using DesktopPet.CharacterSdk;
using DesktopPet.Domain.Pets;
using DesktopPet.Domain.Platform;
using DesktopPet.Infrastructure.Localization;
using DesktopPet.Infrastructure.Services;

namespace DesktopPet.Tests.Unit;

public sealed class FoundationTests
{
    [Fact]
    public void DefaultsMatchDocumentedOfflineBaseline()
    {
        var settings = new AppSettings();
        Assert.True(settings.IsValid());
        Assert.Equal(MovementMode.Hybrid, settings.MovementMode);
        Assert.Equal(HybridMovementStrategy.SmartHybrid, settings.HybridStrategy);
        Assert.Equal(DisplayPolicy.LockedCurrent, settings.DisplayPolicy);
        Assert.Equal(new EmotionState(new(60), new(70), new(20), new(20)), EmotionState.Initial);
        Assert.NotSame(EmotionState.Initial, EmotionState.Initial);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void EmotionPercentageRejectsOutOfRangeValues(int value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Percentage(value));

    [Fact]
    public void DisplayContractsPreserveNegativePhysicalCoordinates()
    {
        var display = new DisplayInfo("left", new(-1920, -400, 1920, 1080),
            new(-1920, -400, 1920, 1040), new(1.25, 1.5), false);
        var restored = JsonSerializer.Deserialize<DisplayInfo>(JsonSerializer.Serialize(display));
        Assert.Equal(display, restored);
    }

    [Fact]
    public void CoreAndApplicationDoNotReferenceAdapters()
    {
        foreach (var assembly in new[] { typeof(PetSnapshot).Assembly, typeof(IPetHost).Assembly, typeof(CharacterDefinition).Assembly })
        {
            var references = assembly.GetReferencedAssemblies().Select(name => name.Name).ToArray();
            Assert.DoesNotContain("DesktopPet.AI", references);
            Assert.DoesNotContain("DesktopPet.Infrastructure", references);
            Assert.DoesNotContain("DesktopPet.Windows", references);
            Assert.DoesNotContain("PresentationFramework", references);
            Assert.DoesNotContain("Microsoft.Data.Sqlite", references);
        }
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void AllUiKeysHaveResources(string culture)
    {
        var settings = new TestSettingsService { Current = new AppSettings { Culture = culture } };
        var text = new ResourceTextLocalizer(settings);
        foreach (var key in Enum.GetValues<TextKey>()) Assert.False(string.IsNullOrWhiteSpace(text.Get(key)));
        Assert.Equal(culture, text.Culture.Name);
    }

    [Fact]
    public void LocalizationUsesSettingsAndDoesNotMutateGlobalCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        var settings = new TestSettingsService();
        var text = new ResourceTextLocalizer(settings);
        var chinese = text.Get(TextKey.Close);
        settings.Current = new AppSettings { Culture = "en-US" };
        Assert.NotEqual(chinese, text.Get(TextKey.Close));
        Assert.Equal("Close", text.Get(TextKey.Close));
        Assert.Equal(previous, System.Globalization.CultureInfo.CurrentCulture);
    }

    [Fact]
    public void ExceptionBoundaryProducesCorrelationWithoutExceptionContent()
    {
        var logger = new RecordingLogger();
        var handler = new ExceptionHandler(logger, TimeProvider.System);
        var result = handler.Report(new InvalidOperationException("api-key=DO-NOT-PERSIST secret chat"),
            ErrorCode.CommandFailed, ErrorOrigin.Command);
        Assert.NotEqual(Guid.Empty, result.CorrelationId);
        Assert.Equal(result.CorrelationId, Assert.Single(logger.Entries).CorrelationId);
        var serialized = JsonSerializer.Serialize(logger.Entries);
        Assert.DoesNotContain("DO-NOT-PERSIST", serialized);
        Assert.DoesNotContain("secret chat", serialized);
    }

    [Fact]
    public async Task OptionalServicesAreDisabledWithoutNetworking()
    {
        Assert.Equal(OptionalServiceStatus.Disabled, await new NoOpUpdateService().CheckAsync(default));
        Assert.Equal(OptionalServiceStatus.Disabled, await new NoOpSyncService().SyncAsync(default));
        await new NoOpCrashReportingService().ReportAsync(new(Guid.NewGuid(), "error", DateTimeOffset.UtcNow), default);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new NoOpUpdateService().CheckAsync(cancellation.Token));
    }
}
