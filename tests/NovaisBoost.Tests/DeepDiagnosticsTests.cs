using NovaisBoost.Core;

namespace NovaisBoost.Tests;

/// <summary>F13 rules: each one fires on a fake snapshot with the problem and stays quiet on a healthy one.</summary>
public sealed class DeepDiagnosticsTests
{
    private const long Gib = 1_073_741_824;
    private static readonly DateOnly Today = DiagnosticsTests.Today;

    private static DeepInfo HealthyDeep() => new(
        new SystemEvents(30, 0, 0, 0, 0, 0, 0, 0), 0x12F,
        new PagefileInfo(true, [new PagefileUsage(@"C:\pagefile.sys", 4096)]),
        TrimEnabled: true, WindowedVrr: true,
        Games: [new GameInstall("Counter-Strike 2", @"C:\Steam\cs2.exe", DiskMedia.Ssd)],
        Overlays: ["Steam Overlay"], Optimizers: []);

    /// <summary>The healthy PC from <see cref="DiagnosticsTests"/>, with healthy deep data and a measured PCIe link.</summary>
    private static HardwareSnapshot Pc(Func<DeepInfo, DeepInfo>? deep = null)
    {
        var pc = DiagnosticsTests.HealthyPc();
        return pc with
        {
            Gpus = [pc.Gpus[0] with { Pcie = new PcieLink(16, 16, 4, 4), Sensors = new GpuSensors(45, 3, 0x1) }],
            Volumes = [pc.Volumes[0] with { Media = DiskMedia.Ssd }],
            Displays = [pc.Displays[0] with { Connection = DisplayConnection.DisplayPort }],
            Deep = (deep ?? (value => value))(HealthyDeep())
        };
    }

    private static IReadOnlyList<Finding> Findings(HardwareSnapshot pc) => Diagnostics.Evaluate(pc, Today).Findings;
    private static string[] Ids(HardwareSnapshot pc) => Findings(pc).Select(finding => finding.Id).ToArray();

    [Fact]
    public void HealthyPcWithDeepDataHasNoFindings() => Assert.Empty(Ids(Pc()));

    [Fact]
    public void NewFindingsNameTheirSource()
    {
        var pc = Pc(deep => deep with { TrimEnabled = false, Events = deep.Events! with { WheaFatal = 1 } });
        Assert.All(Findings(pc), finding => Assert.False(string.IsNullOrWhiteSpace(finding.Source)));
    }

    // ------------------------------------------------------------------ 13.1 PCIe

    [Fact]
    public void GpuOnFewerPcieLanesIsCritical()
    {
        var pc = Pc() with { Gpus = [Pc().Gpus[0] with { Pcie = new PcieLink(8, 16, 4, 4) }] };
        var finding = Assert.Single(Findings(pc));
        Assert.Equal(("gpu-pcie-width", Severity.Critical), (finding.Id, finding.Severity));
        Assert.Contains("x8", finding.Title);
    }

    [Fact]
    public void LaptopGpuOnFewerLanesIsOnlyInfo()
    {
        var pc = Pc() with { Gpus = [Pc().Gpus[0] with { Pcie = new PcieLink(8, 16, 4, 4) }], Os = Pc().Os with { IsLaptop = true } };
        Assert.Equal(Severity.Info, Assert.Single(Findings(pc)).Severity);
    }

    [Fact]
    public void LowerPcieGenerationIsInfoAndFullLinkIsQuiet()
    {
        var idle = Pc() with { Gpus = [Pc().Gpus[0] with { Pcie = new PcieLink(16, 16, 1, 4) }] };
        var finding = Assert.Single(Findings(idle));
        Assert.Equal(("gpu-pcie-gen", Severity.Info), (finding.Id, finding.Severity));
        Assert.Empty(Ids(Pc()));
    }

    // ------------------------------------------------------------------ 13.2 memory channels

    [Theory]
    [InlineData("DIMM_A1", "DIMM_A2", true)]
    [InlineData("ChannelA-DIMM1", "ChannelA-DIMM2", true)]
    [InlineData("DIMM_A2", "DIMM_B2", false)]
    [InlineData("ChannelA-DIMM1", "ChannelB-DIMM1", false)]
    [InlineData("DDR5_A1", "DDR5_B1", false)]
    [InlineData("BANK 0", "BANK 1", false)]
    public void ModulesOnTheSameChannelAreFlagged(string first, string second, bool flagged)
    {
        var memory = Pc().Memory!;
        var pc = Pc() with { Memory = memory with { Modules = [memory.Modules[0] with { Slot = first }, memory.Modules[1] with { Slot = second }] } };
        Assert.Equal(flagged, Ids(pc).Contains("ram-same-channel"));
    }

