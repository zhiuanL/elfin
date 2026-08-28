namespace DesktopPet.CharacterSdk;

public static class PackagePath
{
    public static bool IsSafe(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 240 || path.Contains('\\') ||
            path.Any(c => char.IsControl(c) || "<>:\"|?*".Contains(c))) return false;
        return path.Split('/').All(part =>
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')) return false;
            var stem = part.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
            return stem is not ("CON" or "PRN" or "AUX" or "NUL" or "CLOCK$" or "CONIN$" or "CONOUT$") &&
                !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                    && stem[3] is >= '0' and <= '9' or '¹' or '²' or '³');
        });
    }
    public static string Resolve(string root, string relative)
    {
        if (!IsSafe(relative)) throw new ArgumentException("Unsafe package path.", nameof(relative));
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot, StringComparison.Ordinal)) throw new ArgumentException("Path escapes package root.", nameof(relative));
        return full;
    }
}
