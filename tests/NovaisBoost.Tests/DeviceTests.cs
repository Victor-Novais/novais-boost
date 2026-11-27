using NovaisBoost.Core;

namespace NovaisBoost.Tests;

public sealed partial class TweakEngineTests
{
    [Fact]
    public void DirectXSettingKeepsTheOtherEntriesAndRevertsExactly()
    {
        _windows.DirectX = "VRROptimizeEnable=1;AutoHDREnable=1;";
        var tweak = TweakCatalog.LoadEmbedded().Single(item => item.Id == "windowed-optimizations");

        Engine.Apply(tweak, dryRun: false);
        Assert.Equal("VRROptimizeEnable=1;AutoHDREnable=1;SwapEffectUpgradeEnable=1;", _windows.DirectX);
        Assert.Empty(Engine.Detect(tweak));
        Engine.RevertAll(dryRun: false);
        Assert.Equal("VRROptimizeEnable=1;AutoHDREnable=1;", _windows.DirectX);
    }

    [Fact]
    public void DirectXSettingOnAnEmptyValueGoesBackToNothing()
    {
        var tweak = TweakCatalog.LoadEmbedded().Single(item => item.Id == "windowed-optimizations");
        Engine.Apply(tweak, dryRun: false);
        Assert.Equal("SwapEffectUpgradeEnable=1;", _windows.DirectX);
        Engine.RevertAll(dryRun: false);
        Assert.Null(_windows.DirectX);
    }

    [Fact]
    public void DeviceIsDisabledAndEnabledAgainOnRevertAndMissingDevicesAreSkipped()
    {
        const string id = @"HDAUDIO\FUNC_01&VEN_10DE&DEV_00A3\5&1";
        var tweak = Tweak("audio-hdmi", new DeviceAction(id, "NVIDIA High Definition Audio", false));
        Assert.Empty(Engine.Apply(tweak, dryRun: false));

        _windows.Devices[id] = true;
        Engine.Apply(tweak, dryRun: false);
        Assert.False(_windows.Devices[id]);
        Engine.RevertAll(dryRun: false);
        Assert.True(_windows.Devices[id]);
    }
}

public sealed class DeviceRuleTests
{
    private static HardwareSnapshot Pc(DeepInfo deep, params VolumeInfo[] volumes) => new(
        DateTimeOffset.Now, new OsInfo("Windows 11", "26200", "25H2", TimeSpan.Zero, false, false),
        new CpuInfo("AMD Ryzen 5 7600X", "AuthenticAMD", 6, 12, 4700), [], null, null, [], volumes, [], [], null,
        new SystemFlags(null, null, null, null), [], [], deep);

    private static string[] Ids(HardwareSnapshot pc)
    {
        var findings = new List<Finding>();
        DeviceRules.Evaluate(pc, findings);
        return findings.Select(finding => finding.Id).ToArray();
    }

    [Fact]
    public void DiskReliabilityRules()
    {
        Assert.Empty(Ids(Pc(new DeepInfo(Reliability: [new("990 PRO", 45, 60, 3, 0, 1200, true)]))));
        Assert.Equal(["disk-wear", "disk-read-errors", "disk-temperature", "disk-write-cache"],
            Ids(Pc(new DeepInfo(Reliability: [new("Velho", 75, 80, 95, 3, 40000, false)]))));
        Assert.Equal(["disk-wear"], Ids(Pc(new DeepInfo(Reliability: [new("Médio", null, null, 72, null, null)]))));
        Assert.Equal(FindingCategory.Storage, Diagnostics.CategoryOf("disk-wear"));
    }

    [Fact]
    public void NetworkAdapterRules()
    {
        Assert.Empty(Ids(Pc(new DeepInfo(Nics: [new("Ethernet", true, 512)]))));
        Assert.Equal(["nic-rss-off", "nic-buffers"], Ids(Pc(new DeepInfo(Nics: [new("Realtek", false, 128)]))));
        Assert.Equal(FindingCategory.Network, Diagnostics.CategoryOf("nic-rss-off"));
    }

    [Fact]
    public void ForcedInterruptAffinityIsReported()
    {
        Assert.Empty(Ids(Pc(new DeepInfo(Affinity: []))));
        var pc = Pc(new DeepInfo(Affinity: [new("GPU", 4, 1UL << 2), new("Rede", 4, 1UL << 2)]));
        var findings = new List<Finding>();
        DeviceRules.Evaluate(pc, findings);
        var finding = Assert.Single(findings);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(Severity.Info, Single(Pc(new DeepInfo(Affinity: [new("GPU", 4, 0b1100)]))).Severity);
    }

    private static Finding Single(HardwareSnapshot pc)
    {
        var findings = new List<Finding>();
        DeviceRules.Evaluate(pc, findings);
        return Assert.Single(findings);
    }

    [Fact]
    public void MouseSharingAControllerWithACaptureCard()
    {
        var shared = Pc(new DeepInfo(Usb: [new("G Pro", UsbDeviceKind.Mouse, "USB 3.2 (AMD)"), new("Cam Link 4K", UsbDeviceKind.Capture, "USB 3.2 (AMD)")]));
        Assert.Equal(["usb-shared"], Ids(shared));
        var split = Pc(new DeepInfo(Usb: [new("G Pro", UsbDeviceKind.Mouse, "USB 3.2 (AMD)"), new("Cam Link 4K", UsbDeviceKind.Capture, "ASMedia USB 3.1")]));
        Assert.Empty(Ids(split));
    }

