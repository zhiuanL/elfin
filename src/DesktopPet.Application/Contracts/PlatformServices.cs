using DesktopPet.Domain.Platform;

namespace DesktopPet.Application.Contracts;

public enum AppPage { Home, AI, Pomodoro, Reminders, Statistics, Characters, Settings }
public interface IWindowService
{
    void ShowControlCenter(AppPage page);
    void SetPetVisible(bool visible);
}
public interface IDisplayTopologyService
{
    DisplayTopology GetTopology();
    event EventHandler? TopologyChanged;
}
public interface ISessionStateService
{
    SessionState State { get; }
    event EventHandler? StateChanged;
}
public sealed record HotkeyBinding(string CommandId, uint VirtualKey, uint Modifiers);
public sealed record HotkeyRegistrationResult(bool IsRegistered, string? ErrorCode);
public interface IHotkeyService : IDisposable
{
    HotkeyRegistrationResult Register(HotkeyBinding binding);
    void Unregister(string commandId);
}
public interface INotificationService
{
    Task ShowAsync(string title, string message, CancellationToken ct);
}
