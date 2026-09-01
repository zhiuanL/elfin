using System.Collections.ObjectModel;
using DesktopPet.Application.Commands;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Hotkeys;
using DesktopPet.Application.Localization;

namespace DesktopPet.App.ViewModels;

public sealed record HotkeyModifierOption(HotkeyModifiers Value, string Label);
public sealed class HotkeyApplyRequestEventArgs(HotkeySettings settings) : EventArgs
{
    private readonly TaskCompletionSource<HotkeyApplyResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public HotkeySettings Settings { get; } = settings;
    public Task<HotkeyApplyResult> Completion => _completion.Task;
    public void Complete(HotkeyApplyResult result) => _completion.TrySetResult(result);
}
public sealed class HotkeyBindingViewModel
{
    public required CommandId Command { get; init; }
    public required string Label { get; init; }
    public bool Enabled { get; set; }
    public HotkeyModifiers Modifiers { get; set; }
    public HotkeyKey Key { get; set; }
    public HotkeyCommandBinding ToBinding() => new() { Command = Command, Enabled = Enabled, Gesture = new() { Modifiers = Modifiers, Key = Key } };
}

public sealed class HotkeysViewModel : ObservableViewModel, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly ITextLocalizer _text;
    private readonly IExceptionHandler _exceptions;
    private readonly CancellationTokenSource _lifetime = new();
    private string _notice = string.Empty;

    public HotkeysViewModel(ISettingsService settings, ITextLocalizer text, IExceptionHandler exceptions)
    {
        _settings = settings; _text = text; _exceptions = exceptions;
        Modifiers =
        [
            new(HotkeyModifiers.Control | HotkeyModifiers.Alt, "Ctrl + Alt"),
            new(HotkeyModifiers.Control | HotkeyModifiers.Shift, "Ctrl + Shift"),
            new(HotkeyModifiers.Alt | HotkeyModifiers.Shift, "Alt + Shift"),
            new(HotkeyModifiers.Control, "Ctrl"), new(HotkeyModifiers.Alt, "Alt"), new(HotkeyModifiers.Shift, "Shift")
        ];
        Keys = Enum.GetValues<HotkeyKey>().Where(key => key != HotkeyKey.None).ToArray();
        ApplyCommand = new(ApplyAsync, Report);
        ResetCommand = new(ResetAsync, Report);
        _text.CultureChanged += OnChanged;
        Reload(_settings.Current.Hotkeys);
    }

    public ObservableCollection<HotkeyBindingViewModel> Bindings { get; } = [];
    public IReadOnlyList<HotkeyModifierOption> Modifiers { get; }
    public IReadOnlyList<HotkeyKey> Keys { get; }
    public string Title => _text.Get(TextKey.HotkeysTitle);
    public string Subtitle => _text.Get(TextKey.HotkeysSubtitle);
    public string EnabledText => _text.Get(TextKey.HotkeyEnabled);
    public string CommandText => _text.Get(TextKey.HotkeyCommand);
    public string ModifiersText => _text.Get(TextKey.HotkeyModifiers);
    public string KeyText => _text.Get(TextKey.HotkeyKey);
    public string ApplyText => _text.Get(TextKey.HotkeyApply);
    public string ResetText => _text.Get(TextKey.HotkeyReset);
    public string Notice { get => _notice; private set { _notice = value; OnPropertyChanged(); } }
    public AsyncActionCommand ApplyCommand { get; }
    public AsyncActionCommand ResetCommand { get; }
    public event EventHandler<HotkeyApplyRequestEventArgs>? ApplyRequested;
    public void Initialize() => Reload(_settings.Current.Hotkeys);

    private async Task ApplyAsync()
    {
        var requested = new HotkeySettings { Bindings = Bindings.Select(item => item.ToBinding()).ToArray() };
        var result = await RequestAsync(requested);
        Notice = result.Succeeded ? _text.Get(TextKey.HotkeySaved) :
            result.ErrorCode is "InvalidOrDuplicateBinding" ? _text.Get(TextKey.HotkeyInvalid) :
            $"{_text.Get(TextKey.HotkeyConflict)} ({result.FailedCommand}: {result.ErrorCode})";
        if (result.Succeeded) Reload(requested);
    }
    private async Task ResetAsync()
    {
        var defaults = new HotkeySettings();
        var result = await RequestAsync(defaults);
        Notice = result.Succeeded ? _text.Get(TextKey.HotkeySaved) : $"{_text.Get(TextKey.HotkeyConflict)} ({result.ErrorCode})";
        if (result.Succeeded) Reload(defaults);
    }
    private async Task<HotkeyApplyResult> RequestAsync(HotkeySettings requested)
    {
        var request = new HotkeyApplyRequestEventArgs(requested);
        ApplyRequested?.Invoke(this, request);
        if (ApplyRequested is null) return new(false, null, "HotkeyCoordinatorUnavailable");
        return await request.Completion.WaitAsync(_lifetime.Token);
    }
    private void Reload(HotkeySettings current)
    {
        Bindings.Clear();
        foreach (var binding in current.Bindings)
            Bindings.Add(new() { Command = binding.Command, Label = Label(binding.Command), Enabled = binding.Enabled,
                Modifiers = binding.Gesture.Modifiers, Key = binding.Gesture.Key });
    }
    private string Label(CommandId id) => _text.Get(id switch
    {
        CommandId.ShowPet => TextKey.HotkeyShowPet,
        CommandId.HidePet => TextKey.HotkeyHidePet,
        CommandId.OpenControlCenter => TextKey.HotkeyOpenControlCenter,
        CommandId.TogglePetVisibility => TextKey.HotkeyTogglePet,
        CommandId.ToggleClickThrough => TextKey.HotkeyToggleClickThrough,
        CommandId.TemporaryClickThrough => TextKey.HotkeyTemporaryClickThrough,
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    });
    private void Report(Exception exception)
    {
        _exceptions.Report(exception, ErrorCode.CommandFailed, ErrorOrigin.Command);
        Notice = _text.Get(TextKey.CommandFailed);
    }
    private void OnChanged(object? sender, EventArgs e) { OnPropertyChanged(string.Empty); Reload(_settings.Current.Hotkeys); }
    public async Task StopAsync() { _lifetime.Cancel(); await Task.WhenAll(ApplyCommand.Completion, ResetCommand.Completion); }
    public void Dispose() { _text.CultureChanged -= OnChanged; _lifetime.Cancel(); _lifetime.Dispose(); }
}
