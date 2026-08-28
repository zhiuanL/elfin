using DesktopPet.Application.Configuration;
using DesktopPet.Domain.Platform;
using DesktopPet.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace DesktopPet.Tests.Integration;

public sealed class WindowSettingsTests
{
    [Fact]
    public async Task PhaseZeroSettingsMigrateWithoutLosingPreferencesAndKeepOriginalBackup()
    {
        using var env = new TestEnvironment();
        var path = Path.Combine(env.Directories.Config, "settings.json");
        const string original = """
            {"schemaVersion":1,"culture":"en-US","performanceMode":"PowerSaver",
             "logging":{"maxFileBytes":8192,"retainedFiles":3}}
            """;
        await File.WriteAllTextAsync(path, original);
        using var service = Create(env);
        var loaded = await service.LoadAsync(default);
        Assert.Equal(SettingsLoadStatus.Migrated, loaded.Status);
        Assert.Equal(AppSettings.CurrentSchemaVersion, loaded.Settings.SchemaVersion);
        Assert.Equal("en-US", loaded.Settings.Culture);
        Assert.Equal(DesktopPet.Domain.Pets.PerformanceMode.PowerSaver, loaded.Settings.PerformanceMode);
        Assert.Equal(8192, loaded.Settings.Logging.MaxFileBytes);
        Assert.True(loaded.Settings.PetWindow.IsVisible);
        Assert.Equal(ControlCenterCloseBehavior.HideToTray, loaded.Settings.ControlCenterCloseBehavior);
        Assert.Equal(original, await File.ReadAllTextAsync(path + ".bak"));
        Assert.Equal(SettingsLoadStatus.Loaded, (await service.LoadAsync(default)).Status);
    }
    [Fact]
    public async Task PositionVisibilityAndClosePolicySurviveFreshServiceInstance()
    {
        using var env = new TestEnvironment();
        using (var service = Create(env))
        {
            await service.LoadAsync(default);
            await service.UpdateAsync(current => current with
            {
                PetWindow = new() { Position = new(new(-1530, -230), "left"), IsVisible = false, Topmost = false },
                ControlCenterCloseBehavior = ControlCenterCloseBehavior.Exit
            }, default);
        }
        using var restored = Create(env);
        var result = await restored.LoadAsync(default);
        Assert.Equal(new SavedWindowPosition(new(-1530, -230), "left"), result.Settings.PetWindow.Position);
        Assert.False(result.Settings.PetWindow.IsVisible);
        Assert.False(result.Settings.PetWindow.Topmost);
        Assert.Equal(ControlCenterCloseBehavior.Exit, result.Settings.ControlCenterCloseBehavior);
        Assert.Empty(Directory.GetFiles(env.Directories.Config, "*.tmp"));
    }
    [Fact]
    public async Task AtomicUpdatesMergeWithLatestSnapshotRatherThanOverwriteOtherPreferences()
    {
        using var env = new TestEnvironment();
        using var service = Create(env);
        await service.LoadAsync(default);
        await Task.WhenAll(
            service.UpdateAsync(current => current with { Culture = "en-US" }, default),
            service.UpdateAsync(current => current with { PetWindow = current.PetWindow with { Position = new(new(-800, 100), "left") } }, default),
            service.UpdateAsync(current => current with { Logging = current.Logging with { RetainedFiles = 4 } }, default));
        var result = await service.LoadAsync(default);
        Assert.Equal("en-US", result.Settings.Culture);
        Assert.Equal(4, result.Settings.Logging.RetainedFiles);
        Assert.Equal(new PixelPoint(-800, 100), result.Settings.PetWindow.Position!.Origin);
    }
    [Fact]
    public async Task FailedOrCancelledUpdatePreservesDiskAndMemory()
    {
        using var env = new TestEnvironment();
        using var service = Create(env);
        await service.LoadAsync(default);
        var before = service.Current;
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(current => current with { Culture = "invalid" }, default));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.UpdateAsync(current => current with { Culture = "en-US" }, cancellation.Token));
        Assert.Equal(before, service.Current);
        Assert.Equal(before, (await service.LoadAsync(default)).Settings);
    }
    private static JsonSettingsService Create(TestEnvironment env) =>
        new(env.Directories, Options.Create(new AppSettings()), env.Logger, TimeProvider.System);
}
