using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NovaisBoost.Core;

// ---------------------------------------------------------------- 20.4 boot protection

/// <param name="Owner">SID of the account whose backup "Reverter tudo" must undo.</param>
/// <param name="Attempts">Boots counted since the guard was armed without the owner reaching the desktop.</param>
/// <param name="Triggered">The automatic revert already ran (never runs twice).</param>
public sealed record BootGuardState(string Owner, DateTimeOffset ArmedAt, int Attempts = 0, bool Triggered = false);

public enum BootDecision { Nothing, Counted, Revert }

/// <summary>
/// After changes that need a restart, a startup task counts boots and a logon task confirms the PC is healthy.
/// If the owner does not reach the desktop in <see cref="MaxFailedBoots"/> boots in a row, the next boot reverts everything.
/// </summary>
public static class BootGuard
{
    public const int MaxFailedBoots = 2;
    public const string FileName = "boot-guard.json";

    public static (BootGuardState State, BootDecision Decision) OnBoot(BootGuardState state)
    {
        if (state.Triggered)
            return (state, BootDecision.Nothing);
        var next = state with { Attempts = state.Attempts + 1 };
        return next.Attempts > MaxFailedBoots ? (next with { Triggered = true }, BootDecision.Revert) : (next, BootDecision.Counted);
    }

    public static BootGuardState? Load(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, FileName);
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<BootGuardState>(File.ReadAllText(path), TweakCatalog.JsonOptions) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static void Save(string dataDirectory, BootGuardState state)
    {
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, FileName);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(state, TweakCatalog.JsonOptions));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    public static void Clear(string dataDirectory) => File.Delete(Path.Combine(dataDirectory, FileName));
}

// ---------------------------------------------------------------- 20.3 automatic update

/// <summary>releases/latest.json published with each release (scripts/build.ps1 writes it).</summary>
public sealed record UpdateManifest(string Version, string Url, string Sha256, string? Notes = null);

public static partial class Updates
{
    public const string ManifestUrl = "https://github.com/Victor-Novais/novais-boost/releases/latest/download/latest.json";

    public static UpdateManifest Parse(string json)
    {
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, TweakCatalog.JsonOptions) ?? throw new InvalidDataException("latest.json vazio.");
        if (!System.Version.TryParse(manifest.Version, out _))
            throw new InvalidDataException($"Versão inválida em latest.json: {manifest.Version}.");
        if (!Sha256Pattern().IsMatch(manifest.Sha256))
            throw new InvalidDataException("SHA256 inválido em latest.json.");
        if (!Uri.TryCreate(manifest.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A atualização só é aceita de https://github.com.");
        return manifest;
    }

    /// <summary>True when <paramref name="candidate"/> is newer than <paramref name="current"/> ("dev" builds never update).</summary>
    public static bool IsNewer(string current, string candidate) =>
        System.Version.TryParse(current, out var installed) && System.Version.TryParse(candidate, out var offered) && offered > installed;

    public static bool HashMatches(byte[] file, string sha256) =>
        string.Equals(Convert.ToHexString(SHA256.HashData(file)), sha256, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^[0-9A-Fa-f]{64}$")]
    private static partial Regex Sha256Pattern();
}

// ---------------------------------------------------------------- 20.7 opt-in error report

/// <summary>What an error report contains: never paths, user names, the PC name or the list of programs.</summary>
public sealed record ErrorReport(string Version, string WindowsBuild, string Type, string Message, string[] Stack, DateTimeOffset When);

public static partial class ErrorReports
{
    public static ErrorReport Create(Exception error, string version, string windowsBuild, DateTimeOffset when, string? userName = null, string? machine = null) =>
        new(version, windowsBuild, error.GetType().FullName ?? error.GetType().Name, Sanitize(error.Message, userName, machine),
            (error.StackTrace ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => Sanitize(InPath().Replace(line, ""), userName, machine)).Take(30).ToArray(), when);

    /// <summary>Removes file paths, user and machine names, e-mails and IP addresses.</summary>
    public static string Sanitize(string text, string? userName = null, string? machine = null)
    {
        var clean = WindowsPathPattern().Replace(text, "<caminho>");
        clean = EmailPattern().Replace(clean, "<e-mail>");
        clean = IpPattern().Replace(clean, "<ip>");
        foreach (var name in new[] { userName, machine }.Where(name => name is { Length: >= 3 }))
            clean = Regex.Replace(clean, Regex.Escape(name!), "<nome>", RegexOptions.IgnoreCase);
        return clean.Length > 500 ? clean[..500] : clean;
    }

    public static string ToJson(ErrorReport report) => JsonSerializer.Serialize(report, TweakCatalog.JsonOptions);

    [GeneratedRegex(@"\s+in\s+.+:line\s+\d+")]
    private static partial Regex InPath();

    [GeneratedRegex(@"([A-Za-z]:\\|\\\\)[^\s""'<>|]*")]
    private static partial Regex WindowsPathPattern();

    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.-]+")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"\b\d{1,3}(\.\d{1,3}){3}\b")]
    private static partial Regex IpPattern();
}

// ---------------------------------------------------------------- 20.9 terms of use

public static class Terms
{
    /// <summary>Bump when the text changes: everyone is asked again.</summary>
    public const string Version = "2026-11-27";

    /// <summary>The terms in the current language (<see cref="Strings"/>).</summary>
    public static IReadOnlyList<string> Text => Enumerable.Range(1, 6).Select(index => Strings.T($"terms.{index}")).ToArray();

    public static bool Accepted(AppSettings settings) => settings.TermsAccepted == Version;
}

/// <summary>20.12: CHANGELOG lines from commit subjects ("F18 debloat completo" → "- F18 debloat completo").</summary>
public static class Changelog
{
    public static string Build(string version, DateOnly date, IEnumerable<string> subjects) =>
        $"## {version} ({date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)})\n\n" +
        string.Concat(subjects.Where(subject => subject.Trim().Length > 0 && !subject.StartsWith("Merge ", StringComparison.Ordinal))
            .Select(subject => $"- {subject.Trim()}\n"));
}
