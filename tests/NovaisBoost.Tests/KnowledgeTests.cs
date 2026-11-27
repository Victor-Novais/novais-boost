using System.Text;
using NovaisBoost.Core;

namespace NovaisBoost.Tests;

public sealed class KnowledgeTests
{
    private const long Gib = 1_073_741_824;

    private static HardwareSnapshot Pc(string cpu, string gpu, MemoryKind memory = MemoryKind.Ddr5, int ramGb = 32, string board = "ASUSTeK COMPUTER INC.", bool laptop = false) => new(
        DateTimeOffset.Now, new OsInfo("Windows 11", "26200", "25H2", TimeSpan.Zero, laptop, false),
        new CpuInfo(cpu, "x", 8, 16, 4000),
        [new GpuInfo(gpu, null, null, null, null), new GpuInfo("AMD Radeon(TM) Graphics", null, null, null, null)],
        new MemoryInfo(ramGb * Gib, ramGb * Gib / 2, [new MemoryModule("A", ramGb / 2 * Gib, 6000, 6000, memory, null, null)]),
        new BoardInfo(board, "B650", "1", null), [], [], [], [], null, new SystemFlags(null, null, null, null), [], []);

    private static string[] Ids(HardwareSnapshot pc) => ProfileMatcher.Match(DataCatalog.Profiles(), pc).Select(profile => profile.Id).ToArray();

    [Fact]
    public void AllKnowledgeBasesLoad()
    {
        Assert.NotEmpty(DataCatalog.Profiles());
        Assert.NotEmpty(DataCatalog.Processes());
        Assert.NotEmpty(DataCatalog.Games());
    }

    [Fact]
    public void ProfileTweaksReferenceRealCatalogTweaks()
    {
        var ids = TweakCatalog.LoadEmbedded().Select(tweak => tweak.Id).ToHashSet();
        Assert.All(DataCatalog.Profiles().SelectMany(profile => profile.Tweaks.Concat(profile.Avoid)), id => Assert.Contains(id, ids));
    }

    [Fact]
    public void ThisDevelopmentPcMatchesItsProfiles()
    {
        var ids = Ids(Pc("AMD Ryzen 5 7600X 6-Core Processor", "NVIDIA GeForce RTX 4070"));
        Assert.Equal(["cpu-zen4-zen5", "gpu-rtx40-50", "ram-ddr5", "ram-32gb", "board-asus"], ids);
    }

    [Theory]
    [InlineData("AMD Ryzen 7 7800X3D 8-Core Processor", "cpu-x3d-single")]
    [InlineData("AMD Ryzen 9 7950X3D 16-Core Processor", "cpu-x3d-dual")]
    [InlineData("13th Gen Intel(R) Core(TM) i7-13700K", "cpu-intel-raptor")]
    [InlineData("12th Gen Intel(R) Core(TM) i5-12400F", "cpu-intel-alder")]
    [InlineData("Intel(R) Core(TM) Ultra 7 265K", "cpu-intel-ultra")]
    [InlineData("AMD Ryzen 7 5700X 8-Core Processor", "cpu-zen3")]
    public void CpuProfiles(string cpu, string expected)
    {
        var cpuIds = Ids(Pc(cpu, "NVIDIA GeForce RTX 3060")).Where(id => id.StartsWith("cpu-", StringComparison.Ordinal));
        Assert.Equal([expected], cpuIds);
    }

    [Fact]
    public void DualCcdX3dAvoidsThePowerPlan()
    {
        var profiles = ProfileMatcher.Match(DataCatalog.Profiles(), Pc("AMD Ryzen 9 9950X3D 16-Core Processor", "AMD Radeon RX 9070 XT"));
        Assert.Contains("power-plan", profiles.SelectMany(profile => profile.Avoid));
        Assert.Contains(profiles, profile => profile.Id == "gpu-radeon");
    }

