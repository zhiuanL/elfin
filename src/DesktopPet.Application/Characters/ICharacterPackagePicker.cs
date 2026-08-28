namespace DesktopPet.Application.Characters;

public enum CharacterPackageSourceKind { Zip, Directory }

// Selection only: the chosen path still goes through the existing package validator/importer.
public interface ICharacterPackagePicker
{
    Task<string?> PickAsync(CharacterPackageSourceKind kind, CancellationToken ct);
}
