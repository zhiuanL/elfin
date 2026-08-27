using System.Diagnostics;
using DesktopPet.App.Bootstrap;

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
