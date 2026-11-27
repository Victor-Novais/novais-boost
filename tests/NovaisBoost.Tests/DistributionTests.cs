using NovaisBoost.Core;

namespace NovaisBoost.Tests;

public sealed class DistributionTests
{
    private static readonly DateTimeOffset Armed = new(2026, 11, 27, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BootGuardRevertsOnlyAfterTwoBootsWithoutReachingTheDesktop()
    {
        var state = new BootGuardState("S-1-5-21-1", Armed);
        var (first, decision1) = BootGuard.OnBoot(state);
        var (second, decision2) = BootGuard.OnBoot(first);
        var (third, decision3) = BootGuard.OnBoot(second);
        Assert.Equal([BootDecision.Counted, BootDecision.Counted, BootDecision.Revert], [decision1, decision2, decision3]);
        Assert.True(third.Triggered);
        Assert.Equal(BootDecision.Nothing, BootGuard.OnBoot(third).Decision);
    }

    [Fact]
    public void BootGuardStateRoundTripsOnDisk()
    {
        var directory = Path.Combine(Path.GetTempPath(), "NovaisBoost.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Null(BootGuard.Load(directory));
            BootGuard.Save(directory, new BootGuardState("S-1-5-21-1", Armed, 1));
            Assert.Equal(1, BootGuard.Load(directory)!.Attempts);
            BootGuard.Clear(directory);
            Assert.Null(BootGuard.Load(directory));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    private const string Hash = "B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF";

    [Fact]
    public void UpdateManifestOnlyAcceptsGithubHttpsAndAValidHash()
    {
        var manifest = Updates.Parse($$"""{ "version": "1.3.0", "url": "https://github.com/Victor-Novais/novais-boost/releases/download/v1.3.0/NOVAIS.boost.exe", "sha256": "{{Hash}}" }""");
        Assert.Equal("1.3.0", manifest.Version);
        Assert.Throws<InvalidDataException>(() => Updates.Parse($$"""{ "version": "1.3.0", "url": "http://github.com/x.exe", "sha256": "{{Hash}}" }"""));
        Assert.Throws<InvalidDataException>(() => Updates.Parse($$"""{ "version": "1.3.0", "url": "https://evil.example/x.exe", "sha256": "{{Hash}}" }"""));
        Assert.Throws<InvalidDataException>(() => Updates.Parse("""{ "version": "1.3.0", "url": "https://github.com/x.exe", "sha256": "abc" }"""));
        Assert.Throws<InvalidDataException>(() => Updates.Parse($$"""{ "version": "um", "url": "https://github.com/x.exe", "sha256": "{{Hash}}" }"""));
    }

    [Theory]
    [InlineData("1.0.0", "1.3.0", true)]
    [InlineData("1.3.0", "1.3.0", false)]
    [InlineData("1.4.0", "1.3.0", false)]
    [InlineData("dev", "9.0.0", false)]
    public void VersionComparison(string current, string candidate, bool newer) => Assert.Equal(newer, Updates.IsNewer(current, candidate));

    [Fact]
    public void HashCheck()
    {
        var data = "NOVAIS"u8.ToArray();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));
        Assert.True(Updates.HashMatches(data, hash.ToLowerInvariant()));
        Assert.False(Updates.HashMatches([1, 2, 3], hash));
    }

    [Fact]
    public void ErrorReportsCarryNoPersonalData()
    {
        Exception error;
        try
        {
            throw new IOException(@"Acesso negado a C:\Users\joao.silva\AppData\Local\x.txt em DESKTOP-JOAO (joao@mail.com, 192.168.0.10)");
        }
        catch (IOException caught)
        {
            error = caught;
        }
        var report = ErrorReports.Create(error, "1.3.0", "26200", Armed, "joao.silva", "DESKTOP-JOAO");
        var json = ErrorReports.ToJson(report);
        Assert.DoesNotContain("joao", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("192.168", json);
        Assert.DoesNotContain(@"C:\\Users", json);
        Assert.DoesNotContain(":line", json);
        Assert.Equal("System.IO.IOException", report.Type);
        Assert.Equal("1.3.0", report.Version);
    }

    [Fact]
    public void TermsAreAcceptedPerVersion()
    {
        Assert.False(Terms.Accepted(new AppSettings()));
        Assert.True(Terms.Accepted(new AppSettings(TermsAccepted: Terms.Version)));
        Assert.False(Terms.Accepted(new AppSettings(TermsAccepted: "2020-01-01")));
        Assert.Throws<InvalidDataException>(() => AppSettings.Parse("""{ "errorReports": { "enabled": true, "endpoint": "http://x" } }"""));
        Assert.True(AppSettings.Parse("{}").UpdatesOrDefault.Check);
        Assert.False(AppSettings.Parse("{}").ErrorReportsOrDefault.Enabled);
    }

    [Fact]
    public void ChangelogFromCommitSubjects()
    {
        var text = Changelog.Build("1.3.0", new DateOnly(2026, 11, 27), ["F18 debloat completo", "", "Merge branch x", "F23 manutenção v2"]);
        Assert.Equal("## 1.3.0 (2026-11-27)\n\n- F18 debloat completo\n- F23 manutenção v2\n", text);
    }
}

public sealed class StringsTests
{
    [Fact]
    public void BothLanguagesHaveTheSameKeysAndNoEmptyText()
    {
        var portuguese = Strings.Keys(Strings.Portuguese).Order().ToArray();
        Assert.Equal(portuguese, Strings.Keys(Strings.English).Order().ToArray());
        foreach (var language in Strings.Languages)
        {
            Strings.Use(language);
            Assert.All(portuguese, key => Assert.False(string.IsNullOrWhiteSpace(Strings.T(key)), key));
        }
        Strings.Use(Strings.Portuguese);
    }

    [Fact]
    public void LanguageSelectionAndFallback()
    {
        Assert.Equal(Strings.Portuguese, Strings.Detect(System.Globalization.CultureInfo.GetCultureInfo("pt-PT")));
        Assert.Equal(Strings.English, Strings.Detect(System.Globalization.CultureInfo.GetCultureInfo("es-ES")));
        Strings.Use("en-US");
        Assert.Equal("Settings", Strings.T("menu.settings"));
        Assert.Equal("Pick an option from 0 to 11.", Strings.T("menu.invalid", 11));
        Strings.Use("xx");
        Assert.Equal(Strings.Portuguese, Strings.Language);
        Assert.Equal("chave.inexistente", Strings.T("chave.inexistente"));
        Strings.Use(Strings.Portuguese);
        Assert.Throws<InvalidDataException>(() => AppSettings.Parse("""{ "language": "fr-FR" }"""));
    }

    /// <summary>F22: the migrated screens must not get new fixed texts (Portuguese words outside Strings.T).</summary>
    [Fact]
    public void MigratedScreensHaveNoFixedTexts()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "NovaisBoost.slnx")))
            root = Path.GetDirectoryName(root) ?? throw new DirectoryNotFoundException();
        foreach (var file in new[] { "src/NovaisBoost.Gui/MainWindow.cs" })
        {
            var path = Path.Combine(root, file);
            if (!File.Exists(path))
                continue;
            var literals = System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(path), "\"([^\"\\\\]|\\\\.)*[áéíóúãõçÁÉÍÓÚÃÕÇ]([^\"\\\\]|\\\\.)*\"");
            Assert.True(literals.Count == 0, $"{file}: {string.Join(", ", literals.Select(match => match.Value))}");
        }
    }
}
