using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Localization;
using DesktopPet.Application.Movement;
using DesktopPet.Application.Runtime;
using DesktopPet.Domain.Pets;
using DesktopPet.Domain.Movement;

namespace DesktopPet.App.ViewModels;

public sealed record MovementOption<T>(T Value, string Label);
public sealed class MovementToolsViewModel : ObservableViewModel, IDisposable
{
    private readonly PetHost _pets;
    private readonly ISettingsService _settings;
    private readonly IDisplayTopologyService _displays;
    private readonly IMouseInteractionService _input;
    private readonly ITextLocalizer _text;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly AsyncActionCommand[] _commands;
    private string _details = "";
    public MovementToolsViewModel(PetHost pets, ISettingsService settings, IDisplayTopologyService displays,
        IMouseInteractionService input, ITextLocalizer text, IExceptionHandler exceptions)
    {
        _pets = pets; _settings = settings; _displays = displays; _input = input; _text = text;
        Modes = [Option(MovementMode.Fixed, TextKey.MovementFixed), Option(MovementMode.Local, TextKey.MovementLocal),
            Option(MovementMode.Desktop, TextKey.MovementDesktop), Option(MovementMode.Hybrid, TextKey.MovementHybrid)];
        Displays = [Option(DisplayPolicy.PrimaryOnly, TextKey.DisplayPrimary), Option(DisplayPolicy.LockedCurrent, TextKey.DisplayCurrent),
            Option(DisplayPolicy.SelectedMonitors, TextKey.DisplaySelected), Option(DisplayPolicy.AllMonitors, TextKey.DisplayAll)];
        Styles = [Option(MotionStyle.Quiet, TextKey.MotionQuiet), Option(MotionStyle.Natural, TextKey.MotionNatural), Option(MotionStyle.Lively, TextKey.MotionLively)];
        AsyncActionCommand Command(Func<Task> action) => new(action, e =>
        { exceptions.Report(e, ErrorCode.CommandFailed, ErrorOrigin.Command); Details = text.Get(TextKey.CommandFailed); });
        ApplyCommand = Command(async () =>
        {
            await pets.Runtime.ApplyMovementSettingsAsync(Mode, Hybrid, Display, Style,
                SelectedIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), _lifetime.Token);
            Refresh();
        });
        InteractiveCommand = Command(async () => { await input.SetModeAsync(MouseInteractionMode.Interactive, _lifetime.Token); Refresh(); });
        ClickThroughCommand = Command(async () => { await input.ToggleAsync(_lifetime.Token); Refresh(); });
        TemporaryCommand = Command(async () => { await input.SetModeAsync(MouseInteractionMode.TemporaryPassThrough, _lifetime.Token); Refresh(); });
        RefreshCommand = Command(() => { Refresh(); return Task.CompletedTask; });
        _commands = [ApplyCommand, InteractiveCommand, ClickThroughCommand, TemporaryCommand, RefreshCommand];
    }
    private MovementOption<T> Option<T>(T value, TextKey key) => new(value, _text.Get(key));
    public string Heading => _text.Get(TextKey.MovementTools);
    public string Hint => _text.Get(TextKey.MovementHint);
    public string ApplyText => _text.Get(TextKey.MovementApply);
    public string InteractiveText => _text.Get(TextKey.SetInteractive);
    public string ClickThroughText => _text.Get(TextKey.ToggleClickThrough);
    public string TemporaryText => _text.Get(TextKey.TemporaryClickThrough);
    public string RefreshText => _text.Get(TextKey.CharacterRefresh);
    public IReadOnlyList<MovementOption<MovementMode>> Modes { get; }
    public IReadOnlyList<MovementOption<DisplayPolicy>> Displays { get; }
    public IReadOnlyList<MovementOption<MotionStyle>> Styles { get; }
    public IReadOnlyList<HybridMovementStrategy> Hybrids { get; } = Enum.GetValues<HybridMovementStrategy>();
    public MovementMode Mode { get; set; }
    public HybridMovementStrategy Hybrid { get; set; }
    public DisplayPolicy Display { get; set; }
    public MotionStyle Style { get; set; }
    public string SelectedIds { get; set; } = "";
    public string Details { get => _details; private set { _details = value; OnPropertyChanged(); } }
    public AsyncActionCommand ApplyCommand { get; }
    public AsyncActionCommand InteractiveCommand { get; }
    public AsyncActionCommand ClickThroughCommand { get; }
    public AsyncActionCommand TemporaryCommand { get; }
    public AsyncActionCommand RefreshCommand { get; }
    public void Initialize()
    {
        var s = _settings.Current;
        Mode = s.MovementMode; Hybrid = s.HybridStrategy; Display = s.DisplayPolicy; Style = s.MotionStyle;
        SelectedIds = string.Join(", ", s.Movement.SelectedDisplays);
        OnPropertyChanged(string.Empty);
        Refresh();
    }
    private void Refresh()
    {
        var state = _pets.Runtime.Movement;
        Details = string.Format(_text.Culture, _text.Get(TextKey.MovementSummary), state?.IsMoving, state?.Home?.Position,
            state?.Target, state?.Facing, _input.Mode) + "\n" +
            string.Join("\n", _displays.GetTopology().Displays.Select(d => $"{d.Id}: {d.WorkingArea}; DPI ×{d.Dpi.X:F2}"));
    }
    public async Task StopAsync() { _lifetime.Cancel(); await Task.WhenAll(_commands.Select(c => c.Completion)); }
    public void Dispose() { _lifetime.Cancel(); _lifetime.Dispose(); }
}
