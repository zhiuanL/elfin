using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Storage;
using Microsoft.Extensions.Options;

namespace DesktopPet.Infrastructure.Diagnostics;

public sealed partial class RollingFileAppLogger(IAppDataDirectories directories, IOptions<AppSettings> options) : IAppLogger
{
    private readonly object _gate = new();
    private LogOptions _policy = options.Value.Logging;
    private readonly JsonSerializerOptions _json = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };
    public bool LastWriteSucceeded { get; private set; } = true;

    public void Configure(LogOptions policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (!policy.IsValid()) throw new ArgumentException("Invalid logging policy.", nameof(policy));
        lock (_gate) _policy = policy;
    }

    public void Write(AppLogEntry entry)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(directories.Logs);
                var line = JsonSerializer.Serialize(entry, _json) + Environment.NewLine;
                var bytes = Encoding.UTF8.GetByteCount(line);
                var prefix = "desktop-pet-" + entry.TimestampUtc.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
                var path = SelectFile(prefix, bytes);
                File.AppendAllText(path, line, Encoding.UTF8);
                var files = Directory.GetFiles(directories.Logs, "desktop-pet-*.jsonl")
                    .Where(path => LogNamePattern().IsMatch(Path.GetFileName(path)))
                    .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToArray();
                foreach (var stale in files.Skip(_policy.RetainedFiles)) File.Delete(stale);
                LastWriteSucceeded = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LastWriteSucceeded = false;
                // Logging must not cause a recursive application failure. No exception content is emitted.
                Trace.TraceError("DesktopPet local log write failed; inspect data directory permissions.");
            }
        }
    }

    private string SelectFile(string prefix, int additionalBytes)
    {
        var latest = Directory.GetFiles(directories.Logs, prefix + "-*.jsonl")
            .Where(path => LogNamePattern().IsMatch(Path.GetFileName(path)))
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).FirstOrDefault();
        var index = latest is null ? 0 : int.Parse(Path.GetFileNameWithoutExtension(latest).Split('-')[^1],
            System.Globalization.CultureInfo.InvariantCulture);
        for (; ; index++)
        {
            var path = Path.Combine(directories.Logs, $"{prefix}-{index:D6}.jsonl");
            if (!File.Exists(path) || new FileInfo(path).Length + additionalBytes <= _policy.MaxFileBytes)
                return path;
        }
    }

    [GeneratedRegex(@"^desktop-pet-[0-9]{8}-[0-9]{6}\.jsonl$", RegexOptions.CultureInvariant)]
    private static partial Regex LogNamePattern();
}
