using System.Windows;
using DesktopPet.AI.Contracts;
using DesktopPet.Application.Windows;
using DesktopPet.Application.Localization;

namespace DesktopPet.App.Bootstrap;

public sealed class WpfToolConfirmationService(IUiDispatcher dispatcher, ITextLocalizer text) : IToolConfirmationService
{
    public async Task<bool> ConfirmAsync(ToolConfirmationRequest request, CancellationToken ct)
    {
        var accepted = false;
        await dispatcher.InvokeAsync(() =>
        {
            var message = string.Format(text.Culture, text.Get(TextKey.AiToolConfirmMessage),
                request.ToolId, request.Description, request.RiskLevel, request.ParameterSummary);
            accepted = MessageBox.Show(message, text.Get(TextKey.AiToolConfirmTitle), MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
            return Task.CompletedTask;
        }, ct);
        return accepted;
    }
}
