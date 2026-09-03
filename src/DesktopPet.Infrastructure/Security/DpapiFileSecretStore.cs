using System.Security.Cryptography;
using System.Text;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Storage;

namespace DesktopPet.Infrastructure.Security;

public sealed class DpapiFileSecretStore(IAppDataDirectories directories, IDataProtectionService protection) : ISecretStore
{
    private const string Purpose = "ai-provider-credential";
    private string Root => Path.Combine(directories.Config, "credentials");
    private string PathFor(SecretReference reference)
    {
        if (string.IsNullOrWhiteSpace(reference.Value)) throw new ArgumentException("Invalid secret reference.");
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reference.Value))) + ".secret";
        return Path.Combine(Root, name);
    }
    public async Task StoreAsync(SecretReference reference, ReadOnlyMemory<byte> secret, CancellationToken ct)
    {
        if (secret.IsEmpty) throw new ArgumentException("Secret cannot be empty.");
        Directory.CreateDirectory(Root);
        var encrypted = protection.Protect(secret.Span, Purpose);
        var path = PathFor(reference); var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllBytesAsync(temporary, encrypted, ct); File.Move(temporary, path, true); }
        finally { CryptographicOperations.ZeroMemory(encrypted); if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public async Task<byte[]?> ReadAsync(SecretReference reference, CancellationToken ct)
    {
        var path = PathFor(reference); if (!File.Exists(path)) return null;
        var encrypted = await File.ReadAllBytesAsync(path, ct);
        try { return protection.Unprotect(encrypted, Purpose); }
        finally { CryptographicOperations.ZeroMemory(encrypted); }
    }
    public Task DeleteAsync(SecretReference reference, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); var path = PathFor(reference); if (File.Exists(path)) File.Delete(path); return Task.CompletedTask; }
}
