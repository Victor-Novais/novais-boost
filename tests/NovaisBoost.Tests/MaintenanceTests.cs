using System.Buffers.Binary;
using NovaisBoost.Core;

namespace NovaisBoost.Tests;

public sealed class MaintenanceTests
{
    private static byte[] InfoFile(long version, DateTime deletedUtc)
    {
        var data = new byte[64];
        BinaryPrimitives.WriteInt64LittleEndian(data, version);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(8), 12345);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(16), deletedUtc.ToFileTimeUtc());
        return data;
    }

    [Fact]
    public void RecycleBinReadsTheDeletionTimeAndKeepsRecentItems()
    {
        var now = new DateTime(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);
        var old = RecycleBin.DeletedAtUtc(InfoFile(2, now.AddDays(-45)));
        var recent = RecycleBin.DeletedAtUtc(InfoFile(1, now.AddDays(-3)));
        Assert.True(RecycleBin.IsExpired(old!.Value, now));
        Assert.False(RecycleBin.IsExpired(recent!.Value, now));
        Assert.Null(RecycleBin.DeletedAtUtc(InfoFile(7, now)));
        Assert.Null(RecycleBin.DeletedAtUtc(new byte[10]));
        Assert.Equal("$RAB12CD.txt", RecycleBin.ContentName("$IAB12CD.txt"));
        Assert.Null(RecycleBin.ContentName("desktop.ini"));
    }

    [Fact]
    public void BrowserAndLauncherCachesAreOptInAndOnlyWithTheProgramClosed()
    {
        var weekly = CleanupTargets.Weekly(new CleanupSettings());
        Assert.DoesNotContain(weekly, target => target.Kind is CleanupKind.Browser or CleanupKind.Launcher);
        Assert.Contains(weekly, target => target.Pattern == "thumbcache_*.db" && !target.Recursive);

        var all = CleanupTargets.Weekly(new CleanupSettings(Browsers: true, Launchers: true));
        Assert.Contains(all, target => target.Kind == CleanupKind.Browser);
        Assert.Contains(all, target => target.Kind == CleanupKind.Launcher);
        Assert.All(all.Where(target => target.Kind is CleanupKind.Browser or CleanupKind.Launcher),
            target => Assert.NotEmpty(target.LockedBy!));
        Assert.DoesNotContain(CleanupTargets.All, target => target.Path.Contains("Prefetch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NewSchedulesExist()
    {
        Assert.Equal(["guardian", "reaplicar", "limpeza", "ssd", "saude", "drivers", "rede"], Schedules.All.Select(schedule => schedule.Id));
        Assert.Contains("RestoreHealth", Schedules.Find("saude").Description);
    }

    private static HardwareSnapshot Pc(params GpuInfo[] gpus) => new(
        DateTimeOffset.Now, new OsInfo("Windows 11", "26200", "25H2", TimeSpan.Zero, false, false),
        new CpuInfo("AMD Ryzen 5 7600X", "AuthenticAMD", 6, 12, 4700), gpus, null, null, [], [], [], [], null,
        new SystemFlags(null, null, null, null), [], []);

    [Fact]
    public void DriverCheckOnlyWarnsAboutOldDedicatedDrivers()
    {
        var today = new DateOnly(2026, 11, 27);
        var pc = Pc(new GpuInfo("NVIDIA GeForce RTX 4070", "32.0.15.6094", today.AddDays(-200), null, null),
            new GpuInfo("AMD Radeon(TM) Graphics", "31.0", today.AddDays(-400), null, null));
        var warning = Assert.Single(DriverCheck.Outdated(pc, today));
        Assert.Contains("RTX 4070", warning);
        Assert.Contains("200 dias", warning);
        Assert.Empty(DriverCheck.Outdated(Pc(new GpuInfo("NVIDIA GeForce RTX 4070", "x", today.AddDays(-30), null, null)), today));
    }

    [Fact]
    public void DnsSwitchesOnlyWhenTheGainIsWorthIt()
    {
        DnsResult Result(string name, string server, double ms, int failures = 0) => new(name, server, ms, failures);
        var current = new[] { "1.1.1.1", "1.0.0.1" };

        // Current is already the fastest.
        Assert.Null(DnsAutoSwitch.Choose([Result("Cloudflare", "1.1.1.1", 12), Result("Google", "8.8.8.8", 15)], current));
        // Faster by less than 10 ms: keep it.
        Assert.Null(DnsAutoSwitch.Choose([Result("Cloudflare", "1.1.1.1", 20), Result("Google", "8.8.8.8", 14)], current));
        // Faster by 10 ms or more: switch.
        Assert.Equal("8.8.8.8", DnsAutoSwitch.Choose([Result("Cloudflare", "1.1.1.1", 30), Result("Google", "8.8.8.8", 14)], current)!.Server);
        // Current failing: switch to one that answers.
        Assert.Equal("9.9.9.9", DnsAutoSwitch.Choose([Result("Cloudflare", "1.1.1.1", 9, 3), Result("Quad9", "9.9.9.9", 18)], current)!.Server);
        // A resolver with failures is never chosen.
        Assert.Null(DnsAutoSwitch.Choose([Result("Cloudflare", "1.1.1.1", 40), Result("Google", "8.8.8.8", 10, 1)], current));
    }

    [Fact]
    public void PowerSupplyWarningUsesTheUserAnswer()
    {
        var pc = Pc(new GpuInfo("NVIDIA GeForce RTX 4080 SUPER", null, null, null, null));
        Assert.Null(PowerSupply.Evaluate(pc, new PcSettings()));
        Assert.Null(PowerSupply.Evaluate(pc, new PcSettings(850, PsuRating.Gold)));
        var finding = PowerSupply.Evaluate(pc, new PcSettings(550))!;
        Assert.Equal("psu-undersized", finding.Id);
        Assert.Equal(FindingCategory.Stability, finding.Category);
        Assert.Contains(Diagnostics.Evaluate(pc, new DateOnly(2026, 11, 27), new PcSettings(550)).Findings, item => item.Id == "psu-undersized");
        Assert.Null(PowerSupply.Recommended("NVIDIA GeForce GTX 1650"));
        Assert.Equal(650, PowerSupply.Recommended("NVIDIA GeForce RTX 4070"));
        Assert.Equal(700, PowerSupply.Recommended("NVIDIA GeForce RTX 4070 Ti SUPER"));
    }

    [Fact]
    public void SettingsKeepTheNewSectionsAndRejectAnAbsurdPsu()
    {
        var settings = AppSettings.Parse("""{ "cleanup": { "browsers": true }, "pc": { "psuWatts": 750, "psuRating": "gold" } }""");
        Assert.True(settings.CleanupOrDefault.Browsers);
        Assert.False(settings.CleanupOrDefault.Launchers);
        Assert.Equal((750, PsuRating.Gold), (settings.PcOrDefault.PsuWatts, settings.PcOrDefault.PsuRating));
        Assert.Equal(settings, AppSettings.Parse(settings.Serialize()) with { });
        Assert.Throws<InvalidDataException>(() => AppSettings.Parse("""{ "pc": { "psuWatts": 50 } }"""));

        var directory = Path.Combine(Path.GetTempPath(), "NovaisBoost.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            settings.Save(directory);
            Assert.Equal(750, AppSettings.Load(directory, new NullTestLog()).PcOrDefault.PsuWatts);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void QuickBenchFlagsMemoryAndDiskFarBelowTheirClass()
    {
        var module = new MemoryModule("DIMM_A2", 16L << 30, 6000, 4800, MemoryKind.Ddr5, null, null);
        var pc = Pc() with
        {
            Memory = new MemoryInfo(32L << 30, 16L << 30, [module, module with { Slot = "DIMM_B2" }]),
            Disks = [new DiskInfo("Samsung 990 PRO", DiskMedia.Ssd, "NVMe", DiskHealth.Healthy, 2_000_000_000_000, true)]
        };
        Assert.Equal("ddr5-dual", QuickBench.MemoryClass(pc));
        Assert.Equal("nvme", QuickBench.DiskClass(pc));
        var baselines = DataCatalog.Baselines();
        Assert.Empty(QuickBench.Evaluate(new QuickBenchResult(500, 4000, 40000, 3000), pc, baselines));
        var findings = QuickBench.Evaluate(new QuickBenchResult(500, 4000, 12000, 400), pc, baselines);
        Assert.Equal(["memory-slow", "disk-slow"], findings.Select(finding => finding.Id));
        Assert.Equal(FindingCategory.Memory, findings[0].Category);
        Assert.Equal(FindingCategory.Storage, findings[1].Category);
    }

    private sealed class NullTestLog : IActionLog
    {
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}
