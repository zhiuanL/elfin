using System.Diagnostics;
using DesktopPet.App.Bootstrap;
using DesktopPet.Application.Configuration;
using DesktopPet.Domain.Platform;
using DesktopPet.Infrastructure.Configuration;
using DesktopPet.Windows.Windowing;
using Microsoft.Extensions.Options;

namespace DesktopPet.Tests.Integration;

public sealed class StartupSmokeTests
{
    [Fact]
    public async Task RealWpfWindowRendersAndRestartsWithoutAiOrNetworkConfiguration()
    {
        using var env = new TestEnvironment();
        Assert.Equal(0, await RunApp(env));
        Assert.Equal(0, await RunApp(env));
        Assert.True(File.Exists(Path.Combine(env.Directories.Data, "app.db")));
        Assert.True(File.Exists(Path.Combine(env.Directories.Data, "ai.db")));
        Assert.True(File.Exists(Path.Combine(env.Directories.Config, "settings.json")));
        var log = ReadLogs(env);
        Assert.Contains("Started", log);
        Assert.DoesNotContain("StartupFailed", log);
    }

    [Fact]
    public async Task CorruptAppDatabaseFailsStartupAndPreservesOriginal()
    {
        using var env = new TestEnvironment();
        var path = Path.Combine(env.Directories.Data, "app.db");
        await File.WriteAllTextAsync(path, "invalid-database-fixture");
        Assert.Equal(1, await RunApp(env));
        Assert.Equal("invalid-database-fixture", await File.ReadAllTextAsync(path));
        Assert.Contains("StartupFailed", ReadLogs(env));
    }

    [Fact]
    public async Task CorruptAiDatabaseStillAllowsWpfCoreToRender()
    {
        using var env = new TestEnvironment();
        var path = Path.Combine(env.Directories.Data, "ai.db");
        await File.WriteAllTextAsync(path, "invalid-ai-database-fixture");
        Assert.Equal(0, await RunApp(env));
        Assert.Equal("invalid-ai-database-fixture", await File.ReadAllTextAsync(path));
        var log = ReadLogs(env);
        Assert.Contains("AiStorageUnavailable", log);
        Assert.Contains("Started", log);
    }

    [Fact]
    public void StartupOptionsRequireIsolatedSmokeData()
    {
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["--smoke-test"]));
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["--data-root", "C:\\example"]));
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["--unknown"]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealProcessRestoresOrRepairsPersistedPhysicalPosition(bool offscreen)
    {
        using var env = new TestEnvironment();
        var primary = new WindowsDisplayService().GetDisplays().First(display => display.IsPrimary);
        var origin = offscreen ? new PixelPoint(1_000_000, 1_000_000) :
            new PixelPoint(primary.WorkingArea.X + 40, primary.WorkingArea.Y + 40);
        using var settings = new JsonSettingsService(env.Directories, Options.Create(new AppSettings()), env.Logger, TimeProvider.System);
        await settings.LoadAsync(default);
        await settings.UpdateAsync(current => current with { PetWindow = new() { Position = new(origin, primary.Id) } }, default);
        Assert.Equal(0, await RunApp(env));
        var restored = (await settings.LoadAsync(default)).Settings.PetWindow.Position!;
        if (!offscreen) Assert.Equal(origin, restored.Origin);
        else
        {
            Assert.NotEqual(origin, restored.Origin);
            Assert.InRange(restored.Origin.X, primary.WorkingArea.X, primary.WorkingArea.X + primary.WorkingArea.Width - 1);
            Assert.InRange(restored.Origin.Y, primary.WorkingArea.Y, primary.WorkingArea.Y + primary.WorkingArea.Height - 1);
        }
        Assert.Equal(0, await RunApp(env));
        Assert.Equal(restored, (await settings.LoadAsync(default)).Settings.PetWindow.Position);
        Assert.Contains("Stopping", ReadLogs(env));
    }

    private static string ReadLogs(TestEnvironment env) =>
        string.Join("\n", Directory.GetFiles(env.Directories.Logs, "*.jsonl").Select(File.ReadAllText));

    private static async Task<int> RunApp(TestEnvironment env)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = directory.Parent!.Name;
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DesktopPet.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var assembly = Path.Combine(directory.FullName, "src", "DesktopPet.App", "bin", configuration,
            "net10.0-windows", "DesktopPet.App.dll");
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrEmpty(dotnet))
        {
            var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            dotnet = root is null ? "dotnet" : Path.Combine(root, "dotnet.exe");
        }
        var start = new ProcessStartInfo(dotnet)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        start.ArgumentList.Add(assembly);
        start.ArgumentList.Add("--smoke-test");
        start.ArgumentList.Add("--data-root");
        start.ArgumentList.Add(env.Directories.Root);
        // A deliberately unavailable proxy. No providers are registered or configured at startup.
        start.Environment["HTTP_PROXY"] = "http://127.0.0.1:9";
        start.Environment["HTTPS_PROXY"] = "http://127.0.0.1:9";
        start.Environment["NO_PROXY"] = "";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("WPF smoke process did not start.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("WPF did not render and shut down within 25 seconds.");
        }
        return process.ExitCode;
    }
}
