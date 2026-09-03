using DesktopPet.Domain.Pets;

namespace DesktopPet.Application.Configuration;

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 8;
    public const int MaxEmotionCheckpoints = 256;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string Culture { get; init; } = "zh-CN";
    public string? ActiveCharacterId { get; init; }
    public RuntimePreferences Runtime { get; init; } = new();
    public MovementSettings Movement { get; init; } = new();
    public IReadOnlyList<EmotionCheckpoint> Emotions { get; init; } = [];
    public MovementMode MovementMode { get; init; } = MovementMode.Hybrid;
    public HybridMovementStrategy HybridStrategy { get; init; } = HybridMovementStrategy.SmartHybrid;
    public DisplayPolicy DisplayPolicy { get; init; } = DisplayPolicy.LockedCurrent;
    public MotionStyle MotionStyle { get; init; } = MotionStyle.Natural;
    public PerformanceMode PerformanceMode { get; init; } = PerformanceMode.Auto;
    public LogOptions Logging { get; init; } = new();
    public SecurityLimits Security { get; init; } = new();
    public PetWindowSettings PetWindow { get; init; } = new();
    public ControlCenterCloseBehavior ControlCenterCloseBehavior { get; init; } = ControlCenterCloseBehavior.HideToTray;
    public AppearanceSettings Appearance { get; init; } = new();
    public HotkeySettings Hotkeys { get; init; } = new();
    public ProductivitySettings Productivity { get; init; } = new();
    public AiToolSettings AiTools { get; init; } = new();

    // Settings remain a value snapshot after JSON materializes collection instances.
    public bool Equals(AppSettings? other) => ReferenceEquals(this, other) || other is not null &&
        SchemaVersion == other.SchemaVersion && Culture == other.Culture && ActiveCharacterId == other.ActiveCharacterId &&
        Equals(Runtime, other.Runtime) && Equals(Movement, other.Movement) &&
        (Emotions is null ? other.Emotions is null : other.Emotions is not null && Emotions.SequenceEqual(other.Emotions)) &&
        MovementMode == other.MovementMode && HybridStrategy == other.HybridStrategy && DisplayPolicy == other.DisplayPolicy &&
        MotionStyle == other.MotionStyle && PerformanceMode == other.PerformanceMode && Equals(Logging, other.Logging) &&
        Equals(Security, other.Security) && Equals(PetWindow, other.PetWindow) && ControlCenterCloseBehavior == other.ControlCenterCloseBehavior &&
        Equals(Appearance, other.Appearance) && Equals(Hotkeys, other.Hotkeys) && Equals(Productivity, other.Productivity) &&
        Equals(AiTools, other.AiTools);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SchemaVersion); hash.Add(Culture); hash.Add(ActiveCharacterId); hash.Add(Runtime); hash.Add(Movement);
        if (Emotions is not null) foreach (var emotion in Emotions) hash.Add(emotion);
        hash.Add(MovementMode); hash.Add(HybridStrategy); hash.Add(DisplayPolicy); hash.Add(MotionStyle);
        hash.Add(PerformanceMode); hash.Add(Logging); hash.Add(Security); hash.Add(PetWindow); hash.Add(ControlCenterCloseBehavior);
        hash.Add(Appearance); hash.Add(Hotkeys); hash.Add(Productivity); hash.Add(AiTools);
        return hash.ToHashCode();
    }

    public bool IsValid() => SchemaVersion == CurrentSchemaVersion &&
        Culture is "zh-CN" or "en-US" &&
        Enum.IsDefined(MovementMode) && Enum.IsDefined(HybridStrategy) &&
        Enum.IsDefined(DisplayPolicy) && Enum.IsDefined(MotionStyle) && Enum.IsDefined(PerformanceMode) &&
        Logging is not null && Logging.IsValid() && Security is not null && Security.IsValid() &&
        PetWindow is not null && Movement is { IsValid: true } && Runtime is not null && Runtime.Behaviors is not null &&
        Emotions is not null && Emotions.Count <= MaxEmotionCheckpoints && Enum.IsDefined(ControlCenterCloseBehavior) &&
        Appearance is { IsValid: true } && Hotkeys is { IsValid: true } && Productivity is { IsValid: true } &&
        AiTools is { IsValid: true };
}
public sealed record LogOptions
{
    public long MaxFileBytes { get; init; } = 2 * 1024 * 1024;
    public int RetainedFiles { get; init; } = 14;
    public bool IsValid() => MaxFileBytes is >= 1024 and <= 100 * 1024 * 1024 && RetainedFiles is >= 1 and <= 90;
}
// Central limits shared by staging, validation and rendering.
public sealed record SecurityLimits
{
    public long MaxManifestBytes { get; init; } = 512 * 1024;
    public long MaxArchiveBytes { get; init; } = 100 * 1024 * 1024;
    public long MaxExpandedBytes { get; init; } = 500 * 1024 * 1024;
    public long MaxFileBytes { get; init; } = 20 * 1024 * 1024;
    public int MaxFiles { get; init; } = 5000;
    public int MaxImageDimension { get; init; } = 4096;
    public int MaxAnimationFrames { get; init; } = 1000;
    public bool IsValid() => MaxManifestBytes > 0 && MaxManifestBytes <= MaxFileBytes &&
        MaxArchiveBytes > 0 && MaxExpandedBytes >= MaxArchiveBytes &&
        MaxFileBytes > 0 && MaxFileBytes <= MaxExpandedBytes && MaxFiles > 0 &&
        MaxImageDimension > 0 && MaxAnimationFrames > 0;
}
public enum SettingsLoadStatus { Loaded, Created, RecoveredInvalid, Migrated }
public sealed record SettingsLoadResult(AppSettings Settings, SettingsLoadStatus Status);
public interface ISettingsService
{
    AppSettings Current { get; }
    Task<SettingsLoadResult> LoadAsync(CancellationToken ct);
    Task SaveAsync(AppSettings settings, CancellationToken ct);
    Task UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken ct);
}
