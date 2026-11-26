using NovaisBoost.Core;

namespace NovaisBoost.Tests;

public sealed class DiagnosticsTests
{
    private const long Gib = 1_073_741_824;
    private static readonly DateOnly Today = new(2026, 11, 26);

    private static HardwareSnapshot HealthyPc() => new(
        DateTimeOffset.Now,
        new OsInfo("Windows 11 Pro", "26200", "25H2", TimeSpan.FromHours(5), IsLaptop: false, OnBattery: false),
        new CpuInfo("AMD Ryzen 7 7800X3D 8-Core Processor", "AuthenticAMD", 8, 16, 4200),
        [new GpuInfo("NVIDIA GeForce RTX 4070", "32.0.15.9000", Today.AddDays(-20), 12 * Gib, ResizableBar: true)],
        new MemoryInfo(32 * Gib, 20 * Gib,
        [
            new MemoryModule("DIMM A2", 16 * Gib, 6000, 6000, MemoryKind.Ddr5, "Kingston", "KF560C30-16"),
            new MemoryModule("DIMM B2", 16 * Gib, 6000, 6000, MemoryKind.Ddr5, "Kingston", "KF560C30-16")
        ]),
        new BoardInfo("ASUS", "TUF GAMING B650-PLUS", "3201", Today.AddMonths(-3)),
        [new DiskInfo("Samsung 990 PRO", DiskMedia.Ssd, "NVMe", DiskHealth.Healthy, 1000 * Gib, IsSystem: true)],
        [new VolumeInfo("C:", 930 * Gib, 400 * Gib, IsSystem: true)],
        [new DisplayInfo("Monitor", 1920, 1080, 240, 240)],
        [new NetworkInfo("Realtek 2.5GbE", Wired: true, Connected: true, 2_500_000_000)],
        new PowerPlanInfo(PowerPlanInfo.HighPerformance, "Alto desempenho"),
        new SystemFlags(GameMode: true, HardwareGpuScheduling: true, FastStartup: false, MemoryIntegrity: false),
        [new ProcessUsage("chrome", 30, 3 * Gib)],
        []);

    private static IReadOnlyList<string> Ids(HardwareSnapshot pc) =>
        Diagnostics.Evaluate(pc, Today).Findings.Select(finding => finding.Id).ToArray();

    [Fact]
    public void HealthyPcScoresOneHundredWithNoFindings()
    {
        var report = Diagnostics.Evaluate(HealthyPc(), Today);
        Assert.Equal(100, report.Score);
        Assert.Empty(report.Findings);
    }

    [Fact]
    public void MemoryAboveNinetyPercentIsCriticalAndNamesTopProcesses()
    {
        var pc = HealthyPc() with { Memory = HealthyPc().Memory! with { AvailableBytes = Gib / 2 } };
        var finding = Assert.Single(Diagnostics.Evaluate(pc, Today).Findings);
        Assert.Equal(("memory-pressure", Severity.Critical), (finding.Id, finding.Severity));
        Assert.Contains("chrome", finding.Detail);
    }

    [Fact]
    public void RamBelowRatedSpeedFlagsXmpExpo()
    {
        var modules = HealthyPc().Memory!.Modules.Select(module => module with { ConfiguredMts = 4800 }).ToArray();
        var pc = HealthyPc() with { Memory = HealthyPc().Memory! with { Modules = modules } };
        Assert.Equal(["xmp-expo"], Ids(pc));
    }

    [Theory]
    [InlineData(MemoryKind.Ddr5, 4800, true)]
    [InlineData(MemoryKind.Ddr5, 6000, false)]
    [InlineData(MemoryKind.Ddr4, 2400, true)]
    [InlineData(MemoryKind.Ddr4, 3200, false)]
    public void JedecSpeedWithoutRatedInfoIsAWarning(MemoryKind kind, int speed, bool flagged)
    {
        var modules = HealthyPc().Memory!.Modules.Select(module => module with { Kind = kind, RatedMts = speed, ConfiguredMts = speed }).ToArray();
        var pc = HealthyPc() with { Memory = HealthyPc().Memory! with { Modules = modules } };
        Assert.Equal(flagged, Ids(pc).Contains("xmp-expo-likely"));
    }

    [Fact]
    public void SingleModuleIsSingleChannel()
    {
        var pc = HealthyPc() with { Memory = HealthyPc().Memory! with { Modules = [HealthyPc().Memory!.Modules[0]] } };
        Assert.Contains("single-channel", Ids(pc));
    }

    [Theory]
    [InlineData(12, Severity.Warning)]
    [InlineData(5, Severity.Critical)]
    public void LowSystemDiskSpaceIsFlagged(int freePercent, Severity severity)
    {
        var pc = HealthyPc() with { Volumes = [new VolumeInfo("C:", 100 * Gib, freePercent * Gib, IsSystem: true)] };
        var finding = Assert.Single(Diagnostics.Evaluate(pc, Today).Findings);
        Assert.Equal(("system-disk-space", severity), (finding.Id, finding.Severity));
    }

