using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.Domain.Platform;

namespace DesktopPet.Application.Windows;

/// <summary>Offline window policy. All platform access is through UI-dispatched ports.</summary>
public sealed class WindowLifecycleService(ISettingsService settings, IPetWindow pet,
    IControlCenterWindow controlCenter, IDisplayService displays, ITrayService tray,
    IUiDispatcher dispatcher, WindowPlacementPolicy placement, IAppLifetime lifetime) : IWindowService, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;
    private bool _stopped;

    public Task InitializeAsync(CancellationToken ct) => RunAsync(async () =>
    {
        if (_initialized) return;
        pet.EnsureCreated();
        ApplyPosition(settings.Current.PetWindow.Position);
        pet.SetTopmost(settings.Current.PetWindow.Topmost);
        tray.Start();
        if (settings.Current.PetWindow.IsVisible) pet.Show();
        controlCenter.Show();
        _initialized = true;
        await PersistAsync(ct);
    }, ct);

    public Task ShowPetAsync(CancellationToken ct) => RunAsync(async () =>
    {
        RequireInitialized();
        ApplyPosition(CurrentPosition());
        pet.Show();
        await PersistAsync(ct);
    }, ct);

    public Task HidePetAsync(CancellationToken ct) => RunAsync(async () =>
    {
        RequireInitialized();
        pet.Hide();
        await PersistAsync(ct);
    }, ct);

    public Task TogglePetAsync(CancellationToken ct) => RunAsync(async () =>
    {
        RequireInitialized();
        if (pet.IsVisible) pet.Hide();
        else { ApplyPosition(CurrentPosition()); pet.Show(); }
        await PersistAsync(ct);
    }, ct);

    public Task ShowControlCenterAsync(CancellationToken ct) => RunAsync(() =>
    {
        RequireInitialized();
        controlCenter.Show();
        return Task.CompletedTask;
    }, ct);

    public Task CloseControlCenterAsync(CancellationToken ct) => RunAsync(() =>
    {
        RequireInitialized();
        if (settings.Current.ControlCenterCloseBehavior == ControlCenterCloseBehavior.Exit)
            lifetime.RequestShutdown();
        else controlCenter.Hide();
        return Task.CompletedTask;
    }, ct);

    public Task SavePositionAsync(CancellationToken ct) => RunAsync(async () =>
    {
        if (!_initialized) return;
        ApplyPosition(CurrentPosition());
        await PersistAsync(ct);
    }, ct);

    public Task SetTopmostAsync(bool topmost, CancellationToken ct) => RunAsync(async () =>
    {
        RequireInitialized();
        pet.SetTopmost(topmost);
        await settings.UpdateAsync(current => current with
        {
            PetWindow = current.PetWindow with { Topmost = topmost }
        }, ct);
    }, ct);

    public Task ExitAsync(CancellationToken ct) => dispatcher.InvokeAsync(() =>
    {
        ct.ThrowIfCancellationRequested();
        lifetime.RequestShutdown();
        return Task.CompletedTask;
    }, ct);

    public Task StopAsync(CancellationToken ct) => dispatcher.InvokeAsync(async () =>
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_stopped) return;
            _stopped = true;
            // Cleanup must still run if saving fails; the host reports the failure and exits nonzero.
            try { if (_initialized) await PersistAsync(ct); }
            finally
            {
                try { tray.Dispose(); }
                finally
                {
                    try { pet.Close(); }
                    finally { controlCenter.Close(); }
                }
            }
        }
        finally { _gate.Release(); }
    }, ct);

    private Task RunAsync(Func<Task> action, CancellationToken ct) => dispatcher.InvokeAsync(async () =>
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_stopped || lifetime.IsShuttingDown) return;
            await action();
        }
        finally { _gate.Release(); }
    }, ct);

    private void RequireInitialized()
    {
        if (!_initialized) throw new InvalidOperationException("Window infrastructure is not initialized.");
    }

    private SavedWindowPosition CurrentPosition()
    {
        var bounds = pet.Bounds;
        return new(new(bounds.X, bounds.Y), settings.Current.PetWindow.Position?.DisplayId);
    }

    private void ApplyPosition(SavedWindowPosition? saved)
    {
        var areas = displays.GetDisplays();
        var bounds = pet.Bounds;
        var resolved = placement.Resolve(saved, new(bounds.Width, bounds.Height), areas);
        pet.MoveTo(resolved.Origin);
        // Moving to another monitor can change the HWND's DPI and thus its physical size.
        bounds = pet.Bounds;
        resolved = placement.Resolve(new(resolved.Origin, resolved.DisplayId), new(bounds.Width, bounds.Height), areas);
        if (bounds.X != resolved.Origin.X || bounds.Y != resolved.Origin.Y) pet.MoveTo(resolved.Origin);
    }

    private Task PersistAsync(CancellationToken ct)
    {
        var bounds = pet.Bounds;
        var resolved = placement.Resolve(CurrentPosition(), new(bounds.Width, bounds.Height), displays.GetDisplays());
        var position = new SavedWindowPosition(new(bounds.X, bounds.Y), resolved.DisplayId);
        var visible = pet.IsVisible;
        return settings.UpdateAsync(current => current with
        {
            PetWindow = current.PetWindow with { Position = position, IsVisible = visible }
        }, ct);
    }

    public void Dispose() => _gate.Dispose();
}
