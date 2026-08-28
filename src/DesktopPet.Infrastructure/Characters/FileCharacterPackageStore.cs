using System.IO.Compression;
using DesktopPet.Application.Characters;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Storage;
using DesktopPet.CharacterSdk;
using DesktopPet.Domain.Pets;

namespace DesktopPet.Infrastructure.Characters;

public sealed class FileCharacterPackageStore(IAppDataDirectories directories, ISettingsService settings,
    ICharacterPackageValidator validator, IPngInspector png, IExceptionHandler exceptions) : ICharacterPackageStore, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<CharacterOperationResult> InspectAsync(string sourcePath, bool install, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        string? stage = null;
        try
        {
            directories.EnsureCreated();
            PackageFiles.RejectLinks(directories.Characters);
            using var lease = new FileStream(Path.Combine(directories.Characters, ".operations.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (string.IsNullOrWhiteSpace(sourcePath) || !Path.IsPathFullyQualified(sourcePath))
                return Rejected(CharacterErrorCode.InvalidPath, "Select an absolute directory or ZIP path.");
            PackageFiles.RejectLinks(sourcePath);
            stage = Path.Combine(directories.Characters, ".stage-" + Guid.NewGuid().ToString("N"));
            var sourceRoot = Path.GetFullPath(sourcePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (stage.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase))
                return Rejected(CharacterErrorCode.InvalidPath, "Source must not contain the installation staging directory.");
            Directory.CreateDirectory(stage);
            var limits = settings.Current.Security;
            if (Directory.Exists(sourcePath)) await CopyDirectoryAsync(sourcePath, stage, limits, ct);
            else if (Path.GetExtension(sourcePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                await ExtractAsync(sourcePath, stage, limits, ct);
            else return Rejected(CharacterErrorCode.ForbiddenFile, "Expected a directory or ZIP archive.");
            var validation = await ValidateDirectoryAsync(stage, limits, ct);
            if (!validation.CanInstall || validation.Definition is null) return new(validation);
            if (!install) return new(validation);
            var destination = PackagePath.Resolve(directories.Characters, validation.Definition.Id.Value);
            if (Directory.Exists(destination) || File.Exists(destination))
                return Rejected(CharacterErrorCode.AlreadyInstalled, "This character identifier is already installed; no files were overwritten.");
            ct.ThrowIfCancellationRequested();
            Directory.Move(stage, destination); // Same volume, complete validated tree becomes visible atomically.
            stage = null;
            return new(validation, new(validation.Definition, destination));
        }
        catch (PackageInputException e) { return Rejected(e.Code, e.Message); }
        catch (InvalidDataException) { return Rejected(CharacterErrorCode.InvalidArchive, "Archive is corrupt or uses unsupported compression."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return Rejected(CharacterErrorCode.StorageFailure, "Package could not be read or installed safely."); }
        finally
        {
            if (stage is not null)
            {
                try { PackageFiles.DeleteOwnedDirectory(directories.Characters, stage); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { exceptions.Report(e, ErrorCode.CommandFailed, ErrorOrigin.Command); }
            }
            _gate.Release();
        }
    }

    public async Task<CharacterDiscovery> DiscoverAsync(CancellationToken ct)
    {
        directories.EnsureCreated();
        PackageFiles.RejectLinks(directories.Characters);
        var packages = new List<CharacterPackage>();
        var issues = new List<ValidationIssue>();
        foreach (var path in Directory.EnumerateDirectories(directories.Characters).Order(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var id = Path.GetFileName(path);
            if (!CharacterSchema.IsValidId(id)) continue;
            var result = await GetAsync(new(id), ct);
            if (result.Package is { } package) packages.Add(package);
            issues.AddRange(result.Validation.Issues);
        }
        return new(packages.AsReadOnly(), issues.AsReadOnly());
    }
    public async Task<CharacterOperationResult> GetAsync(CharacterId id, CancellationToken ct)
    {
        if (!CharacterSchema.IsValidId(id.Value) || !PackagePath.IsSafe(id.Value)) return Rejected(CharacterErrorCode.InvalidId, "Invalid character identifier.");
        var path = PackagePath.Resolve(directories.Characters, id.Value);
        if (!Directory.Exists(path)) return Rejected(CharacterErrorCode.NotFound, "Character is not installed.");
        try
        {
            var result = await ValidateDirectoryAsync(path, settings.Current.Security, ct);
            if (result.Definition is { } definition && definition.Id != id) return Rejected(CharacterErrorCode.InvalidId, "Installed directory and manifest identifier differ.");
            return new(result, result.CanInstall && result.Definition is { } valid ? new(valid, path) : null);
        }
        catch (PackageInputException e) { return Rejected(e.Code, e.Message); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { return Rejected(CharacterErrorCode.StorageFailure, "Installed package is unavailable."); }
    }
    public async Task<ValidationResult> RemoveAsync(CharacterId id, CancellationToken ct)
    {
        if (!CharacterSchema.IsValidId(id.Value) || !PackagePath.IsSafe(id.Value)) return Rejected(CharacterErrorCode.InvalidId, "Invalid identifier.").Validation;
        await _gate.WaitAsync(ct);
        try
        {
            PackageFiles.RejectLinks(directories.Characters);
            using var lease = new FileStream(Path.Combine(directories.Characters, ".operations.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var target = PackagePath.Resolve(directories.Characters, id.Value);
            if (!Directory.Exists(target)) return Rejected(CharacterErrorCode.NotFound, "Character is not installed.").Validation;
            PackageFiles.RejectLinks(target);
            ct.ThrowIfCancellationRequested();
            var removed = Path.Combine(directories.Characters, ".removed-" + Guid.NewGuid().ToString("N"));
            Directory.Move(target, removed);
            PackageFiles.DeleteOwnedDirectory(directories.Characters, removed);
            return new(true, null, []);
        }
        catch (PackageInputException e) { return Rejected(e.Code, e.Message).Validation; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { return Rejected(CharacterErrorCode.StorageFailure, "Removal could not complete; inspect local character storage.").Validation; }
        finally { _gate.Release(); }
    }
    private Task<ValidationResult> ValidateDirectoryAsync(string root, SecurityLimits limits, CancellationToken ct) =>
        Task.Run(() => ReadDirectoryAsync(root, limits, ct), ct);
    private async Task<ValidationResult> ReadDirectoryAsync(string root, SecurityLimits limits, CancellationToken ct)
    {
        var files = PackageFiles.Enumerate(root, limits.MaxFiles);
        if (!files.Contains("manifest.json")) return ValidationResult.Reject(CharacterErrorCode.MissingManifest, "manifest.json", "Root manifest.json is required.");
        var resources = new Dictionary<string, CharacterResource>(StringComparer.Ordinal);
        long total = 0;
        foreach (var relative in files)
        {
            ct.ThrowIfCancellationRequested();
            var path = PackagePath.Resolve(root, relative);
            var length = new FileInfo(path).Length;
            total = checked(total + length);
            if (length > limits.MaxFileBytes || total > limits.MaxExpandedBytes)
                throw new PackageInputException(CharacterErrorCode.ResourceLimit, "Resource byte limit exceeded.");
            PngInfo? image = null;
            string? json = null;
            if (relative.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                using var input = File.OpenRead(path);
                image = png.Inspect(input, limits.MaxImageDimension);
            }
            else if (relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                if (length > limits.MaxManifestBytes) throw new PackageInputException(CharacterErrorCode.ResourceLimit, "JSON resource limit exceeded.");
                json = await File.ReadAllTextAsync(path, ct);
            }
            resources.Add(relative, new(relative, length, image, json));
        }
        return validator.Validate(new(resources["manifest.json"].Json!, resources),
            new(limits.MaxImageDimension, limits.MaxAnimationFrames));
    }
    private static async Task CopyDirectoryAsync(string source, string stage, SecurityLimits limits, CancellationToken ct)
    {
        var files = PackageFiles.Enumerate(source, limits.MaxFiles);
        long total = 0;
        foreach (var relative in files)
        {
            ct.ThrowIfCancellationRequested();
            var path = PackagePath.Resolve(source, relative);
            if (new FileInfo(path).Length > limits.MaxFileBytes) throw new PackageInputException(CharacterErrorCode.ResourceLimit, "Single file limit exceeded.");
            await using var input = File.OpenRead(path);
            total += await CopyBoundedAsync(input, PackagePath.Resolve(stage, relative), limits.MaxFileBytes, limits.MaxExpandedBytes - total, ct);
        }
    }
    private static async Task ExtractAsync(string source, string stage, SecurityLimits limits, CancellationToken ct)
    {
        if (new FileInfo(source).Length > limits.MaxArchiveBytes) throw new PackageInputException(CharacterErrorCode.ResourceLimit, "Archive byte limit exceeded.");
        using var archive = ZipFile.OpenRead(source);
        if (archive.Entries.Count > limits.MaxFiles) throw new PackageInputException(CharacterErrorCode.ResourceLimit, "Archive entry limit exceeded.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var isDirectory = entry.FullName.EndsWith('/');
            var relative = isDirectory ? entry.FullName[..^1] : entry.FullName;
            if (!PackagePath.IsSafe(relative) || relative.Split('/').Length > 32) throw new PackageInputException(CharacterErrorCode.InvalidPath, "Archive entry escapes the package or uses an unsafe path.");
            if (!seen.Add(relative)) throw new PackageInputException(CharacterErrorCode.DuplicateResource, "Duplicate archive path.");
            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if ((entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 || unixType is not (0 or 0x8000 or 0x4000))
                throw new PackageInputException(CharacterErrorCode.LinkNotAllowed, "Archive links and special files are forbidden.");
            var destination = PackagePath.Resolve(stage, relative);
            if (isDirectory) { Directory.CreateDirectory(destination); continue; }
            PackageFiles.CheckExtension(relative);
            if (entry.Length > limits.MaxFileBytes || entry.Length > limits.MaxExpandedBytes - total)
                throw new PackageInputException(CharacterErrorCode.ResourceLimit, "Expanded archive byte limit exceeded.");
            await using var input = entry.Open();
            total += await CopyBoundedAsync(input, destination, limits.MaxFileBytes, limits.MaxExpandedBytes - total, ct);
        }
    }
    private static async Task<long> CopyBoundedAsync(Stream input, string destination, long singleLimit, long remaining, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        var buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            written += read;
            if (written > singleLimit || written > remaining) throw new PackageInputException(CharacterErrorCode.ResourceLimit, "Actual extracted bytes exceed the configured limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        await output.FlushAsync(ct);
        return written;
    }
    private static CharacterOperationResult Rejected(CharacterErrorCode code, string message) => new(ValidationResult.Reject(code, null, message));
    public void Dispose() => _gate.Dispose();
}
