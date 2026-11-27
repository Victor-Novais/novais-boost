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
