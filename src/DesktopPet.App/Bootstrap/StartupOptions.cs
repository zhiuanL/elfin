using System.IO;
using DesktopPet.Application.Storage;

namespace DesktopPet.App.Bootstrap;

public sealed record StartupOptions(DeploymentMode Mode, bool SmokeTest, string? SmokeDataRoot)
{
    public static StartupOptions Parse(string[] args)
    {
        var mode = DeploymentMode.Installed;
        var smoke = false;
        string? root = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--portable": mode = DeploymentMode.Portable; break;
                case "--smoke-test": smoke = true; break;
                case "--data-root" when i + 1 < args.Length: root = args[++i]; break;
                default: throw new ArgumentException("Unsupported startup argument.");
            }
        }
        if (root is not null && (!smoke || !Path.IsPathFullyQualified(root)))
            throw new ArgumentException("An absolute --data-root requires --smoke-test.");
        if (smoke && root is null) throw new ArgumentException("Smoke tests require an isolated --data-root.");
        return new(mode, smoke, root);
    }
}
