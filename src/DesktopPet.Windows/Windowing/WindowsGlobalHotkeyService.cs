using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using DesktopPet.Application.Commands;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Windows;

namespace DesktopPet.Windows.Windowing;

public sealed class WindowsGlobalHotkeyService(IUiDispatcher dispatcher) : IHotkeyService
{
    private const int WmHotkey = 0x0312;
    private const uint NoRepeat = 0x4000;
    private readonly Dictionary<CommandId, Registration> _registrations = [];
    private readonly Dictionary<int, CommandId> _byId = [];
    private HwndSource? _source;
    private int _nextId = 0x5100;
    private bool _disposed;
    public IReadOnlyCollection<CommandId> RegisteredCommands => _registrations.Keys.ToArray();
    public event EventHandler<HotkeyInvokedEventArgs>? Invoked;

    public Task<HotkeyRegistrationResult> RegisterAsync(HotkeyCommandBinding binding, CancellationToken ct)
    {
        if (!binding.IsValid || !binding.Enabled)
            return Task.FromResult(new HotkeyRegistrationResult(HotkeyRegistrationStatus.Invalid, "InvalidBinding"));
        return ResultOnUiAsync(() =>
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureWindow();
            if (_registrations.ContainsKey(binding.Command)) return new(HotkeyRegistrationStatus.Conflict, "DuplicateCommand");
            var id = _nextId++;
            if (!RegisterHotKey(_source!.Handle, id, (uint)binding.Gesture.Modifiers | NoRepeat, (uint)binding.Gesture.Key))
            {
                var error = Marshal.GetLastWin32Error();
                return error == 1409
                    ? new(HotkeyRegistrationStatus.Conflict, "HotkeyAlreadyInUse")
                    : new(HotkeyRegistrationStatus.SystemRejected, new Win32Exception(error).NativeErrorCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            _registrations.Add(binding.Command, new(id, binding));
            _byId.Add(id, binding.Command);
            return new(HotkeyRegistrationStatus.Registered);
        }, ct);
    }

    public Task UnregisterAsync(CommandId command, CancellationToken ct) => dispatcher.InvokeAsync(() =>
    {
        if (_registrations.Remove(command, out var registration))
        {
            if (_source is not null) UnregisterHotKey(_source.Handle, registration.Id);
            _byId.Remove(registration.Id);
        }
        return Task.CompletedTask;
    }, ct);

    public Task UnregisterAllAsync(CancellationToken ct) => dispatcher.InvokeAsync(() =>
    {
        if (_source is not null)
            foreach (var registration in _registrations.Values) UnregisterHotKey(_source.Handle, registration.Id);
        _registrations.Clear();
        _byId.Clear();
        return Task.CompletedTask;
    }, ct);

    private async Task<HotkeyRegistrationResult> ResultOnUiAsync(Func<HotkeyRegistrationResult> action, CancellationToken ct)
    {
        HotkeyRegistrationResult? result = null;
        await dispatcher.InvokeAsync(() => { result = action(); return Task.CompletedTask; }, ct);
        return result!;
    }
    private void EnsureWindow()
    {
        if (_source is not null) return;
        var parameters = new HwndSourceParameters("DesktopPet.Hotkeys", 0, 0)
        {
            Width = 0,
            Height = 0,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = 0
        };
        _source = new(parameters);
        _source.AddHook(Hook);
    }
    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmHotkey && _byId.TryGetValue(unchecked((int)wParam), out var command))
        {
            handled = true;
            Invoked?.Invoke(this, new(command));
        }
        return 0;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_source is not null)
        {
            foreach (var registration in _registrations.Values) UnregisterHotKey(_source.Handle, registration.Id);
            _source.RemoveHook(Hook);
            _source.Dispose();
        }
        _registrations.Clear();
        _byId.Clear();
    }
    private sealed record Registration(int Id, HotkeyCommandBinding Binding);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);
}