    [Fact]
    public void MonitorBelowMaxRefreshRateIsCritical()
    {
        var pc = HealthyPc() with { Displays = [new DisplayInfo("XG27", 2560, 1440, 60, 170)] };
        var finding = Assert.Single(Diagnostics.Evaluate(pc, Today).Findings);
        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Contains("170 Hz", finding.Title);
    }

    [Fact]
    public void IntegratedPlusDiscreteGpuIsReported()
    {
        var pc = HealthyPc() with { Gpus = [.. HealthyPc().Gpus, new GpuInfo("AMD Radeon(TM) Graphics", "31.0", Today, Gib / 2, null)] };
        Assert.Equal(["hybrid-gpu"], Ids(pc));
    }

    [Fact]
    public void OldDriverAndDisabledResizableBarAreWarnings()
    {
        var pc = HealthyPc() with { Gpus = [HealthyPc().Gpus[0] with { DriverDate = Today.AddDays(-400), ResizableBar = false }] };
        Assert.Equal(["gpu-driver-age", "resizable-bar"], Ids(pc));
    }

    [Fact]
    public void WindowsSettingsAndNetworkAreChecked()
    {
        var pc = HealthyPc() with
        {
            PowerPlan = new PowerPlanInfo(PowerPlanInfo.PowerSaver, "Economia de energia"),
            Flags = new SystemFlags(GameMode: false, HardwareGpuScheduling: false, FastStartup: true, MemoryIntegrity: true),
            Networks = [new NetworkInfo("Intel Wi-Fi 6E", Wired: false, Connected: true, 1_200_000_000)],
            Os = HealthyPc().Os with { Uptime = TimeSpan.FromDays(9) }
        };
        Assert.Equal(["power-plan", "game-mode", "wifi", "fast-startup", "hags", "memory-integrity", "uptime"], Ids(pc));
    }

    [Fact]
    public void WslVirtualMachineHoldingRamIsReported()
    {
        var pc = HealthyPc() with { TopProcesses = [new ProcessUsage("vmmemWSL", 1, 15 * Gib), .. HealthyPc().TopProcesses] };
        var finding = Assert.Single(Diagnostics.Evaluate(pc, Today).Findings);
        Assert.Equal("wsl-vm", finding.Id);
        Assert.Contains("wsl --shutdown", finding.Recommendation);
    }

    [Fact]
    public void HighNonPagedPoolSuggestsDriverLeak()
    {
        var pc = HealthyPc() with { Memory = HealthyPc().Memory! with { NonPagedPoolBytes = 3 * Gib } };
        Assert.Equal(["nonpaged-pool"], Ids(pc));
    }

    [Fact]
    public void ThirdPartyPowerPlanIsReportedButDefaultsAreNot()
    {
        var custom = HealthyPc() with { PowerPlan = new PowerPlanInfo(Guid.NewGuid(), "OutroBoost - Ultimate") };
        Assert.Equal(["custom-power-plan"], Ids(custom));
        var ultimate = HealthyPc() with { PowerPlan = new PowerPlanInfo(PowerPlanInfo.UltimatePerformance, "Desempenho Máximo") };
        Assert.Empty(Ids(ultimate));
    }

    [Fact]
    public void ScoreNeverDropsBelowZero()
    {
        var pc = HealthyPc() with
        {
            Memory = new MemoryInfo(8 * Gib, 0, [new MemoryModule("A", 8 * Gib, 3200, 2133, MemoryKind.Ddr4, null, null)]),
            Volumes = [new VolumeInfo("C:", 100 * Gib, Gib, IsSystem: true)],
            Disks = [new DiskInfo("HDD", DiskMedia.Hdd, "SATA", DiskHealth.Unhealthy, 500 * Gib, IsSystem: true)],
            Displays = [new DisplayInfo("A", 1920, 1080, 60, 144), new DisplayInfo("B", 1920, 1080, 60, 165)],
            PowerPlan = new PowerPlanInfo(PowerPlanInfo.PowerSaver, "Economia"),
            Os = HealthyPc().Os with { IsLaptop = true, OnBattery = true }
        };
        Assert.Equal(0, Diagnostics.Evaluate(pc, Today).Score);
    }

    [Theory]
    [InlineData("AMD Radeon(TM) Graphics", true)]
    [InlineData("AMD Radeon RX 7800 XT", false)]
    [InlineData("Intel(R) UHD Graphics 770", true)]
    [InlineData("Intel(R) Arc(TM) Graphics", true)]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", false)]
    [InlineData("Intel(R) Arc(TM) B580 Graphics", false)]
    [InlineData("NVIDIA GeForce RTX 4070", false)]
    public void ClassifiesIntegratedGpus(string name, bool integrated) =>
        Assert.Equal(integrated, GpuClassifier.IsIntegrated(name));
}
