using System.Windows;
using System.Windows.Media;
using DesktopPet.Application.Appearance;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Windows;
using AppThemeMode = DesktopPet.Application.Configuration.ThemeMode;

namespace DesktopPet.App.Appearance;

public sealed class WpfAppearanceService(ISettingsService settings, IUiDispatcher dispatcher) : IAppearanceService
{
    public AppThemeMode Current { get; private set; } = AppThemeMode.System;
    public event EventHandler? Changed;
    public Task InitializeAsync(CancellationToken ct) => ApplyCoreAsync(settings.Current.Appearance.Theme, false, ct);
    public Task ApplyAsync(AppThemeMode theme, CancellationToken ct) => ApplyCoreAsync(theme, true, ct);

    private async Task ApplyCoreAsync(AppThemeMode theme, bool persist, CancellationToken ct)
    {
        if (!Enum.IsDefined(theme)) throw new ArgumentOutOfRangeException(nameof(theme));
        if (persist) await settings.UpdateAsync(current => current with { Appearance = new() { Theme = theme } }, ct);
        await dispatcher.InvokeAsync(() =>
        {
            var resources = System.Windows.Application.Current?.Resources ?? throw new InvalidOperationException("WPF resources are unavailable.");
            var palette = theme switch
            {
                AppThemeMode.Light => new Palette("#FFF7F8FC", "#FFFFFFFF", "#FF1B1B1F", "#FF5D5F69", "#FFE2E4EC", "#FF6750A4", "#FFEADDFF", "#FFB3261E"),
                AppThemeMode.Dark => new Palette("#FF121318", "#FF1C1D24", "#FFF3F0FA", "#FFCAC4D0", "#FF3D3F49", "#FFD0BCFF", "#FF4F378B", "#FFF2B8B5"),
                _ => new Palette(SystemColors.WindowColor, SystemColors.ControlColor, SystemColors.WindowTextColor,
                    SystemColors.GrayTextColor, SystemColors.ActiveBorderColor, SystemColors.HighlightColor,
                    SystemColors.ControlLightColor, SystemColors.ControlTextColor)
            };
            resources["AppBackgroundBrush"] = Brush(palette.Background);
            resources["SurfaceBrush"] = Brush(palette.Surface);
            resources["PrimaryTextBrush"] = Brush(palette.Text);
            resources["SecondaryTextBrush"] = Brush(palette.Secondary);
            resources["BorderBrush"] = Brush(palette.Border);
            resources["AccentBrush"] = Brush(palette.Accent);
            resources["AccentSoftBrush"] = Brush(palette.AccentSoft);
            resources["DangerBrush"] = Brush(palette.Danger);
            Current = theme;
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }, ct);
    }

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
    private static Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value);
    private sealed record Palette(Color Background, Color Surface, Color Text, Color Secondary, Color Border, Color Accent, Color AccentSoft, Color Danger)
    {
        public Palette(string background, string surface, string text, string secondary, string border, string accent, string accentSoft, string danger)
            : this(Parse(background), Parse(surface), Parse(text), Parse(secondary), Parse(border), Parse(accent), Parse(accentSoft), Parse(danger)) { }
    }
}