    [Fact]
    public void ChannelIsReadFromTheBankLabelToo()
    {
        var module = new MemoryModule("DIMM 1", 16 * Gib, 6000, 6000, MemoryKind.Ddr5, null, null, "P0 CHANNEL B");
        Assert.Equal("B", KnownSoftware.ChannelOf(module, "LENOVO", DataCatalog.MemorySlots()));
        Assert.Null(KnownSoftware.ChannelOf(module with { Bank = "BANK 2" }, "LENOVO", DataCatalog.MemorySlots()));
    }

    // ------------------------------------------------------------------ 13.3 / 13.4 / 13.5 event log

    [Fact]
    public void FatalWheaIsCriticalAndCorrectedIsAWarning()
    {
        var fatal = Assert.Single(Findings(Pc(deep => deep with { Events = deep.Events! with { WheaFatal = 1, WheaCorrected = 5 } })));
        Assert.Equal(("whea-fatal", Severity.Critical), (fatal.Id, fatal.Severity));
        var corrected = Assert.Single(Findings(Pc(deep => deep with { Events = deep.Events! with { WheaCorrected = 3 } })));
        Assert.Equal(("whea-corrected", Severity.Warning), (corrected.Id, corrected.Severity));
    }

    [Theory]
    [InlineData(0, 0, 0, null)]
    [InlineData(1, 1, 1, Severity.Warning)]
    [InlineData(2, 2, 0, Severity.Warning)]
    [InlineData(1, 3, null, Severity.Critical)]
    [InlineData(0, 1, 4, Severity.Critical)]
    public void CrashesOverTwoInThirtyDaysAreCritical(int bugChecks, int kernelPower, int? minidumps, Severity? expected)
    {
        var pc = Pc(deep => deep with { Events = deep.Events! with { BugChecks = bugChecks, UnexpectedShutdowns = kernelPower, Minidumps = minidumps } });
        Assert.Equal(expected, Findings(pc).SingleOrDefault(finding => finding.Id == "crashes")?.Severity);
    }

    [Fact]
    public void DisplayDriverResetsAreAWarning()
    {
        var finding = Assert.Single(Findings(Pc(deep => deep with { Events = deep.Events! with { DisplayResets = 1, NvidiaDriverErrors = 2 } })));
        Assert.Equal(("gpu-tdr", Severity.Warning), (finding.Id, finding.Severity));
        Assert.Contains("3 vez", finding.Title);
    }

    // ------------------------------------------------------------------ 13.6 microcode

    private static HardwareSnapshot Intel(string cpu, uint revision) => Pc(deep => deep with { MicrocodeRevision = revision }) with
    {
        Cpu = new CpuInfo(cpu, "GenuineIntel", 24, 32, 3000)
    };

    [Theory]
    [InlineData("13th Gen Intel(R) Core(TM) i9-13900K", 0x125u, "microcode-outdated")]
    [InlineData("14th Gen Intel(R) Core(TM) i7-14700KF", 0x12Bu, "microcode-update")]
    [InlineData("14th Gen Intel(R) Core(TM) i5-14600K", 0x12Fu, null)]
    [InlineData("13th Gen Intel(R) Core(TM) i7-13700H", 0x4121u, null)]
    [InlineData("12th Gen Intel(R) Core(TM) i7-12700K", 0x35u, null)]
    public void RaptorLakeMicrocodeIsChecked(string cpu, uint revision, string? expected) =>
        Assert.Equal(expected, Ids(Intel(cpu, revision)).SingleOrDefault());

    [Fact]
    public void MicrocodeRuleIgnoresAmd()
    {
        var pc = Pc(deep => deep with { MicrocodeRevision = 0x1 }) with { Cpu = new CpuInfo("AMD Ryzen 9 i9-13900K lookalike", "AuthenticAMD", 8, 16, 4000) };
        Assert.Empty(Ids(pc));
        Assert.Equal(0x12Bu, DataCatalog.Microcode().Single().MinimumRevision);
    }

    // ------------------------------------------------------------------ 13.7 GPU sensors

    [Fact]
    public void ThermalThrottlingGpuIsAWarning()
    {
        var hot = Pc() with { Gpus = [Pc().Gpus[0] with { Sensors = new GpuSensors(84, 99, GpuSensors.HwThermalSlowdown) }] };
        Assert.Equal(("gpu-thermal", Severity.Warning), (Assert.Single(Findings(hot)).Id, Assert.Single(Findings(hot)).Severity));
        var cool = Pc() with { Gpus = [Pc().Gpus[0] with { Sensors = new GpuSensors(70, 99, 0) }] };
        Assert.Empty(Ids(cool));
    }

