using System.Security.Cryptography;
using System.Text;
using DesktopPet.Application.Contracts;

namespace DesktopPet.Windows.Security;

public sealed class DpapiDataProtectionService : IDataProtectionService
{
    public byte[] Protect(ReadOnlySpan<byte> plaintext, string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        var buffer = plaintext.ToArray();
        try { return ProtectedData.Protect(buffer, Entropy(purpose), DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }
    public byte[] Unprotect(ReadOnlySpan<byte> ciphertext, string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        return ProtectedData.Unprotect(ciphertext.ToArray(), Entropy(purpose), DataProtectionScope.CurrentUser);
    }
    private static byte[] Entropy(string purpose) =>
        SHA256.HashData(Encoding.UTF8.GetBytes("DesktopPet/v1/" + purpose));
}
