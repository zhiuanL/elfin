using DesktopPet.Application.Contracts;

namespace DesktopPet.Application.Navigation;

public sealed class NavigationChangedEventArgs(AppPage page) : EventArgs { public AppPage Page { get; } = page; }
public interface INavigationService
{
    AppPage Current { get; }
    event EventHandler<NavigationChangedEventArgs>? Changed;
    void Navigate(AppPage page);
}
public sealed class ControlCenterNavigationService : INavigationService
{
    private static readonly HashSet<AppPage> AvailablePages =
        [AppPage.Home, AppPage.AI, AppPage.Pomodoro, AppPage.Reminders, AppPage.Statistics,
         AppPage.Characters, AppPage.Settings, AppPage.Hotkeys, AppPage.Diagnostics];
    public AppPage Current { get; private set; } = AppPage.Home;
    public event EventHandler<NavigationChangedEventArgs>? Changed;
    public void Navigate(AppPage page)
    {
        if (!AvailablePages.Contains(page)) throw new ArgumentOutOfRangeException(nameof(page));
        if (Current == page) return;
        Current = page;
        Changed?.Invoke(this, new(page));
    }
}
