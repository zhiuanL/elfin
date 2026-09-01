using DesktopPet.Application.Configuration;

namespace DesktopPet.Application.Appearance;

public interface IAppearanceService
{
    ThemeMode Current { get; }
    event EventHandler? Changed;
    Task InitializeAsync(CancellationToken ct);
    Task ApplyAsync(ThemeMode theme, CancellationToken ct);
}
