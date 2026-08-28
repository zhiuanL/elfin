using DesktopPet.CharacterSdk;

namespace DesktopPet.Infrastructure.Characters;

internal static class PackageFiles
{
    public static void RejectLinks(string path)
    {
        var cursor = Path.GetFullPath(path);
        while (cursor.Length > 0)
        {
            if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new PackageInputException(CharacterErrorCode.LinkNotAllowed, "Links/reparse points are not allowed.");
            var parent = Path.GetDirectoryName(cursor);
            if (parent is null || parent == cursor) break;
            cursor = parent;
        }
    }
    public static IReadOnlyList<string> Enumerate(string root, int maxEntries)
    {
        RejectLinks(root);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((Path.GetFullPath(root), 0));
        var count = 0;
        while (pending.TryPop(out var current))
        {
            if (current.Depth > 32) throw new PackageInputException(CharacterErrorCode.ResourceLimit, "Directory nesting limit exceeded.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(current.Path))
            {
                if (++count > maxEntries) throw new PackageInputException(CharacterErrorCode.ResourceLimit, "Package entry count limit exceeded.");
                RejectLinks(entry);
                var relative = Path.GetRelativePath(root, entry).Replace('\\', '/');
                if (!PackagePath.IsSafe(relative)) throw new PackageInputException(CharacterErrorCode.InvalidPath, "Unsafe resource path.");
                if (!seen.Add(relative)) throw new PackageInputException(CharacterErrorCode.DuplicateResource, "Duplicate/case-aliased path.");
                if (Directory.Exists(entry)) pending.Push((entry, current.Depth + 1));
                else { CheckExtension(relative); result.Add(relative); }
            }
        }
        return result.AsReadOnly();
    }
    public static void CheckExtension(string relative)
    {
        if (Path.GetExtension(relative).ToLowerInvariant() is not (".json" or ".png" or ".txt"))
            throw new PackageInputException(CharacterErrorCode.ForbiddenFile, "Only PNG, JSON and TXT resources are allowed.");
    }
    public static void DeleteOwnedDirectory(string parent, string path)
    {
        var relative = Path.GetRelativePath(parent, path);
        if (relative.Contains(Path.DirectorySeparatorChar) ||
            !(relative.StartsWith(".stage-", StringComparison.Ordinal) || relative.StartsWith(".removed-", StringComparison.Ordinal)))
            throw new InvalidOperationException("Refusing to delete an unowned directory.");
        if (Directory.Exists(path))
        {
            // Re-check all links immediately before deleting only our staged tree.
            RejectLinks(path);
            foreach (var item in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories)) RejectLinks(item);
            Directory.Delete(path, recursive: true);
        }
    }
}
internal sealed class PackageInputException(CharacterErrorCode code, string message) : IOException(message)
{
    public CharacterErrorCode Code { get; } = code;
}
