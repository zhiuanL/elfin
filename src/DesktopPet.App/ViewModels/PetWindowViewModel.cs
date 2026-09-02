using DesktopPet.Application.Localization;
using DesktopPet.Windows.Characters;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Windows;

namespace DesktopPet.App.ViewModels;

public sealed class PetWindowViewModel(ITextLocalizer text, IUiDispatcher? dispatcher = null,
    ICharacterImageSource? character = null) : ObservableViewModel, IPetBubbleService, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _bubble;
    private string? _bubbleText;
    private bool _disposed;
    public ICharacterImageSource? Character => character;
    public string Title => text.Get(TextKey.PetTitle);
    public string Hint => text.Get(TextKey.PetHint);
    public string? BubbleText { get => _bubbleText; private set { _bubbleText = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsBubbleVisible)); } }
    public bool IsBubbleVisible => !string.IsNullOrWhiteSpace(BubbleText);
    public async Task ShowAsync(string message, CancellationToken ct)
    {
        _bubble?.Cancel(); _bubble?.Dispose();
        _bubble = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        var token = _bubble.Token;
        if (dispatcher is null) BubbleText = message;
        else await dispatcher.InvokeAsync(() => { BubbleText = message; return Task.CompletedTask; }, token);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(3 + message.Length / 12.0, 3, 8)), token);
            if (dispatcher is null) BubbleText = null;
            else await dispatcher.InvokeAsync(() => { BubbleText = null; return Task.CompletedTask; }, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel(); _bubble?.Cancel(); _bubble?.Dispose(); _lifetime.Dispose();
    }
}
