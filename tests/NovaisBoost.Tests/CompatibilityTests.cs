using NovaisBoost.Core;

namespace NovaisBoost.Tests;

public sealed class CompatibilityTests
{
    private static HardwareSnapshot Pc(string cpu = "AMD Ryzen 5 7600X 6-Core Processor", bool laptop = false, params string[] gpus) => new(
        DateTimeOffset.Now,
        new OsInfo("Windows 11", "26200", "25H2", TimeSpan.Zero, laptop, false),
        new CpuInfo(cpu, "AuthenticAMD", 6, 12, 4700),
        (gpus.Length == 0 ? ["NVIDIA GeForce RTX 4070"] : gpus).Select(name => new GpuInfo(name, null, null, null, null)).ToArray(),
        null, null, [], [], [], [], null, new SystemFlags(null, null, null, null), [], []);

    private static string? Check(HardwareSnapshot pc, params string[] tokens) => Compatibility.Check(new TweakCompatibility(tokens, []), pc);

    [Theory]
    [InlineData("AMD Ryzen 9 7950X3D 16-Core Processor", true)]
    [InlineData("AMD Ryzen 9 9900X3D 12-Core Processor", true)]
    [InlineData("AMD Ryzen 7 7800X3D 8-Core Processor", false)]
    [InlineData("AMD Ryzen 7 9800X3D 8-Core Processor", false)]
    [InlineData("AMD Ryzen 9 7950X 16-Core Processor", false)]
    public void DetectsDualCcdX3d(string cpu, bool dual) => Assert.Equal(dual, Compatibility.IsDualCcdX3d(cpu));

    [Fact]
    public void DualCcdX3dBlocksThePowerPlan()
    {
        Assert.Null(Check(Pc(), "desktop", "not-dual-ccd-x3d"));
        Assert.Contains("3D V-Cache", Check(Pc("AMD Ryzen 9 7950X3D 16-Core Processor"), "desktop", "not-dual-ccd-x3d"));
    }

    [Fact]
    public void LaptopFailsDesktopRequirement() => Assert.NotNull(Check(Pc(laptop: true), "desktop"));

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4070", true)]
    [InlineData("NVIDIA GeForce GTX 1060 6GB", true)]
    [InlineData("NVIDIA GeForce GTX 960", false)]
    [InlineData("AMD Radeon RX 7800 XT", true)]
    [InlineData("AMD Radeon RX 580", false)]
    [InlineData("AMD Radeon(TM) Graphics", false)]
    [InlineData("Intel(R) Arc(TM) B580 Graphics", true)]
    public void HagsSupport(string gpu, bool supported) => Assert.Equal(supported, Check(Pc(gpus: gpu), "gpu-hags") is null);

    [Fact]
    public void UnknownTokenIsNeverSatisfied() => Assert.Contains("desconhecido", Check(Pc(), "warp-drive"));
}
