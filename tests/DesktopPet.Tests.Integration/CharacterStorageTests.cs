using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DesktopPet.Application.Characters;
using DesktopPet.Application.Configuration;
using DesktopPet.CharacterSdk;
using DesktopPet.Domain.Platform;
using DesktopPet.Infrastructure.Configuration;
using DesktopPet.Windows.Characters;
using Microsoft.Extensions.Options;

namespace DesktopPet.Tests.Integration;

public sealed class CharacterStorageTests
{
    [Fact]
    public async Task ConventionalProfilePathsWorkWithoutExplicitProfileReferences()
    {
        using var context = new CharacterTestContext();
        var source = context.CopyFixture("dev-standard");
        var path = Path.Combine(source, "manifest.json");
        var manifest = CharacterSchema.Read<CharacterManifest>(await File.ReadAllTextAsync(path));
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest with { Profiles = new() }, CharacterSchema.JsonOptions()));
        var result = await context.Manager.ImportAsync(source, default);
        Assert.True(result.Succeeded);
        Assert.Equal(CharacterTier.Standard, result.Validation.ActualLevel);
        Assert.Equal("persona/zh-CN.json", result.Package!.Definition.Manifest.Profiles.Persona["zh-CN"]);
    }
    [Fact]
    public async Task DirectoryAndZipImportActivationRestoreRemovalAndNoSourceMutation()
    {
        using var context = new CharacterTestContext();
        await context.Settings.LoadAsync(default);
        var basic = context.CopyFixture();
        var original = HashTree(basic);
        var validated = await context.Manager.ValidateAsync(basic, default);
        Assert.True(validated.CanInstall);
        Assert.Empty(await context.Manager.ListAsync(default));
        var first = await context.Manager.InstallAsync(basic, default);
        Assert.True(first.Succeeded);
        Assert.Equal(CharacterTier.Basic, first.Validation.ActualLevel);
        var standard = context.CopyFixture("dev-standard");
        var zip = Path.Combine(context.Environment.Directories.Root, "standard.zip");
        ZipFile.CreateFromDirectory(standard, zip);
        var second = await context.Manager.ImportAsync(zip, default);
        Assert.True(second.Succeeded);
        Assert.Equal(CharacterTier.Standard, second.Validation.ActualLevel);
        Assert.Equal(2, (await context.Manager.DiscoverAsync(default)).Packages.Count);
        var id = second.Package!.Definition.Id;
        Assert.True((await context.Manager.ActivateAsync(id, default)).Succeeded);
        using var restored = new JsonSettingsService(context.Environment.Directories, Options.Create(new AppSettings()), context.Environment.Logger, TimeProvider.System);
        Assert.Equal(id.Value, (await restored.LoadAsync(default)).Settings.ActiveCharacterId);
        Assert.Contains((await context.Manager.RemoveAsync(id, default)).Issues, issue => issue.ErrorCode == CharacterErrorCode.ActiveCharacter);
        var duplicate = await context.Manager.ImportAsync(basic, default);
        Assert.Contains(duplicate.Validation.Issues, issue => issue.ErrorCode == CharacterErrorCode.AlreadyInstalled);
        Assert.Equal(original, HashTree(first.Package!.InstalledDirectory));
        Assert.True((await context.Manager.RemoveAsync(first.Package.Definition.Id, default)).CanInstall);
        Assert.Single(await context.Manager.ListAsync(default));
        Assert.Equal(original, HashTree(basic));
        context.AssertNoStaging();
        Assert.Empty(Directory.GetDirectories(context.Environment.Directories.Characters, ".removed-*"));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("C:/drive.txt")]
    [InlineData("\\\\server\\share\\file.txt")]
    [InlineData("images\\bad.png")]
    [InlineData("images/a.png:stream")]
    [InlineData("CON.txt")]
    [InlineData("COM¹.txt")]
    [InlineData("images/a. /b.txt")]
    public async Task UnsafeZipPathsAreRejectedWithoutPartialInstallation(string path)
    {
        using var context = new CharacterTestContext();
        var zip = CreateZip(context, [(path, "untrusted")]);
        var result = await context.Manager.ImportAsync(zip, default);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Validation.Issues, i => i.ErrorCode == CharacterErrorCode.InvalidPath);
        Assert.Empty(await context.Manager.ListAsync(default));
        Assert.False(File.Exists(Path.Combine(context.Environment.Directories.Root, "escape.txt")));
        context.AssertNoStaging();
    }
    [Theory]
    [InlineData("same.json", "SAME.json", CharacterErrorCode.DuplicateResource)]
    [InlineData("same.json", "same.json", CharacterErrorCode.DuplicateResource)]
    [InlineData("same.json", "same.json/child.json", CharacterErrorCode.StorageFailure)]
    public async Task ZipDuplicatesAndFileDirectoryConflictsAreRejected(string first, string second, CharacterErrorCode code)
    {
        using var context = new CharacterTestContext();
        var result = await context.Manager.ImportAsync(CreateZip(context, [(first, "{}"), (second, "{}")]), default);
        Assert.Contains(result.Validation.Issues, i => i.ErrorCode == code);
        context.AssertNoStaging();
    }
    [Theory]
    [InlineData("run.exe")]
    [InlineData("run.dll")]
    [InlineData("run.ps1")]
    [InlineData("run.cmd")]
    [InlineData("run.js")]
    public async Task ExecutableAndScriptResourcesAreForbidden(string path)
    {
        using var context = new CharacterTestContext();
        var result = await context.Manager.ImportAsync(CreateZip(context, [(path, "blocked")]), default);
        Assert.Contains(result.Validation.Issues, i => i.ErrorCode == CharacterErrorCode.ForbiddenFile);
        context.AssertNoStaging();
    }
    [Fact]
    public async Task ZipSymbolicLinksAreNeverMaterialized()
    {
        using var context = new CharacterTestContext();
        var zip = Path.Combine(context.Environment.Directories.Root, "link.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("link.json");
            entry.ExternalAttributes = unchecked((int)0xA1FF0000);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("../../outside");
        }
        var result = await context.Manager.ImportAsync(zip, default);
        Assert.Contains(result.Validation.Issues, i => i.ErrorCode == CharacterErrorCode.LinkNotAllowed);
        context.AssertNoStaging();
    }
    [Theory]
    [InlineData("count")]
    [InlineData("single")]
    [InlineData("expanded")]
    [InlineData("archive")]
    [InlineData("json")]
    public async Task CentralLimitsRejectOversizedInput(string kind)
    {
        using var context = new CharacterTestContext();
        await context.Settings.LoadAsync(default);
        await context.Settings.UpdateAsync(s => s with { Security = kind switch
        {
            "count" => s.Security with { MaxFiles = 1 },
            "single" => s.Security with { MaxFileBytes = 1000, MaxManifestBytes = 1000 },
            "expanded" => s.Security with { MaxExpandedBytes = 20000, MaxArchiveBytes = 10000, MaxFileBytes = 20000, MaxManifestBytes = 1000 },
            "archive" => s.Security with { MaxArchiveBytes = 10 },
            _ => s.Security with { MaxManifestBytes = 10 }
        } }, default);
        var zip = CreateZip(context, [("a.json", new string('a', 15000)), ("b.json", new string('b', 15000)), ("manifest.json", "{\"invalid\":true}")]);
        var result = await context.Manager.ImportAsync(zip, default);
        Assert.Contains(result.Validation.Issues, i => i.ErrorCode == CharacterErrorCode.ResourceLimit);
        context.AssertNoStaging();
    }
    [Theory]
    [InlineData("missing-manifest")]
    [InlineData("missing-idle")]
    [InlineData("broken-fallback")]
    [InlineData("oversized-image")]
    [InlineData("broken-zip")]
    public async Task BrokenPackagesNeverBecomeInstalled(string kind)
    {
        using var context = new CharacterTestContext();
        var source = context.CopyFixture();
        if (kind == "missing-manifest") File.Delete(Path.Combine(source, "manifest.json"));
        if (kind == "missing-idle") File.Delete(Path.Combine(source, "images", "idle.png"));
        if (kind == "broken-fallback") await File.WriteAllBytesAsync(Path.Combine(source, "fallback.png"), [137, 80, 78, 71]);
        if (kind == "oversized-image") await context.Settings.UpdateAsync(s => s with { Security = s.Security with { MaxImageDimension = 128 } }, default);
        if (kind == "broken-zip") { source = Path.Combine(context.Environment.Directories.Root, "broken.zip"); await File.WriteAllTextAsync(source, "not-a-zip"); }
        var result = await context.Manager.ImportAsync(source, default);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Validation.Issues, i => i.Severity == ValidationSeverity.Fatal);
        Assert.Empty(await context.Manager.ListAsync(default));
        context.AssertNoStaging();
    }
    [Fact]
    public async Task CancellationDuringStagedValidationCleansAllOwnedFiles()
    {
        using var cancellation = new CancellationTokenSource();
        using var context = new CharacterTestContext(new CancellingInspector(cancellation));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.Manager.ImportAsync(context.CopyFixture(), cancellation.Token));
        context.AssertNoStaging();
        Assert.Empty(await context.Manager.ListAsync(default));
    }
    [Fact]
    public async Task PhaseOneSettingsMigrationPreservesWindowAndAddsCharacterSelection()
    {
        using var context = new CharacterTestContext();
        var old = new AppSettings { SchemaVersion = 2, Culture = "en-US", PetWindow = new() { IsVisible = false, Position = new(new(-500, -300), "negative-monitor") } };
        await File.WriteAllTextAsync(Path.Combine(context.Environment.Directories.Config, "settings.json"), JsonSerializer.Serialize(old, CharacterSchema.JsonOptions()));
        var result = await context.Settings.LoadAsync(default);
        Assert.Equal(SettingsLoadStatus.Migrated, result.Status);
        Assert.Null(result.Settings.ActiveCharacterId);
        Assert.False(result.Settings.PetWindow.IsVisible);
        Assert.Equal(new PixelPoint(-500, -300), result.Settings.PetWindow.Position!.Origin);
        Assert.Equal("en-US", result.Settings.Culture);
    }
    private static string CreateZip(CharacterTestContext context, (string Path, string Content)[] entries)
    {
        var zip = Path.Combine(context.Environment.Directories.Root, Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var (path, content) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(path, CompressionLevel.SmallestSize).Open(), Encoding.UTF8);
            writer.Write(content);
        }
        return zip;
    }
    private static string[] HashTree(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
        .Select(path => Path.GetRelativePath(root, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();
    private sealed class CancellingInspector(CancellationTokenSource cancellation) : IPngInspector
    {
        public PngInfo Inspect(Stream stream, int maximum) { cancellation.Cancel(); return new WindowsPngInspector().Inspect(stream, maximum); }
    }
}
