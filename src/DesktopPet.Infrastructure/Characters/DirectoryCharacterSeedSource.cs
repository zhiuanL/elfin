using DesktopPet.Application.Characters;

namespace DesktopPet.Infrastructure.Characters;

public sealed class DirectoryCharacterSeedSource(string root) : ICharacterSeedSource
{
    public IReadOnlyList<string> GetDirectories() => Directory.Exists(root) ? Directory.GetDirectories(root).Order(StringComparer.Ordinal).ToArray() : [];
}
