using DesktopPet.Application.Localization;

namespace DesktopPet.App.ViewModels;

public sealed class PetWindowViewModel(ITextLocalizer text)
{
    public string Title => text.Get(TextKey.PetTitle);
    public string Hint => text.Get(TextKey.PetHint);
}
