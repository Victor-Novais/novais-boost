using NovaisBoost.Core;

namespace NovaisBoost.Tests;

public sealed class CommunityTests
{
    private static HardwareSnapshot Pc() => new(
        DateTimeOffset.Now, new OsInfo("Microsoft Windows 11 Pro", "26200", "25H2", TimeSpan.Zero, false, false),
        new CpuInfo("AMD Ryzen 5 7600X 6-Core Processor", "AuthenticAMD", 6, 12, 4700),
        [new GpuInfo("NVIDIA GeForce RTX 4070", "32.0", null, null, null), new GpuInfo("AMD Radeon(TM) Graphics", "31.0", null, null, null)],
        new MemoryInfo(32L << 30, 16L << 30, [new MemoryModule("DIMM_A2", 16L << 30, 6000, 6000, MemoryKind.Ddr5, "Kingston", "KF560C36"),
            new MemoryModule("DIMM_B2", 16L << 30, 6000, 6000, MemoryKind.Ddr5, "Kingston", "KF560C36")]),
        new BoardInfo("ASUS", "TUF GAMING B650-PLUS", "3024", null), [], [], [], [], null, new SystemFlags(null, null, null, null), [], []);

    private static LocalEvidenceEntry Entry(string id) =>
        new(id, "Energia", "Counter-Strike 2", Verdict.RealGain, 4.2345, 6.789, 2.1, 9.9, new DateTimeOffset(2026, 11, 27, 21, 14, 3, TimeSpan.Zero));

    [Fact]
    public void PayloadHasOnlyHardwareModelsAndTheRelativeResult()
    {
        var result = Community.Build(Pc(), [Entry("power-plan"), Entry("power-throttling-off")], "1.3.0");
        Assert.Equal("AMD Ryzen 5 7600X", result.Cpu);
        Assert.Equal("NVIDIA GeForce RTX 4070", result.Gpu);
        Assert.Equal((32, 6000), (result.RamGb, result.RamMts));
        Assert.Equal("2026-11", result.Month);
        Assert.Equal(["power-plan", "power-throttling-off"], result.Tweaks);
        Assert.Equal(6.79, result.OnePercentLowChange);

        var json = Community.ToJson(result);
        foreach (var personal in new[] { "Kingston", "KF560C36", "ASUS", "TUF", Environment.UserName, Environment.MachineName, "21:14" })
            if (personal.Length >= 3)
                Assert.DoesNotContain(personal, json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Intel(R) Core(TM) i7-14700K", "Intel Core i7-14700K")]
    [InlineData("AMD Ryzen 7 7800X3D 8-Core Processor", "AMD Ryzen 7 7800X3D")]
    [InlineData("Intel(R) Core(TM) i5-9400F CPU @ 2.90GHz", "Intel Core i5-9400F")]
    [InlineData(null, "desconhecido")]
    public void ModelNames(string? raw, string expected) => Assert.Equal(expected, Community.Model(raw));

    [Fact]
    public void PromotionNeedsThirtyResultsAndAnIntervalAboveZero()
    {
        Assert.True(Community.IsProven(new("x", 214, 3.1, 1.8, 4.4)));
        Assert.False(Community.IsProven(new("x", 29, 3.1, 1.8, 4.4)));
        Assert.False(Community.IsProven(new("x", 500, 1.0, -0.2, 2.2)));
        Assert.True(Community.IsProvenWorse(new("x", 40, -3, -5, -1)));
        Assert.Equal("Testado em 214 PCs parecidos com o seu: +3,1% no 1% low (IC 95%: +1,8 a +4,4)", Community.Describe(new("x", 214, 3.1, 1.8, 4.4)));
    }

    [Fact]
    public void CommunityEvidenceAffectsTheLevelsButThisPcWins()
    {
        var tweaks = TweakCatalog.LoadEmbedded();
        var experimental = tweaks.Single(tweak => tweak.Id == "timer-resolution");
        var proven = new Dictionary<string, CommunityEvidence> { ["timer-resolution"] = new("timer-resolution", 120, 2.5, 1.1, 3.9) };
        Assert.DoesNotContain(TweakLevels.Select(tweaks, TweakLevel.Extreme), tweak => tweak.Id == experimental.Id);
        Assert.Contains(TweakLevels.Select(tweaks, TweakLevel.Extreme, LocalEvidence.Empty, proven), tweak => tweak.Id == experimental.Id);

        var worseHere = LocalEvidence.Empty.With([Entry("timer-resolution") with { Verdict = Verdict.Worse }]);
        Assert.DoesNotContain(TweakLevels.Select(tweaks, TweakLevel.Extreme, worseHere, proven), tweak => tweak.Id == experimental.Id);

        var worse = new Dictionary<string, CommunityEvidence> { ["power-plan"] = new("power-plan", 60, -4, -6, -2) };
        Assert.DoesNotContain(TweakLevels.Select(tweaks, TweakLevel.Extreme, LocalEvidence.Empty, worse), tweak => tweak.Id == "power-plan");
    }

    [Fact]
    public void EvidenceJsonAndSettings()
    {
        var evidence = Community.ParseEvidence("""[ { "tweak": "power-plan", "samples": 31, "onePercentLowChange": 2.0, "ciLowPercent": 0.5, "ciHighPercent": 3.5 } ]""");
        Assert.Equal(31, Assert.Single(evidence).Samples);
        Assert.False(AppSettings.Parse("{}").CommunityOrDefault.Enabled);
        Assert.Throws<InvalidDataException>(() => AppSettings.Parse("""{ "community": { "endpoint": "http://x" } }"""));
    }
}
