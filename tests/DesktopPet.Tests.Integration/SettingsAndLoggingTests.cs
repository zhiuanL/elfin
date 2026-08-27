using System.Text.Json;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Storage;
using DesktopPet.Infrastructure.Configuration;
using DesktopPet.Infrastructure.Diagnostics;
using DesktopPet.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace DesktopPet.Tests.Integration;

public sealed class SettingsAndLoggingTests
{
    [Fact]
    public void InstalledAndPortablePathsFollowDesign()
    {
        using var env = new TestEnvironment();
        var installed = AppDataDirectories.Resolve(DeploymentMode.Installed, env.Directories.Root, env.Directories.Root);
        var portable = AppDataDirectories.Resolve(DeploymentMode.Portable, env.Directories.Root, env.Directories.Root);
        Assert.Equal(Path.Combine(env.Directories.Root, "DesktopPet"), installed.Root);
        Assert.Equal(Path.Combine(env.Directories.Root, "UserData"), portable.Root);
        Assert.Throws<ArgumentException>(() => new AppDataDirectories("relative"));
    }

    [Fact]
    public async Task SettingsRoundTripAtomicallyAndPreservePreviousVersion()
    {
        using var env = new TestEnvironment();
        using var service = CreateSettings(env);
        Assert.Equal(SettingsLoadStatus.Created, (await service.LoadAsync(default)).Status);
        var updated = new AppSettings { Culture = "en-US" };
        await service.SaveAsync(updated, default);
        Assert.Equal(updated, (await service.LoadAsync(default)).Settings);
        var backup = await File.ReadAllTextAsync(Path.Combine(env.Directories.Config, "settings.json.bak"));
        Assert.Contains("zh-CN", backup);
        Assert.Empty(Directory.GetFiles(env.Directories.Config, "*.tmp"));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"culture\":\"unsupported\"}")]
    [InlineData("{\"schemaVersion\":\"invalid\"}")]
    [InlineData("{\"apiKey\":\"not-a-real-key\"}")]
    public async Task InvalidOrUnknownSettingsArePreservedAndRecovered(string invalid)
    {
        using var env = new TestEnvironment();
        var path = Path.Combine(env.Directories.Config, "settings.json");
        await File.WriteAllTextAsync(path, invalid);
        using var service = CreateSettings(env);
        var result = await service.LoadAsync(default);
        Assert.Equal(SettingsLoadStatus.RecoveredInvalid, result.Status);
        Assert.True(result.Settings.IsValid());
        var original = Assert.Single(Directory.GetFiles(env.Directories.Config, "*.invalid-*"));
        Assert.Equal(invalid, await File.ReadAllTextAsync(original));
        Assert.DoesNotContain("apiKey", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task FutureConfigurationIsNotOverwritten()
    {
        using var env = new TestEnvironment();
        var path = Path.Combine(env.Directories.Config, "settings.json");
        const string future = "{\"schemaVersion\":99,\"futureProperty\":true}";
        await File.WriteAllTextAsync(path, future);
        using var service = CreateSettings(env);
        await Assert.ThrowsAsync<UnsupportedSettingsVersionException>(() => service.LoadAsync(default));
        Assert.Equal(future, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task CancelledAndInvalidWritesPreserveExistingConfiguration()
    {
        using var env = new TestEnvironment();
        using var service = CreateSettings(env);
        await service.LoadAsync(default);
        var path = Path.Combine(env.Directories.Config, "settings.json");
        var before = await File.ReadAllTextAsync(path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SaveAsync(new AppSettings { Culture = "en-US" }, cancellation.Token));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(new AppSettings { Culture = "invalid" }, default));
        Assert.Equal(before, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task SavedLoggingPolicyIsAppliedToTheRunningLogger()
    {
        using var env = new TestEnvironment();
        using var service = CreateSettings(env);
        await service.LoadAsync(default);
        await service.SaveAsync(new AppSettings
        {
            Logging = new LogOptions { MaxFileBytes = 1024, RetainedFiles = 1 }
        }, default);
        for (var i = 0; i < 50; i++) env.Logger.Write(new(AppEvent.Started, DateTimeOffset.UtcNow));
        var file = Assert.Single(Directory.GetFiles(env.Directories.Logs, "*.jsonl"));
        Assert.InRange(new FileInfo(file).Length, 1, 1024);
    }

    [Fact]
    public void LogsRotateRetainLatestEventsAndContainNoExceptionSecrets()
    {
        using var env = new TestEnvironment();
        var logger = new RollingFileAppLogger(env.Directories, Options.Create(new AppSettings
        {
            Logging = new LogOptions { MaxFileBytes = 1024, RetainedFiles = 2 }
        }));
        var exceptionHandler = new ExceptionHandler(logger, TimeProvider.System);
        var unrelated = Path.Combine(env.Directories.Logs, "desktop-pet-user-notes.jsonl");
        File.WriteAllText(unrelated, "user-owned note");
        AppFailure? latest = null;
        for (var i = 0; i < 60; i++)
            latest = exceptionHandler.Report(new InvalidOperationException("sensitive-chat-and-key"),
                ErrorCode.CommandFailed, ErrorOrigin.Command);
        var files = Directory.GetFiles(env.Directories.Logs, "*.jsonl").Where(path => path != unrelated).ToArray();
        Assert.Equal("user-owned note", File.ReadAllText(unrelated));
        Assert.InRange(files.Length, 1, 2);
        var lines = files.SelectMany(File.ReadAllLines).Where(line => line.Length > 0).ToArray();
        Assert.All(lines, line => { using var json = JsonDocument.Parse(line); });
        var output = string.Join("\n", lines);
        Assert.DoesNotContain("sensitive-chat-and-key", output);
        Assert.Contains(latest!.CorrelationId.ToString(), output);
        Assert.True(logger.LastWriteSucceeded);
    }

    private static JsonSettingsService CreateSettings(TestEnvironment env) =>
        new(env.Directories, Options.Create(new AppSettings()), env.Logger, TimeProvider.System);
}