    [Fact]
    public void SmallRamAndLaptopProfiles()
    {
        var ids = Ids(Pc("AMD Ryzen 7 7840HS", "NVIDIA GeForce RTX 4060 Laptop GPU", MemoryKind.Ddr5, 16, "LENOVO", laptop: true));
        Assert.Contains("ram-16gb", ids);
        Assert.Contains("laptop", ids);
        Assert.DoesNotContain("ram-32gb", ids);
    }

    [Fact]
    public void GuardianDoesNothingWithoutAGame()
    {
        var policy = new GuardianPolicy(new ProcessCatalog(DataCatalog.Processes()), ["cs2.exe"]);
        Assert.Empty(policy.Plan([new ProcessSample(1, "msedge", PriorityLevel.Normal, 50)]));
    }

    [Fact]
    public void GuardianBoostsGameLowersBackgroundAndKeepsVoiceAndSystem()
    {
        var policy = new GuardianPolicy(new ProcessCatalog(DataCatalog.Processes()), ["cs2.exe"]);
        var changes = policy.Plan(
        [
            new ProcessSample(1, "cs2", PriorityLevel.Normal, 40),
            new ProcessSample(2, "msedge", PriorityLevel.Normal, 1),
            new ProcessSample(3, "Discord", PriorityLevel.Normal, 3),
            new ProcessSample(4, "obs64", PriorityLevel.Normal, 5),
            new ProcessSample(5, "svchost", PriorityLevel.Normal, 30),
            new ProcessSample(6, "UnknownTool", PriorityLevel.Normal, 20),
            new ProcessSample(7, "UnknownIdle", PriorityLevel.Normal, 1),
            new ProcessSample(8, "vgc", PriorityLevel.Normal, 2)
        ]).ToDictionary(change => change.Name, change => change.To);

        Assert.Equal(PriorityLevel.AboveNormal, changes["cs2"]);
        Assert.Equal(PriorityLevel.BelowNormal, changes["msedge"]);
        Assert.Equal(PriorityLevel.AboveNormal, changes["obs64"]);
        Assert.Equal(PriorityLevel.BelowNormal, changes["UnknownTool"]);
        Assert.DoesNotContain("Discord", changes.Keys);
        Assert.DoesNotContain("svchost", changes.Keys);
        Assert.DoesNotContain("UnknownIdle", changes.Keys);
        Assert.DoesNotContain("vgc", changes.Keys);
    }

    [Fact]
    public void GuardianGamePriorityDefaultsToAboveNormal()
    {
        var policy = new GuardianPolicy(new ProcessCatalog(DataCatalog.Processes()), ["cs2.exe"]);
        Assert.Equal(PriorityLevel.AboveNormal, policy.GamePriority);
        var change = Assert.Single(policy.Plan([new ProcessSample(1, "cs2", PriorityLevel.Normal, 40)]));
        Assert.Equal(PriorityLevel.AboveNormal, change.To);
        // Already at (or above) the target: left alone, never lowered.
        Assert.Empty(policy.Plan([new ProcessSample(1, "cs2", PriorityLevel.AboveNormal, 40)]));
        Assert.Empty(policy.Plan([new ProcessSample(1, "cs2", PriorityLevel.High, 40)]));
    }

    [Fact]
    public void GuardianHighModeRaisesTheGameToHigh()
    {
        var policy = new GuardianPolicy(new ProcessCatalog(DataCatalog.Processes()), ["cs2.exe"], PriorityLevel.High);
        var changes = policy.Plan(
        [
            new ProcessSample(1, "cs2", PriorityLevel.AboveNormal, 40),
            new ProcessSample(2, "obs64", PriorityLevel.Normal, 5),
            new ProcessSample(3, "msedge", PriorityLevel.Normal, 1)
        ]).ToDictionary(change => change.Name, change => change.To);
        Assert.Equal(PriorityLevel.High, changes["cs2"]);
        Assert.Equal(PriorityLevel.AboveNormal, changes["obs64"]);
        Assert.Equal(PriorityLevel.BelowNormal, changes["msedge"]);
    }

