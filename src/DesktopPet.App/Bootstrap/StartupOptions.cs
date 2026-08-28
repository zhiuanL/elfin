using System.IO;
using DesktopPet.Application.Storage;

namespace DesktopPet.App.Bootstrap;

public sealed record StartupOptions(DeploymentMode Mode, bool SmokeTest, string? SmokeDataRoot, int SmokeDurationSeconds = 0)
{
    public static StartupOptions Parse(string[] args)
    {
        var mode = DeploymentMode.Installed;
        var smoke = false;
        string? root = null;
        var duration = 0;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--portable": mode = DeploymentMode.Portable; break;
                case "--smoke-test": smoke = true; break;
                case "--data-root" when i + 1 < args.Length: root = args[++i]; break;
                case "--smoke-duration-seconds" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out duration) || duration is < 0 or > 300)
                        throw new ArgumentException("Smoke duration must be between 0 and 300 seconds.");
                    break;
                default: throw new ArgumentException("Unsupported startup argument.");
            }
        }
        if (root is not null && (!smoke || !Path.IsPathFullyQualified(root)))
            throw new ArgumentException("An absolute --data-root requires --smoke-test.");
        if (smoke && root is null) throw new ArgumentException("Smoke tests require an isolated --data-root.");
        if (duration > 0 && !smoke) throw new ArgumentException("Smoke duration requires --smoke-test.");
        return new(mode, smoke, root, duration);
    }
}
