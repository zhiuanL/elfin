using DesktopPet.Application.Commands;

namespace DesktopPet.Application.Configuration;

public enum ThemeMode { System, Light, Dark }

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008
}

public enum HotkeyKey : uint
{
    None = 0,
    A = 0x41, B = 0x42, C = 0x43, D = 0x44, E = 0x45, F = 0x46, G = 0x47,
    H = 0x48, I = 0x49, J = 0x4A, K = 0x4B, L = 0x4C, M = 0x4D, N = 0x4E,
    O = 0x4F, P = 0x50, Q = 0x51, R = 0x52, S = 0x53, T = 0x54, U = 0x55,
    V = 0x56, W = 0x57, X = 0x58, Y = 0x59, Z = 0x5A,
    F1 = 0x70, F2 = 0x71, F3 = 0x72, F4 = 0x73, F5 = 0x74, F6 = 0x75,
    F7 = 0x76, F8 = 0x77, F9 = 0x78, F10 = 0x79, F11 = 0x7A, F12 = 0x7B
}

public sealed record HotkeyGesture
{
    private const HotkeyModifiers AllModifiers = HotkeyModifiers.Alt | HotkeyModifiers.Control |
        HotkeyModifiers.Shift | HotkeyModifiers.Windows;
    public HotkeyModifiers Modifiers { get; init; }
    public HotkeyKey Key { get; init; }
    public bool IsValid => Key != HotkeyKey.None && Enum.IsDefined(Key) && Modifiers != HotkeyModifiers.None &&
        (Modifiers & ~AllModifiers) == 0;
    public override string ToString() => $"{Modifiers}+{Key}";
}

public sealed record HotkeyDefinition(CommandId Command, HotkeyGesture DefaultGesture);

public sealed record HotkeyCommandBinding
{
    public CommandId Command { get; init; }
    public bool Enabled { get; init; } = true;
    public HotkeyGesture Gesture { get; init; } = new();
    public bool IsValid => HotkeyCatalog.IsSupported(Command) && Gesture is { IsValid: true };
}

public static class HotkeyCatalog
{
    public static IReadOnlyList<HotkeyDefinition> Definitions { get; } =
    [
        new(CommandId.ShowPet, Gesture(HotkeyKey.S)),
        new(CommandId.HidePet, Gesture(HotkeyKey.H)),
        new(CommandId.OpenControlCenter, Gesture(HotkeyKey.O)),
        new(CommandId.TogglePetVisibility, Gesture(HotkeyKey.P)),
        new(CommandId.StartOrPausePomodoro, Gesture(HotkeyKey.F)),
        new(CommandId.ToggleClickThrough, Gesture(HotkeyKey.T)),
        new(CommandId.TemporaryClickThrough, Gesture(HotkeyKey.I))
    ];
    public static bool IsSupported(CommandId command) => Definitions.Any(item => item.Command == command);
    public static IReadOnlyList<HotkeyCommandBinding> Defaults() => Definitions.Select(item => new HotkeyCommandBinding
        { Command = item.Command, Gesture = item.DefaultGesture }).ToArray();
    private static HotkeyGesture Gesture(HotkeyKey key) => new() { Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt, Key = key };
}

public sealed record HotkeySettings
{
    public IReadOnlyList<HotkeyCommandBinding> Bindings { get; init; } = HotkeyCatalog.Defaults();
    public bool IsValid => Bindings is { Count: > 0 and <= 32 } && Bindings.All(item => item is { IsValid: true }) &&
        Bindings.Select(item => item.Command).Distinct().Count() == Bindings.Count &&
        Bindings.Where(item => item.Enabled).Select(item => item.Gesture).Distinct().Count() == Bindings.Count(item => item.Enabled);
    public bool Equals(HotkeySettings? other) => ReferenceEquals(this, other) || other is not null &&
        (Bindings is null ? other.Bindings is null : other.Bindings is not null && Bindings.SequenceEqual(other.Bindings));
    public override int GetHashCode()
    {
        var hash = new HashCode();
        if (Bindings is not null) foreach (var binding in Bindings) hash.Add(binding);
        return hash.ToHashCode();
    }
}

public sealed record AppearanceSettings
{
    public ThemeMode Theme { get; init; } = ThemeMode.System;
    public bool IsValid => Enum.IsDefined(Theme);
}
