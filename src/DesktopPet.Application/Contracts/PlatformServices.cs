using DesktopPet.Domain.Platform;

namespace DesktopPet.Application.Contracts;

public enum AppPage { Home, AI, Pomodoro, Reminders, Statistics, Characters, Settings }
public interface IWindowService
{
    Task InitializeAsync(CancellationToken ct);
    Task ShowPetAsync(CancellationToken ct);
    Task HidePetAsync(CancellationToken ct);
    Task TogglePetAsync(CancellationToken ct);
    Task ShowControlCenterAsync(CancellationToken ct);
    Task CloseControlCenterAsync(CancellationToken ct);
    Task SavePositionAsync(CancellationToken ct);
    Task ExitAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
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
