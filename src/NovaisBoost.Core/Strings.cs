using System.Globalization;
using System.Text.Json;

namespace NovaisBoost.Core;

/// <summary>
/// F22: interface texts in data/i18n/&lt;language&gt;.json (pt-BR and en-US). pt-BR is the reference; a key missing in the
/// chosen language falls back to it. Screens migrated so far: menu, terms, settings and the graphical interface.
/// </summary>
public static class Strings
{
    public const string Portuguese = "pt-BR";
    public const string English = "en-US";
    public static readonly string[] Languages = [Portuguese, English];

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> Tables = new(() =>
        Languages.ToDictionary(language => language, Load, StringComparer.OrdinalIgnoreCase));

    /// <summary>Current language. Set from settings.json ("language"); "auto" follows Windows.</summary>
    public static string Language { get; private set; } = Detect(CultureInfo.CurrentUICulture);

    public static void Use(string? setting) =>
        Language = setting is null or "auto" ? Detect(CultureInfo.CurrentUICulture)
            : Languages.FirstOrDefault(language => language.Equals(setting, StringComparison.OrdinalIgnoreCase)) ?? Portuguese;

    /// <summary>Portuguese for pt-*, English for everything else.</summary>
    public static string Detect(CultureInfo culture) => culture.TwoLetterISOLanguageName == "pt" ? Portuguese : English;

    public static string T(string key) =>
        Tables.Value[Language].GetValueOrDefault(key) ?? Tables.Value[Portuguese].GetValueOrDefault(key) ?? key;

    public static string T(string key, params object[] args) => string.Format(CultureInfo.GetCultureInfo(Language), T(key), args);

    public static IReadOnlyCollection<string> Keys(string language) => Tables.Value[language].Keys.ToArray();

    private static IReadOnlyDictionary<string, string> Load(string language)
    {
        using var stream = typeof(Strings).Assembly.GetManifestResourceStream($"I18n.{language}.json")
            ?? throw new InvalidDataException($"Textos ausentes: {language}.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}
