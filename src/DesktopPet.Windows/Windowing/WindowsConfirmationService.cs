using System.Windows;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Localization;
using DesktopPet.Application.Windows;

namespace DesktopPet.Windows.Windowing;

public sealed class WindowsConfirmationService(IUiDispatcher dispatcher, ITextLocalizer text) : IUserConfirmationService
{
    public async Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken ct)
    {
        var confirmed = false;
        await dispatcher.InvokeAsync(() =>
        {
            var message = request.Action switch
            {
                ConfirmationAction.RemoveCharacter => string.Format(text.Culture, text.Get(TextKey.CharacterRemoveConfirm), request.Subject),
                _ => throw new ArgumentOutOfRangeException(nameof(request))
            };
            confirmed = MessageBox.Show(message, text.Get(TextKey.ConfirmTitle), MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
            return Task.CompletedTask;
        }, ct);
        return confirmed;
    }
}
