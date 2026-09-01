using DesktopPet.Application.Commands;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Diagnostics;

namespace DesktopPet.Application.Hotkeys;

public sealed record HotkeyApplyResult(bool Succeeded, CommandId? FailedCommand = null, string? ErrorCode = null);
public interface IHotkeyCoordinator : IAsyncDisposable
{
    HotkeySettings Current { get; }
    event EventHandler? Changed;
    Task<HotkeyApplyResult> InitializeAsync(CancellationToken ct);
    Task<HotkeyApplyResult> ApplyAsync(HotkeySettings settings, CancellationToken ct);
    Task<HotkeyApplyResult> ResetAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}

public sealed class HotkeyCoordinator(IHotkeyService platform, ICommandRegistry commands, ISettingsService settings,
    IExceptionHandler exceptions) : IHotkeyCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _started;
    private bool _stopped;
    public HotkeySettings Current { get; private set; } = settings.Current.Hotkeys;
    public event EventHandler? Changed;

    public async Task<HotkeyApplyResult> InitializeAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_started) return new(true);
            _started = true;
            platform.Invoked += OnInvoked;
            var result = await RegisterSetAsync(settings.Current.Hotkeys, ct);
            if (result.Succeeded) Current = settings.Current.Hotkeys;
            else await platform.UnregisterAllAsync(CancellationToken.None);
            return result;
        }
        finally { _gate.Release(); }
    }

    public async Task<HotkeyApplyResult> ApplyAsync(HotkeySettings requested, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(requested);
        if (!requested.IsValid) return new(false, null, "InvalidOrDuplicateBinding");
        await _gate.WaitAsync(ct);
        try
        {
            if (_stopped) return new(false, null, "HotkeysStopped");
            var previous = Current;
            await platform.UnregisterAllAsync(ct);
            var result = await RegisterSetAsync(requested, ct);
            if (!result.Succeeded)
            {
                await platform.UnregisterAllAsync(CancellationToken.None);
                await RegisterSetAsync(previous, CancellationToken.None);
                return result;
            }
            try { await settings.UpdateAsync(current => current with { Hotkeys = requested }, ct); }
            catch
            {
                await platform.UnregisterAllAsync(CancellationToken.None);
                await RegisterSetAsync(previous, CancellationToken.None);
                throw;
            }
            Current = requested;
            Changed?.Invoke(this, EventArgs.Empty);
            return result;
        }
        finally { _gate.Release(); }
    }

    public Task<HotkeyApplyResult> ResetAsync(CancellationToken ct) => ApplyAsync(new(), ct);

    private async Task<HotkeyApplyResult> RegisterSetAsync(HotkeySettings requested, CancellationToken ct)
    {
        if (!requested.IsValid) return new(false, null, "InvalidOrDuplicateBinding");
        foreach (var binding in requested.Bindings.Where(item => item.Enabled))
        {
            var result = await platform.RegisterAsync(binding, ct);
            if (!result.IsRegistered) return new(false, binding.Command, result.ErrorCode ?? result.Status.ToString());
        }
        return new(true);
    }

    private async void OnInvoked(object? sender, HotkeyInvokedEventArgs e)
    {
        if (_stopped) return;
        try { await commands.ExecuteAsync(e.Command, CancellationToken.None); }
        catch (Exception exception) { exceptions.Report(exception, ErrorCode.CommandFailed, ErrorOrigin.Command); }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_stopped) return;
            _stopped = true;
            platform.Invoked -= OnInvoked;
            await platform.UnregisterAllAsync(ct);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _gate.Dispose();
    }
}
