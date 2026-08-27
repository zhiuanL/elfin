using System.Security.Cryptography;
using System.Text;
using DesktopPet.Windows.Security;

namespace DesktopPet.Tests.Integration;

public sealed class WindowsSecurityTests
{
    [Fact]
    public void DpapiRoundTripsWithPurposeAndRejectsTampering()
    {
        var protection = new DpapiDataProtectionService();
        var plaintext = Encoding.UTF8.GetBytes("test-only-sensitive-value");
        var encrypted = protection.Protect(plaintext, "unit-test");
        Assert.NotEqual(plaintext, encrypted);
        Assert.Equal(plaintext, protection.Unprotect(encrypted, "unit-test"));
        Assert.Throws<CryptographicException>(() => protection.Unprotect(encrypted, "wrong-purpose"));
        encrypted[^1] ^= 0xff;
        Assert.Throws<CryptographicException>(() => protection.Unprotect(encrypted, "unit-test"));
    }
}
