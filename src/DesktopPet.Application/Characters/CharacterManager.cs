using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.CharacterSdk;
using DesktopPet.Domain.Pets;

namespace DesktopPet.Application.Characters;

public sealed class CharacterManager(ICharacterPackageStore store, ISettingsService settings) : ICharacterPackageService, IDisposable
{
    private readonly SemaphoreSlim _selection = new(1, 1);
    public Task<CharacterDiscovery> DiscoverAsync(CancellationToken ct) => store.DiscoverAsync(ct);
    public async Task<ValidationResult> ValidateAsync(string sourcePath, CancellationToken ct) =>
        (await store.InspectAsync(sourcePath, false, ct)).Validation;
    public Task<CharacterOperationResult> ImportAsync(string sourcePath, CancellationToken ct) => store.InspectAsync(sourcePath, true, ct);
    public Task<CharacterOperationResult> InstallAsync(string sourcePath, CancellationToken ct) => ImportAsync(sourcePath, ct);
    public async Task<IReadOnlyList<CharacterPackage>> ListAsync(CancellationToken ct) => (await DiscoverAsync(ct)).Packages;
    public Task<CharacterOperationResult> GetAsync(CharacterId id, CancellationToken ct) => store.GetAsync(id, ct);
    public async Task<CharacterOperationResult> ActivateAsync(CharacterId id, CancellationToken ct)
    {
        await _selection.WaitAsync(ct);
        try
        {
            var result = await store.GetAsync(id, ct);
            if (result.Succeeded) await settings.UpdateAsync(current => current with { ActiveCharacterId = id.Value }, ct);
            return result;
        }
        finally { _selection.Release(); }
    }
    public async Task<ValidationResult> RemoveAsync(CharacterId id, CancellationToken ct)
    {
        await _selection.WaitAsync(ct);
        try
        {
            if (string.Equals(settings.Current.ActiveCharacterId, id.Value, StringComparison.Ordinal))
                return ValidationResult.Reject(CharacterErrorCode.ActiveCharacter, id.Value, "Activate another character before removing the active character.");
            return await store.RemoveAsync(id, ct);
        }
        finally { _selection.Release(); }
    }
    public void Dispose() => _selection.Dispose();
}
