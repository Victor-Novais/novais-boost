namespace NovaisBoost.Core;

/// <summary>The "Key=Value;Key=Value;" string Windows keeps in DirectXUserGlobalSettings (graphics settings of Windows).</summary>
public static class DirectXSettings
{
    public const string Path = @"Software\Microsoft\DirectX\UserGpuPreferences";
    public const string ValueName = "DirectXUserGlobalSettings";

    public static string? Get(string? raw, string key) => Entries(raw).FirstOrDefault(entry => Same(entry.Key, key)).Value;

    /// <summary>Sets (or removes, with null) one entry and keeps the others in their order. Empty result → null (delete the value).</summary>
    public static string? Set(string? raw, string key, string? value)
    {
        var entries = Entries(raw).ToList();
        var index = entries.FindIndex(entry => Same(entry.Key, key));
        if (value is null)
        {
            if (index >= 0)
                entries.RemoveAt(index);
        }
        else if (index >= 0)
            entries[index] = (entries[index].Key, value);
        else
            entries.Add((key, value));
        return entries.Count == 0 ? null : string.Concat(entries.Select(entry => $"{entry.Key}={entry.Value};"));
    }

    private static IEnumerable<(string Key, string Value)> Entries(string? raw) => (raw ?? "")
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(part => part.Split('=', 2))
        .Where(pair => pair.Length == 2 && pair[0].Length > 0)
        .Select(pair => (pair[0], pair[1]));

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
