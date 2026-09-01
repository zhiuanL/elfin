using DesktopPet.Domain.Platform;
using DesktopPet.Application.Commands;
using DesktopPet.Application.Configuration;

namespace DesktopPet.Application.Contracts;

public enum AppPage { Home, AI, Pomodoro, Reminders, Statistics, Characters, Settings, Hotkeys, Diagnostics }
public interface IWindowService
{
    Task InitializeAsync(CancellationToken ct);
    Task ShowPetAsync(CancellationToken ct);
    Task HidePetAsync(CancellationToken ct);
    Task TogglePetAsync(CancellationToken ct);
    Task ShowControlCenterAsync(CancellationToken ct);
    Task CloseControlCenterAsync(CancellationToken ct);
    Task SavePositionAsync(CancellationToken ct);
    Task SetTopmostAsync(bool topmost, CancellationToken ct);
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
public enum HotkeyRegistrationStatus { Registered, Invalid, Conflict, SystemRejected }
public sealed record HotkeyRegistrationResult(HotkeyRegistrationStatus Status, string? ErrorCode = null)
{
    public bool IsRegistered => Status == HotkeyRegistrationStatus.Registered;
}
public sealed class HotkeyInvokedEventArgs(CommandId command) : EventArgs { public CommandId Command { get; } = command; }
public interface IHotkeyService : IDisposable
{
    IReadOnlyCollection<CommandId> RegisteredCommands { get; }
    event EventHandler<HotkeyInvokedEventArgs>? Invoked;
    Task<HotkeyRegistrationResult> RegisterAsync(HotkeyCommandBinding binding, CancellationToken ct);
    Task UnregisterAsync(CommandId command, CancellationToken ct);
    Task UnregisterAllAsync(CancellationToken ct);
}
public enum ConfirmationAction { RemoveCharacter }
public sealed record ConfirmationRequest(ConfirmationAction Action, string Subject);
public interface IUserConfirmationService
{
    Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken ct);
}
public interface INotificationService
{
    Task ShowAsync(string title, string message, CancellationToken ct);
}