    [Fact]
    public void PowerCapUnderLoadIsInfoButNotAtIdle()
    {
        var loaded = Pc() with { Gpus = [Pc().Gpus[0] with { Sensors = new GpuSensors(70, 97, GpuSensors.SwPowerCap) }] };
        Assert.Equal("gpu-power-limit", Assert.Single(Ids(loaded)));
        var idle = Pc() with { Gpus = [Pc().Gpus[0] with { Sensors = new GpuSensors(40, 2, GpuSensors.SwPowerCap) }] };
        Assert.Empty(Ids(idle));
    }

    // ------------------------------------------------------------------ 13.8 CPU under load

    [Fact]
    public void CpuLimitedUnderLoadIsAWarning()
    {
        var finding = Assert.Single(CpuLoadRules.Evaluate(new CpuLoadSample(17, 98, 72, 90)));
        Assert.Equal(("cpu-throttling", Severity.Warning), (finding.Id, finding.Severity));
        Assert.Equal(FindingCategory.Power, finding.Category);
    }

    [Fact]
    public void HealthyCpuUnderLoadIsQuietAndBelowBaseIsFlagged()
    {
        Assert.Empty(CpuLoadRules.Evaluate(new CpuLoadSample(17, 118, 100, 100)));
        Assert.Equal("cpu-below-base", Assert.Single(CpuLoadRules.Evaluate(new CpuLoadSample(17, 85, 100, 80))).Id);
    }

    // ------------------------------------------------------------------ 13.9 / 13.10 / 13.13 storage

    [Fact]
    public void GameOnHddIsAWarning()
    {
        var pc = Pc(deep => deep with { Games = [new GameInstall("Fortnite", @"D:\Epic\Fortnite.exe", DiskMedia.Hdd), .. deep.Games!] });
        var finding = Assert.Single(Findings(pc));
        Assert.Equal(("game-on-hdd", Severity.Warning), (finding.Id, finding.Severity));
        Assert.Contains("Fortnite", finding.Title);
        Assert.DoesNotContain("Counter-Strike", finding.Title);
    }

    [Fact]
    public void DisabledPagefileIsCritical()
    {
        var off = Pc(deep => deep with { Pagefile = new PagefileInfo(false, []) });
        Assert.Equal(("pagefile-off", Severity.Critical), (Assert.Single(Findings(off)).Id, Assert.Single(Findings(off)).Severity));
        var manual = Pc(deep => deep with { Pagefile = new PagefileInfo(false, [new PagefileUsage(@"C:\pagefile.sys", 16384)]) });
        Assert.Empty(Ids(manual));
    }

    [Fact]
    public void PagefileOnHddWithAnSsdAvailableIsAWarning()
    {
        var pc = Pc(deep => deep with { Pagefile = new PagefileInfo(false, [new PagefileUsage(@"D:\pagefile.sys", 8192)]) }) with
        {
            Volumes = [Pc().Volumes[0], new VolumeInfo("D:", 2000 * Gib, 1000 * Gib, false, DiskMedia.Hdd)]
        };
        Assert.Equal("pagefile-hdd", Assert.Single(Ids(pc)));
    }

    [Fact]
    public void TrimOffWithSsdIsCriticalButNotWithOnlyHdds()
    {
        var ssd = Pc(deep => deep with { TrimEnabled = false });
        Assert.Equal(("trim-off", Severity.Critical), (Assert.Single(Findings(ssd)).Id, Assert.Single(Findings(ssd)).Severity));
        var hdd = ssd with { Disks = [new DiskInfo("WD Blue", DiskMedia.Hdd, "SATA", DiskHealth.Healthy, 1000 * Gib, true)] };
        Assert.DoesNotContain("trim-off", Ids(hdd));
    }

    // ------------------------------------------------------------------ 13.11 / 13.14 software

    [Fact]
    public void SeveralOverlaysAreInfo()
    {
        var pc = Pc(deep => deep with { Overlays = ["Steam Overlay", "Discord Overlay", "RivaTuner Statistics Server (RTSS)"] });
        var finding = Assert.Single(Findings(pc));
        Assert.Equal(("overlays", Severity.Info), (finding.Id, finding.Severity));
        Assert.Contains("3 overlays", finding.Title);
        Assert.Empty(Ids(Pc()));
    }

    [Fact]
    public void OverlayNamesComeFromProcesses()
    {
        Assert.Equal(["RivaTuner Statistics Server (RTSS)", "Steam Overlay"],
            KnownSoftware.MatchOverlays(["explorer", "rtss", "GameOverlayUI", "cs2"]));
        Assert.Empty(KnownSoftware.MatchOverlays(["explorer", "Discord"]));
    }

