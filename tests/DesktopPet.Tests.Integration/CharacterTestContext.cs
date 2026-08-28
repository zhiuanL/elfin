using DesktopPet.Application.Characters;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Windows;
using DesktopPet.CharacterSdk;
using DesktopPet.Infrastructure.Characters;
using DesktopPet.Infrastructure.Configuration;
using DesktopPet.Windows.Characters;
using Microsoft.Extensions.Options;

namespace DesktopPet.Tests.Integration;

internal sealed class CharacterTestContext : IDisposable
{
    public TestEnvironment Environment { get; } = new();
    public JsonSettingsService Settings { get; }
    public FileCharacterPackageStore Store { get; }
    public CharacterManager Manager { get; }
    public IExceptionHandler Exceptions { get; }
    public CharacterTestContext(IPngInspector? inspector = null)
    {
        Settings = new(Environment.Directories, Options.Create(new AppSettings()), Environment.Logger, TimeProvider.System);
        Exceptions = new ExceptionHandler(Environment.Logger, TimeProvider.System);
        Store = new(Environment.Directories, Settings, new CharacterPackageValidator(), inspector ?? new WindowsPngInspector(), Exceptions);
        Manager = new(Store, Settings);
    }
    public static string FixtureRoot
    {
        get
        {
            var cursor = new DirectoryInfo(AppContext.BaseDirectory);
            while (cursor is not null && !File.Exists(Path.Combine(cursor.FullName, "DesktopPet.sln"))) cursor = cursor.Parent;
            return Path.Combine(cursor?.FullName ?? throw new InvalidOperationException("Repository not found."), "tests", "Fixtures", "Characters");
        }
    }
    public string CopyFixture(string name = "dev-basic")
    {
        var source = Path.Combine(FixtureRoot, name);
        var destination = Path.Combine(Environment.Directories.Root, "source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var output = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.Copy(file, output);
        }
        return destination;
    }
    public void AssertNoStaging() => Assert.Empty(Directory.GetDirectories(Environment.Directories.Characters, ".stage-*"));
    public void Dispose() { Manager.Dispose(); Store.Dispose(); Settings.Dispose(); Environment.Dispose(); }
    public sealed class InlineDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Func<Task> action, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return action(); }
    }
}
