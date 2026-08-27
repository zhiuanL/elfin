namespace DesktopPet.Application.Contracts;

// Ciphertext only. Purpose separates usages; no cleartext persistence fallback is permitted.
public interface IDataProtectionService
{
    byte[] Protect(ReadOnlySpan<byte> plaintext, string purpose);
    byte[] Unprotect(ReadOnlySpan<byte> ciphertext, string purpose);
}
public readonly record struct SecretReference(string Value);
public interface ISecretStore
{
    Task StoreAsync(SecretReference reference, ReadOnlyMemory<byte> secret, CancellationToken ct);
    Task<byte[]?> ReadAsync(SecretReference reference, CancellationToken ct);
    Task DeleteAsync(SecretReference reference, CancellationToken ct);
}
public enum OptionalServiceStatus { Disabled }
public interface IUpdateService
{
    Task<OptionalServiceStatus> CheckAsync(CancellationToken ct);
}
public interface ISyncService
{
    Task<OptionalServiceStatus> SyncAsync(CancellationToken ct);
}
public sealed record CrashReport(Guid CorrelationId, string ErrorCode, DateTimeOffset TimestampUtc);
public interface ICrashReportingService
{
    Task ReportAsync(CrashReport report, CancellationToken ct);
}
[Flags]
public enum BackupContents { Settings = 1, BusinessData = 2, AiData = 4, Characters = 8 }
public interface IBackupService
{
    Task ExportAsync(string destination, BackupContents contents, ReadOnlyMemory<char> password, CancellationToken ct);
    Task RestoreAsync(string source, ReadOnlyMemory<char> password, CancellationToken ct);
}
