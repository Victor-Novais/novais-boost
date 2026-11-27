using NovaisBoost.Core;

namespace NovaisBoost.Tests;

/// <summary>F16: per-game NVIDIA driver profiles and Radeon settings, on fakes that mirror the driver.</summary>
public sealed partial class TweakEngineTests
{
    private static InstalledGame Game(string id, string exe) =>
        new(DataCatalog.Games().Single(game => game.Id == id), $@"C:\Games\{id}\{exe}");

    private static TweakDefinition NvidiaPower(string id, string exe) =>
        NvidiaProfiles.Build([Game(id, exe)], frameCap: null).Single();

    [Fact]
    public void GameWithoutProfileGetsOneThatRevertDeletes()
    {
        _compatibility = _ => null;
        var tweak = NvidiaPower("fortnite", "FortniteClient-Win64-Shipping.exe");
        Assert.Single(Engine.Detect(tweak));
        Engine.Apply(tweak, dryRun: false);

        var (name, profile) = Assert.Single(_windows.Profiles);
        Assert.Equal("NOVAIS boost - FortniteClient-Win64-Shipping.exe", name);
        Assert.Equal(NvidiaProfiles.PreferMaximumPerformance, profile.Settings[NvidiaProfiles.PowerManagementMode]);
        Assert.Empty(Engine.Detect(tweak));

        Engine.RevertAll(dryRun: false);
        Assert.Empty(_windows.Profiles);
    }

    [Fact]
    public void PredefinedProfileIsNeverDeletedOnlyItsSettingRestored()
    {
        _compatibility = _ => null;
        _windows.Profiles["Counter-strike 2"] = (true, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cs2.exe" },
            new Dictionary<uint, uint> { [0x1234] = 7 });
        Engine.Apply(NvidiaPower("cs2", "cs2.exe"), dryRun: false);
        Assert.Equal(1u, _windows.Profiles["Counter-strike 2"].Settings[NvidiaProfiles.PowerManagementMode]);

        Engine.RevertAll(dryRun: false);
        var profile = Assert.Single(_windows.Profiles).Value;
        Assert.DoesNotContain(NvidiaProfiles.PowerManagementMode, profile.Settings.Keys);
        Assert.Equal(7u, profile.Settings[0x1234]);
    }

    [Fact]
    public void PreviousValueInTheGameProfileComesBack()
    {
        _compatibility = _ => null;
        _windows.Profiles["Apex Legends"] = (true, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "r5apex.exe" },
            new Dictionary<uint, uint> { [NvidiaProfiles.PowerManagementMode] = 0 });
        Engine.Apply(NvidiaPower("apex", "r5apex.exe"), dryRun: false);
        Engine.RevertAll(dryRun: false);
        Assert.Equal(0u, _windows.Profiles["Apex Legends"].Settings[NvidiaProfiles.PowerManagementMode]);
    }

    [Fact]
    public void ProfileAlreadyAtMaximumPerformanceNeedsNothing()
    {
        // The real CS2 predefined profile already prefers maximum performance (checked on the driver).
        _windows.Profiles["Counter-strike 2"] = (true, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cs2.exe" },
            new Dictionary<uint, uint> { [NvidiaProfiles.PowerManagementMode] = 1 });
        Assert.Empty(Engine.Detect(NvidiaPower("cs2", "cs2.exe")));
    }

    [Fact]
    public void FrameLimiterIsOnlyOfferedWithACapAndIsExperimental()
    {
        Assert.Single(NvidiaProfiles.Build([Game("cs2", "cs2.exe")], frameCap: null));
        var both = NvidiaProfiles.Build([Game("cs2", "cs2.exe"), Game("obs", "obs64.exe")], frameCap: 141);
        Assert.Equal(["nvidia-profile-cs2", "nvidia-fps-limit-cs2"], both.Select(tweak => tweak.Id));
        Assert.Equal(TweakEvidence.Documented, both[0].Evidence);
        Assert.Equal(TweakEvidence.Experimental, both[1].Evidence);
        Assert.Equal(141u, Assert.IsType<NvidiaProfileAction>(Assert.Single(both[1].Actions)).Expected);
        Assert.All(both, tweak => Assert.Contains("gpu-nvidia", tweak.Compat.Hardware));
    }

    [Fact]
    public void RadeonSettingsRoundTripThroughAdlx()
    {
        _compatibility = _ => null;
        _windows.Amd["Chill"] = true;
        _windows.Amd["AntiLag"] = false;
        var tweak = AmdProfiles.Competitive();
        Assert.Equal(2, Engine.Apply(tweak, dryRun: false).Count);
        Assert.True(_windows.Amd["AntiLag"]);
        Assert.False(_windows.Amd["Chill"]);
        Engine.RevertAll(dryRun: false);
        Assert.False(_windows.Amd["AntiLag"]);
        Assert.True(_windows.Amd["Chill"]);
    }

    [Fact]
    public void SchemaAcceptsTheNewGpuActions()
    {
        const string template = """
            {
              "id": "gpu-x", "name": "GPU", "description": "Descrição", "category": "GPU", "level": "safe",
              "evidence": "documented", "risk": "Baixo", "requiresReboot": false, "compat": { "hardware": [], "antiCheat": [] },
              "actions": [ ACTION ]
            }
            """;
        var nvidia = TweakCatalog.Parse(template.Replace("ACTION", """{ "type": "nvidiaProfile", "exe": "cs2.exe", "settingId": 274197361, "name": "Power", "expected": 1 }"""), "n.json");
        Assert.IsType<NvidiaProfileAction>(Assert.Single(nvidia.Actions));
        var amd = TweakCatalog.Parse(template.Replace("ACTION", """{ "type": "amdSetting", "feature": "AntiLag", "enabled": true }"""), "a.json");
        Assert.IsType<AmdSettingAction>(Assert.Single(amd.Actions));
        Assert.Throws<InvalidDataException>(() => TweakCatalog.Parse(template.Replace("ACTION", """{ "type": "nvidiaProfile", "exe": "..\\x", "settingId": 1, "name": "P", "expected": 1 }"""), "bad.json"));
        Assert.Throws<InvalidDataException>(() => TweakCatalog.Parse(template.Replace("ACTION", """{ "type": "amdSetting", "feature": "Overclock", "enabled": true }"""), "bad2.json"));
    }
}
