using DesktopPet.Domain.Pets;

namespace DesktopPet.Application.Configuration;

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string Culture { get; init; } = "zh-CN";
    public MovementMode MovementMode { get; init; } = MovementMode.Hybrid;
    public HybridMovementStrategy HybridStrategy { get; init; } = HybridMovementStrategy.SmartHybrid;
    public DisplayPolicy DisplayPolicy { get; init; } = DisplayPolicy.LockedCurrent;
    public MotionStyle MotionStyle { get; init; } = MotionStyle.Natural;
    public PerformanceMode PerformanceMode { get; init; } = PerformanceMode.Auto;
    public LogOptions Logging { get; init; } = new();
    public SecurityLimits Security { get; init; } = new();

    public bool IsValid() => SchemaVersion == CurrentSchemaVersion &&
        Culture is "zh-CN" or "en-US" &&
        Enum.IsDefined(MovementMode) && Enum.IsDefined(HybridStrategy) &&
        Enum.IsDefined(DisplayPolicy) && Enum.IsDefined(MotionStyle) && Enum.IsDefined(PerformanceMode) &&
        Logging is not null && Logging.IsValid() && Security is not null && Security.IsValid();
}
public sealed record LogOptions
{
    public long MaxFileBytes { get; init; } = 2 * 1024 * 1024;
    public int RetainedFiles { get; init; } = 14;
    public bool IsValid() => MaxFileBytes is >= 1024 and <= 100 * 1024 * 1024 && RetainedFiles is >= 1 and <= 90;
}
// Initial conservative limits; the package validator is implemented in Phase 2.
public sealed record SecurityLimits
{
    public long MaxArchiveBytes { get; init; } = 100 * 1024 * 1024;
    public long MaxExpandedBytes { get; init; } = 500 * 1024 * 1024;
    public long MaxFileBytes { get; init; } = 20 * 1024 * 1024;
    public int MaxFiles { get; init; } = 5000;
    public int MaxImageDimension { get; init; } = 4096;
    public int MaxAnimationFrames { get; init; } = 1000;
    public bool IsValid() => MaxArchiveBytes > 0 && MaxExpandedBytes >= MaxArchiveBytes &&
        MaxFileBytes > 0 && MaxFileBytes <= MaxExpandedBytes && MaxFiles > 0 &&
        MaxImageDimension > 0 && MaxAnimationFrames > 0;
}
public enum SettingsLoadStatus { Loaded, Created, RecoveredInvalid }
public sealed record SettingsLoadResult(AppSettings Settings, SettingsLoadStatus Status);
public interface ISettingsService
{
    AppSettings Current { get; }
    Task<SettingsLoadResult> LoadAsync(CancellationToken ct);
    Task SaveAsync(AppSettings settings, CancellationToken ct);
}