    [Fact]
    public void GameOnSataSsdWithAnNvmeAvailable()
    {
        var deep = new DeepInfo(Games: [new GameInstall("Counter-Strike 2", @"D:\cs2.exe", DiskMedia.Ssd, Nvme: false)]);
        Assert.Equal(["disk-game-sata"], Ids(Pc(deep, new VolumeInfo("C:", 1000, 500, true, DiskMedia.Ssd, Nvme: true))));
        Assert.Empty(Ids(Pc(deep, new VolumeInfo("C:", 1000, 50, true, DiskMedia.Ssd, Nvme: true))));
        Assert.Empty(Ids(Pc(deep, new VolumeInfo("C:", 1000, 500, true, DiskMedia.Ssd))));
    }

    [Fact]
    public void DirectXStringHelpers()
    {
        Assert.Equal("1", DirectXSettings.Get("VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;", "swapeffectupgradeenable"));
        Assert.Null(DirectXSettings.Get(null, "X"));
        Assert.Equal("A=1;B=2;", DirectXSettings.Set("A=1;", "B", "2"));
        Assert.Equal("A=3;", DirectXSettings.Set("A=1;", "a", "3"));
        Assert.Null(DirectXSettings.Set("A=1;", "A", null));
    }

    [Fact]
    public void PollingRateFromRawInputTimestamps()
    {
        var steady = Enumerable.Range(0, 1000).Select(index => index * 1.0).ToArray();
        var result = PollingRate.Analyze(steady)!;
        Assert.Equal(1000, PollingRate.Nominal(result.AverageHz));
        Assert.True(result.StabilityPercent > 99);
        Assert.Empty(PollingRate.Evaluate(result, new CpuInfo("x", "x", 8, 16, 5000)));

        var jumpy = Enumerable.Range(0, 1000).Select(index => index * 1.0 + (index % 2 == 0 ? 0 : 0.6)).ToArray();
        Assert.Contains(PollingRate.Evaluate(PollingRate.Analyze(jumpy)!, null), finding => finding.Id == "mouse-polling-unstable");

        var fast = Enumerable.Range(0, 4000).Select(index => index * 0.125).ToArray();
        var eightK = PollingRate.Analyze(fast)!;
        Assert.Equal(8000, PollingRate.Nominal(eightK.AverageHz));
        Assert.Contains(PollingRate.Evaluate(eightK, new CpuInfo("x", "x", 6, 12, 4700)), finding => finding.Id == "mouse-polling-cpu");
        Assert.Null(PollingRate.Analyze([0, 1, 2]));
    }

    [Theory]
    [InlineData(10, "twitch", 6000)]
    [InlineData(5, "twitch", 4000)]
    [InlineData(50, "youtube", 20000)]
    [InlineData(20, "youtube", 15000)]
    public void BitrateFromUpload(double upload, string platform, int expected) => Assert.Equal(expected, StreamSettings.SuggestBitrate(upload, platform));
}

public sealed class GameServerTests
{
    [Fact]
    public void ParsesTheValveRelayConfiguration()
    {
        var pops = GameServerCatalog.ParseSdr("""
            { "revision": 1, "pops": {
                "gru": { "desc": "Sao Paulo", "relays": [ { "ipv4": "155.133.227.2", "port_range": [27015, 27060] } ] },
                "iad": { "desc": "Sterling", "relays": [ { "ipv4": "bad" }, { "ipv4": "162.254.192.2" } ] },
                "nop": { "desc": "No relays" } } }
            """);
        Assert.Equal(["gru", "iad"], pops.Select(pop => pop.Code));
        Assert.Equal("162.254.192.2", pops[1].Address);
        Assert.Empty(GameServerCatalog.ParseSdr("{}"));
    }

    [Fact]
    public void ServerCatalogReferencesOnlyKnownRegionsAndGames()
    {
        var catalog = DataCatalog.Servers();
        var regions = catalog.Regions.Select(region => region.Id).ToHashSet();
        var games = DataCatalog.Games().Select(game => game.Id).ToHashSet();
        Assert.All(catalog.Games, entry =>
        {
            Assert.Contains(entry.Game, games);
            Assert.True(entry.ValveSdrAppId is not null || entry.Regions.Length > 0, entry.Game);
            Assert.All(entry.Regions, region => Assert.Contains(region, regions));
        });
    }

    [Fact]
    public void RankPutsUnreachableServersLast()
    {
        var ranked = GameServerCatalog.Rank([("A", new PingStats(3, 0, 0, 0, 0, 0)), ("B", new PingStats(3, 3, 40, 39, 41, 1)), ("C", new PingStats(3, 3, 12, 11, 13, 1))]);
        Assert.Equal(["C", "B", "A"], ranked.Select(item => item.Name));
    }

    [Fact]
    public void StreamingAppsGoToTheBackgroundCoresOnlyWithOptIn()
    {
        var catalog = new ProcessCatalog(DataCatalog.Processes());
        ProcessSample[] samples = [new(1, "cs2", PriorityLevel.Normal, 50), new(2, "obs64", PriorityLevel.Normal, 10), new(3, "Discord", PriorityLevel.Normal, 1)];
        Assert.Empty(new GuardianPolicy(catalog, ["cs2.exe"]).StreamingCpuSetTargets(samples));
        var policy = new GuardianPolicy(catalog, ["cs2.exe"], settings: new GuardianSettings(ObsCpuSets: true));
        Assert.Equal([2], policy.StreamingCpuSetTargets(samples));
        Assert.Empty(policy.StreamingCpuSetTargets([samples[1]]));
        Assert.True(new GuardianSettings().WifiScanOff);
    }
}
