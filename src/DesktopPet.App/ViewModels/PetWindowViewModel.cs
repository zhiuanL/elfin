using DesktopPet.Application.Localization;
using DesktopPet.Windows.Characters;

namespace DesktopPet.App.ViewModels;

public sealed class PetWindowViewModel(ITextLocalizer text, ICharacterImageSource? character = null)
{
    public ICharacterImageSource? Character => character;
    public string Title => text.Get(TextKey.PetTitle);
    public string Hint => text.Get(TextKey.PetHint);
}
