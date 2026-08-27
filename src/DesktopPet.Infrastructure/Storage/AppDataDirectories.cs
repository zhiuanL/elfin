using DesktopPet.Application.Storage;

namespace DesktopPet.Infrastructure.Storage;

public sealed class AppDataDirectories : IAppDataDirectories
{
    public AppDataDirectories(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (!Path.IsPathFullyQualified(root)) throw new ArgumentException("An absolute data root is required.", nameof(root));
        Root = Path.GetFullPath(root);
    }
    public static AppDataDirectories Resolve(DeploymentMode mode, string appRoot, string localAppDataRoot) =>
        mode switch
        {
            DeploymentMode.Installed => new(Path.Combine(localAppDataRoot, "DesktopPet")),
            DeploymentMode.Portable => new(Path.Combine(appRoot, "UserData")),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    public string Root { get; }
    public string Config => Path.Combine(Root, "config");
    public string Data => Path.Combine(Root, "data");
    public string Characters => Path.Combine(Root, "characters");
    public string Cache => Path.Combine(Root, "cache");
    public string Logs => Path.Combine(Root, "logs");
    public string Backups => Path.Combine(Root, "backups");
    public void EnsureCreated()
    {
        foreach (var path in new[] { Root, Config, Data, Characters, Cache, Logs, Backups })
            Directory.CreateDirectory(path);
    }
}