    [Theory]
    [InlineData(PriorityLevel.Normal)]
    [InlineData(PriorityLevel.Idle)]
    public void GuardianRejectsOtherGamePriorities(PriorityLevel priority) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new GuardianPolicy(new ProcessCatalog([]), ["cs2.exe"], priority));

    [Theory]
    [InlineData("{}", PriorityLevel.AboveNormal)]
    [InlineData("""{ "guardian": {} }""", PriorityLevel.AboveNormal)]
    [InlineData("""{ "guardian": { "gamePriority": "aboveNormal" } }""", PriorityLevel.AboveNormal)]
    [InlineData("""{ "guardian": { "gamePriority": "high" } }""", PriorityLevel.High)]
    public void SettingsReadTheGuardianGamePriority(string json, PriorityLevel expected) =>
        Assert.Equal(expected, AppSettings.Parse(json).GuardianOrDefault.GamePriority);

    [Theory]
    [InlineData("""{ "guardian": { "gamePriority": "realTime" } }""")]
    [InlineData("""{ "guardian": { "gamePriority": "normal" } }""")]
    [InlineData("""{ "guardian": { "gamePriority": 4 } }""")]
    [InlineData("""{ "guardian": { "priority": "high" } }""")]
    [InlineData("{ invalid")]
    public void InvalidSettingsAreRejected(string json) => Assert.Throws<InvalidDataException>(() => AppSettings.Parse(json));

    [Fact]
    public void MissingOrInvalidSettingsFileFallsBackToAboveNormal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "NovaisBoost.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var log = new ListLog();
            Assert.Equal(PriorityLevel.AboveNormal, AppSettings.Load(directory, log).GuardianOrDefault.GamePriority);
            Assert.Empty(log.Messages);

            File.WriteAllText(Path.Combine(directory, AppSettings.FileName), """{ "guardian": { "gamePriority": "realTime" } }""");
            Assert.Equal(PriorityLevel.AboveNormal, AppSettings.Load(directory, log).GuardianOrDefault.GamePriority);
            Assert.Single(log.Messages);

            File.WriteAllText(Path.Combine(directory, AppSettings.FileName), """{ "guardian": { "gamePriority": "high" } }""");
            Assert.Equal(PriorityLevel.High, AppSettings.Load(directory, log).GuardianOrDefault.GamePriority);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class ListLog : IActionLog
    {
        public List<string> Messages { get; } = [];
        public void Info(string message) => Messages.Add(message);
        public void Warning(string message) => Messages.Add(message);
        public void Error(string message) => Messages.Add(message);
    }

    [Fact]
    public void FrameStatsComputeLowsAndStutters()
    {
        var frames = Enumerable.Repeat(5.0, 990).Concat(Enumerable.Repeat(20.0, 10)).ToArray(); // 200 FPS with 10 slow frames
        var stats = FrameAnalysis.Compute(frames);
        Assert.Equal(1000, stats.Frames);
        Assert.InRange(stats.AverageFps, 194, 195);
        Assert.Equal(50, stats.OnePercentLowFps, 3);
        Assert.Equal(10, stats.Stutters);
    }

    [Fact]
    public void ReadsPresentMonCsvInBothFormats()
    {
        var v1 = FrameAnalysis.ReadPresentMonCsv(new StringReader("Application,ProcessID,MsBetweenPresents\ncs2.exe,1,6.94\ncs2.exe,1,7.10\n"));
        var v2 = FrameAnalysis.ReadPresentMonCsv(new StringReader("Application,FrameTime,CPUBusy\ncs2.exe,4.2,1\ncs2.exe,NA,1\n"));
        Assert.Equal([6.94, 7.10], v1);
        Assert.Equal([4.2], v2);
    }

    [Fact]
    public void TooFewFramesIsAnError() => Assert.Throws<InvalidDataException>(() => FrameAnalysis.Compute([5, 5, 5]));

    [Fact]
    public void PingStatsAndBufferbloatGrade()
    {
        var stats = NetworkAnalysis.Ping([10, 12, null, 14]);
        Assert.Equal(25, stats.LossPercent);
        Assert.Equal(12, stats.AverageMs);
        Assert.Equal(2, stats.JitterMs);
        Assert.Equal("A+", NetworkAnalysis.BufferbloatGrade(10, 12));
        Assert.Equal("C", NetworkAnalysis.BufferbloatGrade(10, 150));
        Assert.Contains(NetworkAnalysis.Evaluate(stats, "D"), finding => finding.Id == "bufferbloat");
    }

    [Fact]
    public void DnsQueryIsWellFormed()
    {
        var query = NetworkAnalysis.BuildQuery(0x1234, "steampowered.com");
        Assert.Equal([0x12, 0x34, 0x01, 0x00, 0x00, 0x01], query[..6]);
        Assert.Equal("steampowered", Encoding.ASCII.GetString(query, 13, 12));
        Assert.Equal([0, 0, 1, 0, 1], query[^5..]);

        var reply = (byte[])query.Clone();
        reply[2] |= 0x80;
        Assert.True(NetworkAnalysis.IsValidReply(reply, 0x1234));
        Assert.False(NetworkAnalysis.IsValidReply(reply, 0x9999));
        reply[3] = 0x03; // NXDOMAIN
        Assert.False(NetworkAnalysis.IsValidReply(reply, 0x1234));
    }

    [Fact]
    public void CleanupRespectsAgeAndKeepsShaderCacheSeparate()
    {
        var temp = CleanupTargets.All.First(target => target.Path == "%TEMP%");
        var now = DateTime.UtcNow;
        Assert.False(CleanupTargets.IsExpired(temp, now.AddHours(-12), now));
        Assert.True(CleanupTargets.IsExpired(temp, now.AddDays(-3), now));
        Assert.All(CleanupTargets.All.Where(target => target.Name.Contains("shader", StringComparison.OrdinalIgnoreCase)),
            target => Assert.Equal(CleanupKind.ShaderCache, target.Kind));
        Assert.DoesNotContain(CleanupTargets.Weekly(new CleanupSettings(true, true)), target => target.Kind == CleanupKind.ShaderCache);
        Assert.DoesNotContain(CleanupTargets.All, target => target.Path.Contains("Prefetch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LatencyRulesFlagHighDpc()
    {
        Assert.Empty(LatencyRules.Evaluate(new LatencySample(0.2, 0.8, 0.1, 0.4, 1000, 5000, 1)));
        Assert.Contains(LatencyRules.Evaluate(new LatencySample(3, 12, 0.1, 0.4, 1000, 5000, 1)), finding => finding.Severity == Severity.Critical);
    }

    [Fact]
    public void DpcFindingsReportCpuLoadNotMicroseconds()
    {
        var findings = LatencyRules.Evaluate(new LatencySample(3, 12, 3, 12, 1000, 5000, 1));
        Assert.Equal(2, findings.Count);
        Assert.All(findings, finding =>
        {
            Assert.Contains("% da CPU", finding.Title);
            Assert.DoesNotContain("µs", finding.Title + finding.Detail + finding.Recommendation);
            Assert.DoesNotContain("Latência", finding.Title);
        });
    }

    [Fact]
    public void SchedulesHaveUniqueIdsAndKnownTasks()
    {
        Assert.Equal(Schedules.All.Count, Schedules.All.Select(schedule => schedule.Id).Distinct().Count());
        Assert.Equal(DayOfWeek.Friday, Schedules.Find("limpeza").Day);
        Assert.Throws<ArgumentException>(() => Schedules.Find("unknown"));
    }
}
