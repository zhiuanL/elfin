using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Storage;
using Microsoft.Extensions.Options;

namespace DesktopPet.Infrastructure.Configuration;

public sealed class UnsupportedSettingsVersionException(int version)
    : InvalidOperationException($"Unsupported settings schema version: {version}.");

public sealed class JsonSettingsService(IAppDataDirectories directories, IOptions<AppSettings> defaults,
    IAppLogger logger, TimeProvider timeProvider) : ISettingsService, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = CreateJsonOptions();
    private AppSettings _current = defaults.Value;
    public AppSettings Current => _current;
    private string SettingsPath => Path.Combine(directories.Config, "settings.json");

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }

    public async Task<SettingsLoadResult> LoadAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            directories.EnsureCreated();
            if (!File.Exists(SettingsPath))
            {
                await WriteAtomicAsync(defaults.Value, ct);
                _current = defaults.Value;
                logger.Configure(_current.Logging);
                return new(_current, SettingsLoadStatus.Created);
            }
            AppSettings? loaded;
            var migrated = false;
            try
            {
                await using var stream = File.OpenRead(SettingsPath);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                // Inspect the envelope before rejecting unknown fields from a future schema.
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("schemaVersion", out var schema) &&
                    schema.ValueKind == JsonValueKind.Number &&
                    schema.TryGetInt32(out var version) && version > AppSettings.CurrentSchemaVersion)
                    throw new UnsupportedSettingsVersionException(version);
                loaded = document.RootElement.Deserialize<AppSettings>(_json);
                if (loaded?.SchemaVersion is 1 or 2 or 3 or 4 or 5 or 6)
                {
                    loaded = loaded with { SchemaVersion = AppSettings.CurrentSchemaVersion };
                    migrated = true;
                }
                if (loaded is null || !loaded.IsValid()) throw new JsonException("Invalid settings.");
            }
            catch (JsonException)
            {
                ct.ThrowIfCancellationRequested();
                var quarantine = SettingsPath + ".invalid-" + Guid.NewGuid().ToString("N");
                // Preserve the original for user recovery. Never include its content in logs.
                File.Copy(SettingsPath, quarantine, overwrite: false);
                await WriteAtomicAsync(defaults.Value, ct);
                _current = defaults.Value;
                logger.Configure(_current.Logging);
                logger.Write(new(AppEvent.SettingsRecovered, timeProvider.GetUtcNow()));
                return new(_current, SettingsLoadStatus.RecoveredInvalid);
            }
            if (migrated) await WriteAtomicAsync(loaded, ct);
            _current = loaded;
            logger.Configure(_current.Logging);
            return new(_current, migrated ? SettingsLoadStatus.Migrated : SettingsLoadStatus.Loaded);
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.IsValid()) throw new ArgumentException("Settings validation failed.", nameof(settings));
        await _gate.WaitAsync(ct);
        try
        {
            directories.EnsureCreated();
            await WriteAtomicAsync(settings, ct);
            _current = settings;
            logger.Configure(_current.Logging);
        }
        finally { _gate.Release(); }
    }

    public async Task UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(update);
        await _gate.WaitAsync(ct);
        try
        {
            var next = update(_current);
            if (next is null || !next.IsValid()) throw new ArgumentException("Settings validation failed.", nameof(update));
            directories.EnsureCreated();
            await WriteAtomicAsync(next, ct);
            _current = next;
            logger.Configure(next.Logging);
        }
        finally { _gate.Release(); }
    }

    private async Task WriteAtomicAsync(AppSettings settings, CancellationToken ct)
    {
        if (!settings.IsValid()) throw new InvalidOperationException("Default settings validation failed.");
        var temporaryPath = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, settings, _json, ct);
                await stream.FlushAsync(ct);
                stream.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            if (File.Exists(SettingsPath))
                File.Replace(temporaryPath, SettingsPath, SettingsPath + ".bak");
            else
                File.Move(temporaryPath, SettingsPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
    public void Dispose() => _gate.Dispose();
}