    [Fact]
    public void OtherOptimizersAreReported()
    {
        var finding = Assert.Single(Findings(Pc(deep => deep with { Optimizers = ["Razer Cortex"] })));
        Assert.Equal(("other-optimizers", Severity.Info), (finding.Id, finding.Severity));
        Assert.Contains("Razer Cortex", finding.Title);
    }

    [Fact]
    public void OptimizersAreMatchedByProgramNameOrProcess()
    {
        var known = DataCatalog.Optimizers();
        Assert.Equal(["Razer Cortex", "Process Lasso"],
            KnownSoftware.MatchOptimizers(known, ["Razer Cortex 10.15", "Google Chrome"], ["ProcessGovernor"]));
        Assert.Empty(KnownSoftware.MatchOptimizers(known, ["Steam", "Discord", "NOVAIS boost"], ["explorer"]));
    }

    // ------------------------------------------------------------------ 13.12 display connection and VRR

    [Theory]
    [InlineData(DisplayConnection.Hdmi, 60, true)]
    [InlineData(DisplayConnection.Hdmi, 120, true)]
    [InlineData(DisplayConnection.Hdmi, 144, false)]
    [InlineData(DisplayConnection.DisplayPort, 60, false)]
    public void HdmiCappedRefreshRateIsAWarning(DisplayConnection connection, int hz, bool flagged)
    {
        var pc = Pc() with { Displays = [new DisplayInfo("XG27", 2560, 1440, hz, hz, connection)] };
        Assert.Equal(flagged, Ids(pc).Contains("display-hdmi"));
    }

    [Fact]
    public void WindowsVrrOffIsInfoAndUnsetIsQuiet()
    {
        Assert.Equal("vrr-off", Assert.Single(Ids(Pc(deep => deep with { WindowedVrr = false }))));
        Assert.Empty(Ids(Pc(deep => deep with { WindowedVrr = null })));
    }

    // ------------------------------------------------------------------ 13.15 health score v2

    [Fact]
    public void ManyDiskProblemsCannotZeroTheScore()
    {
        // 3 critical (45 points) + 10 almost-full data disks (20 points) = 65 points, capped at the storage limit.
        var volumes = Enumerable.Range(0, 10).Select(index => new VolumeInfo($"{(char)('D' + index)}:", 100 * Gib, 5 * Gib, false, DiskMedia.Ssd));
        var pc = Pc() with
        {
            Volumes = [new VolumeInfo("C:", 100 * Gib, 5 * Gib, true, DiskMedia.Hdd), .. volumes],
            Disks = [new DiskInfo("WD Blue", DiskMedia.Hdd, "SATA", DiskHealth.Unhealthy, 100 * Gib, true)]
        };
        var report = Diagnostics.Evaluate(pc, Today);
        Assert.Equal(13, report.Findings.Count);
        Assert.Equal(65, report.Findings.Sum(finding => finding.Penalty));
        Assert.Equal(100 - Diagnostics.CategoryCaps[FindingCategory.Storage], report.Score);
    }

    [Fact]
    public void StabilityWeighsMoreThanStorage()
    {
        Assert.True(Diagnostics.CategoryCaps[FindingCategory.Stability] > Diagnostics.CategoryCaps[FindingCategory.Storage]);
        var report = Diagnostics.Evaluate(Pc(deep => deep with { Events = deep.Events! with { WheaFatal = 2, BugChecks = 5, DisplayResets = 1 } }), Today);
        Assert.Equal(15 + 15 + 7, report.Categories[FindingCategory.Stability]);
        Assert.Equal(100 - 37, report.Score);
    }

    [Theory]
    [InlineData("whea-fatal", FindingCategory.Stability)]
    [InlineData("gpu-pcie-width", FindingCategory.Graphics)]
    [InlineData("disk-space-d", FindingCategory.Storage)]
    [InlineData("ram-same-channel", FindingCategory.Memory)]
    [InlineData("display-hdmi", FindingCategory.Display)]
    [InlineData("other-optimizers", FindingCategory.Software)]
    [InlineData("uptime", FindingCategory.Windows)]
    public void FindingsAreGroupedByCategory(string id, FindingCategory category) => Assert.Equal(category, Diagnostics.CategoryOf(id));

    [Fact]
    public void NewKnowledgeFilesLoad()
    {
        Assert.NotEmpty(DataCatalog.Microcode());
        Assert.NotEmpty(DataCatalog.MemorySlots());
        Assert.NotEmpty(DataCatalog.Optimizers());
        Assert.All(DataCatalog.Microcode(), item => Assert.True(item.MinimumRevision <= item.RecommendedRevision));
    }
}
