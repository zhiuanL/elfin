namespace DesktopPet.Application.Storage;

public enum DatabaseKind { App, Ai }
public enum DeploymentMode { Installed, Portable }
public interface IAppDataDirectories
{
    string Root { get; }
    string Config { get; }
    string Data { get; }
    string Characters { get; }
    string Cache { get; }
    string Logs { get; }
    string Backups { get; }
    void EnsureCreated();
}
public interface IDatabaseMigrator
{
    Task MigrateAsync(DatabaseKind database, CancellationToken ct);
}
