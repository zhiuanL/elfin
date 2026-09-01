using DesktopPet.Application.Localization;

namespace DesktopPet.App.ViewModels;

public sealed class DiagnosticsPageViewModel : ObservableViewModel, IDisposable
{
    private readonly ITextLocalizer _text;
    public DiagnosticsPageViewModel(ITextLocalizer text, RuntimeDiagnosticsViewModel runtime, CharacterToolsViewModel characters)
    {
        _text = text; Runtime = runtime; Characters = characters;
        _text.CultureChanged += OnChanged;
    }
    public string Title => _text.Get(TextKey.DiagnosticsTitle);
    public string Subtitle => _text.Get(TextKey.DiagnosticsSubtitle);
    public string OfflineStatus => _text.Get(TextKey.OfflineCoreStatus);
    public RuntimeDiagnosticsViewModel Runtime { get; }
    public CharacterToolsViewModel Characters { get; }
    private void OnChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
    public void Dispose() => _text.CultureChanged -= OnChanged;
}
