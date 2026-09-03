using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using DesktopPet.AI.Contracts;
using DesktopPet.Application.Contracts;

namespace DesktopPet.AI.Security;

public sealed class AiCredentialVault(ISecretStore persistent) : IAiCredentialVault, IDisposable
{
    private readonly ConcurrentDictionary<string, byte[]> _session = new(StringComparer.Ordinal);
    public async Task<SecretReference> StoreAsync(Guid profileId, ReadOnlyMemory<char> key,
        CredentialPersistence persistence, CancellationToken ct)
    {
        if (key.IsEmpty || key.Span.IsWhiteSpace()) throw new ArgumentException("API key is required.", nameof(key));
        var chars = key.ToArray();
        var bytes = Encoding.UTF8.GetBytes(chars);
        try
        {
            var reference = new SecretReference($"{(persistence == CredentialPersistence.Saved ? "saved" : "session")}:{profileId:D}");
            if (persistence == CredentialPersistence.Saved)
            {
                await persistent.StoreAsync(reference, bytes, ct);
                RemoveSession($"session:{profileId:D}");
            }
            else
            {
                await persistent.DeleteAsync(new($"saved:{profileId:D}"), ct);
                var copy = bytes.ToArray();
                _session.AddOrUpdate(reference.Value, copy, (_, old) => { CryptographicOperations.ZeroMemory(old); return copy; });
            }
            return reference;
        }
        finally { Array.Clear(chars); CryptographicOperations.ZeroMemory(bytes); }
    }
    public Task<byte[]?> ReadAsync(SecretReference reference, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (reference.Value.StartsWith("session:", StringComparison.Ordinal))
            return Task.FromResult(_session.TryGetValue(reference.Value, out var value) ? value.ToArray() : null);
        return persistent.ReadAsync(reference, ct);
    }
    public async Task DeleteAsync(SecretReference reference, CancellationToken ct)
    {
        if (reference.Value.StartsWith("session:", StringComparison.Ordinal)) RemoveSession(reference.Value);
        else await persistent.DeleteAsync(reference, ct);
    }
    private void RemoveSession(string key)
    {
        if (_session.TryRemove(key, out var bytes)) CryptographicOperations.ZeroMemory(bytes);
    }
    public void Dispose() { foreach (var key in _session.Keys) RemoveSession(key); }
}

internal static class SpanTextExtensions
{
    public static bool IsWhiteSpace(this ReadOnlySpan<char> value)
    {
        foreach (var character in value) if (!char.IsWhiteSpace(character)) return false;
        return true;
    }
}
