namespace NovaisBoost.Core;

/// <summary>
/// Path helpers for Windows paths that behave the same on any OS, so the Core logic (and its tests) does not depend on
/// the separator of the machine running it. System.IO.Path only splits on '\' when running on Windows.
/// </summary>
public static class WindowsPath
{
    private static readonly char[] Separators = ['\\', '/'];

    public static string GetFileName(string path) => path[(path.LastIndexOfAny(Separators) + 1)..];

    public static string GetFileNameWithoutExtension(string path)
    {
        var name = GetFileName(path);
        var dot = name.LastIndexOf('.');
        return dot <= 0 ? name : name[..dot];
    }

    /// <summary>Parent folder without the trailing separator ("C:\" for a file at the drive root); null when there is none.</summary>
    public static string? GetDirectoryName(string path)
    {
        var index = path.LastIndexOfAny(Separators);
        if (index < 0)
            return null;
        var parent = path[..index];
        return parent.Length == 2 && parent[1] == ':' ? parent + "\\" : parent.Length == 0 ? null : parent;
    }

    /// <summary>"C:\Games\x\" → "C:\Games\x"; "c:/" → "C:\". Resolves "." and ".." segments, never touches the disk.</summary>
    public static string Normalize(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
                continue;
            if (part == "..")
            {
                if (parts.Count > 1)
                    parts.RemoveAt(parts.Count - 1);
                continue;
            }
            parts.Add(part);
        }
        if (parts.Count == 0)
            return path;
        if (parts[0].Length == 2 && parts[0][1] == ':')
            parts[0] = char.ToUpperInvariant(parts[0][0]) + ":";
        var joined = string.Join('\\', parts);
        return parts.Count == 1 && joined.EndsWith(':') ? joined + "\\" : joined;
    }

    /// <summary>"C:\" for "C:\Games\x"; null for a relative path.</summary>
    public static string? GetRoot(string path)
    {
        var normalized = Normalize(path);
        return normalized.Length >= 2 && normalized[1] == ':' ? normalized[..2] + "\\" : null;
    }
}
