using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Localization;
using DesktopPet.Application.Runtime;
using DesktopPet.Application.Windows;

namespace DesktopPet.App.ViewModels;

public sealed class RuntimeDiagnosticsViewModel : ObservableViewModel, IDisposable
{
    private readonly PetHost _host;
    private readonly ITextLocalizer _text;
    private readonly IUiDispatcher _dispatcher;
    private readonly IExceptionHandler _exceptions;
    private readonly CancellationTokenSource _lifetime = new();
    private string _details = string.Empty;
    public RuntimeDiagnosticsViewModel(PetHost host, ITextLocalizer text, IUiDispatcher dispatcher, IExceptionHandler exceptions)
    {
        _host = host; _text = text; _dispatcher = dispatcher; _exceptions = exceptions;
        host.Runtime.Changed += OnChanged;
    }
    public string Heading => _text.Get(TextKey.RuntimeDiagnostics);
    public string Details { get => _details; private set { _details = value; OnPropertyChanged(); } }
    private async void OnChanged(object? sender, EventArgs e)
    {
        var snapshot = _host.Runtime.Diagnostic;
        try
        {
            await _dispatcher.InvokeAsync(() =>
            {
                Details = string.Format(_text.Culture, _text.Get(TextKey.RuntimeSummary), snapshot.State.Primary,
                    snapshot.State.Transient?.ToString() ?? snapshot.State.Behavior.ToString(), snapshot.State.Semantic.Value,
                    snapshot.Emotion.Mood.Value, snapshot.Emotion.Energy.Value, snapshot.Emotion.Boredom.Value, snapshot.Emotion.Affinity.Value,
                    snapshot.IsRunning, snapshot.IsVisible, snapshot.InteractionCount) + "\n" +
                    string.Join("\n", snapshot.Scores.Select(score => string.Format(_text.Culture, _text.Get(TextKey.RuntimeScore),
                        score.Behavior, score.FinalScore, score.Filter, score.CooldownRemaining.TotalSeconds,
                        score.BaseWeight, score.CharacterModifier, score.EmotionModifier, score.ContextModifier, score.UserModifier, score.RecentModifier))) +
                    "\n" + string.Format(_text.Culture, _text.Get(TextKey.RuntimeRecent),
                        string.Join(" → ", snapshot.Recent.RecentBehaviors.TakeLast(8).Select(item => item.Behavior)));
                return Task.CompletedTask;
            }, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) { _exceptions.Report(exception, ErrorCode.CommandFailed, ErrorOrigin.Dispatcher); }
    }
    public void Dispose() { _host.Runtime.Changed -= OnChanged; _lifetime.Cancel(); _lifetime.Dispose(); }
}
