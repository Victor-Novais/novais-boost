using NovaisBoost.Core;

namespace NovaisBoost.Tests;

/// <summary>F17: Guardian v2 rules (pure) and the Defender exclusion action (fake).</summary>
public sealed class GuardianTests
{
    private const long Mb = 1L << 20, Gb = 1L << 30;

    private static GuardianPolicy Policy(GuardianSettings? settings = null) => new(new ProcessCatalog(DataCatalog.Processes()),
        DataCatalog.Games().Where(game => game.Id != "obs").SelectMany(game => game.Executables), PriorityLevel.AboveNormal, settings,
        DataCatalog.Games().Where(game => game.ProtectedByAntiCheat).SelectMany(game => game.Executables));

    private static readonly ProcessSample[] WithValorant =
    [
        new(1, "VALORANT-Win64-Shipping", PriorityLevel.Normal, 40),
        new(2, "msedge", PriorityLevel.Normal, 1),
        new(3, "Discord", PriorityLevel.Normal, 3),
        new(4, "obs64", PriorityLevel.Normal, 5),
        new(5, "svchost", PriorityLevel.Normal, 30),
        new(6, "Renderer", PriorityLevel.Normal, 25)
    ];

    // ------------------------------------------------------------------ anti-cheat protection (17.4)

    [Fact]
    public void ProtectedGameIsNeverTouchedWithoutOptIn()
    {
        var changes = Policy().Plan(WithValorant);
        Assert.DoesNotContain(changes, change => change.Name == "VALORANT-Win64-Shipping");
        Assert.Contains(changes, change => change.Name == "msedge");
        Assert.Empty(Policy(new GuardianSettings(GameCpuSets: true)).GameCpuSetTargets(WithValorant));
    }

    [Fact]
    public void OptInTouchesTheProtectedGamePriorityButNeverItsCpuSets()
    {
        var policy = Policy(new GuardianSettings(TouchProtectedGames: true, GameCpuSets: true));
        Assert.Contains(policy.Plan(WithValorant), change => change.Name == "VALORANT-Win64-Shipping" && change.To == PriorityLevel.AboveNormal);
        Assert.Empty(policy.GameCpuSetTargets(WithValorant));
    }

    [Fact]
    public void VacGamesAreNotProtectedAndGameCpuSetsAreOptIn()
    {
        Assert.False(DataCatalog.Games().Single(game => game.Id == "cs2").ProtectedByAntiCheat);
        Assert.True(DataCatalog.Games().Single(game => game.Id == "valorant").ProtectedByAntiCheat);
        ProcessSample[] cs2 = [new(1, "cs2", PriorityLevel.Normal, 40)];
        Assert.Empty(Policy().GameCpuSetTargets(cs2));
        Assert.Equal([1], Policy(new GuardianSettings(GameCpuSets: true)).GameCpuSetTargets(cs2));
    }

    // ------------------------------------------------------------------ efficiency mode and CPU sets (17.2, 17.3)

    [Fact]
    public void HeavyBackgroundGoesToEfficiencyModeButNotTheGameVoiceOrStream()
    {
        var targets = Policy().EfficiencyTargets(WithValorant);
        Assert.Contains(2, targets);   // browser
        Assert.Contains(6, targets);   // unknown process using 25% CPU
        Assert.DoesNotContain(1, targets);
        Assert.DoesNotContain(3, targets);
        Assert.DoesNotContain(4, targets);
        Assert.DoesNotContain(5, targets); // system
        Assert.Equal(targets, Policy().CpuSetTargets(WithValorant));
    }

    [Fact]
    public void NothingHappensWithoutAGameOrWhenDisabled()
    {
        ProcessSample[] idle = [new(2, "msedge", PriorityLevel.Normal, 50)];
        Assert.Empty(Policy().EfficiencyTargets(idle));
        Assert.Empty(Policy().CpuSetTargets(idle));
        var off = Policy(new GuardianSettings(EfficiencyMode: false, BackgroundCpuSets: false));
        Assert.Empty(off.EfficiencyTargets(WithValorant));
        Assert.Empty(off.CpuSetTargets(WithValorant));
    }

    [Fact]
    public void UserListsLowerIgnoreAndEfficiency()
    {
        var policy = Policy(new GuardianSettings(Lower: ["Discord.exe"], Ignore: ["msedge"], Efficiency: ["notepad"]));
        ProcessSample[] samples = [.. WithValorant, new(7, "notepad", PriorityLevel.Normal, 0)];
        var changes = policy.Plan(samples);
        Assert.Contains(changes, change => change.Name == "Discord" && change.To == PriorityLevel.BelowNormal);
        Assert.DoesNotContain(changes, change => change.Name == "msedge");
        Assert.Contains(7, policy.EfficiencyTargets(samples));
        Assert.DoesNotContain(7, policy.CpuSetTargets(samples));
        Assert.DoesNotContain(2, policy.EfficiencyTargets(samples));
    }

    // ------------------------------------------------------------------ CPU topology

    [Fact]
    public void HybridIntelSendsBackgroundToECoresAndTheGameToPCores()
    {
        var sets = Enumerable.Range(0, 8).Select(index => new CpuSetInfo((uint)(256 + index), 1, 0))
            .Concat(Enumerable.Range(8, 8).Select(index => new CpuSetInfo((uint)(256 + index), 0, 0))).ToArray();
        var topology = new CpuTopology(sets, new Dictionary<byte, long> { [0] = 36 * Mb });
        Assert.True(topology.IsHybrid);
        Assert.Equal(Enumerable.Range(264, 8).Select(id => (uint)id), topology.BackgroundSets());
        Assert.Equal(Enumerable.Range(256, 8).Select(id => (uint)id), topology.GameSets());
    }

    [Fact]
    public void DualCcdX3dSendsBackgroundToTheCcdWithoutVCache()
    {
        var sets = Enumerable.Range(0, 16).Select(index => new CpuSetInfo((uint)(256 + index), 0, (byte)(index < 8 ? 0 : 8))).ToArray();
        var topology = new CpuTopology(sets, new Dictionary<byte, long> { [0] = 96 * Mb, [8] = 32 * Mb });
        Assert.True(topology.HasAsymmetricCache);
        Assert.Equal(Enumerable.Range(264, 8).Select(id => (uint)id), topology.BackgroundSets());
        Assert.Equal(Enumerable.Range(256, 8).Select(id => (uint)id), topology.GameSets());
    }

    [Fact]
    public void SingleCcdLeavesEverythingAlone()
    {
        var topology = new CpuTopology(Enumerable.Range(0, 12).Select(index => new CpuSetInfo((uint)index, 0, 0)).ToArray(),
            new Dictionary<byte, long> { [0] = 32 * Mb });
        Assert.Empty(topology.BackgroundSets());
        Assert.Empty(topology.GameSets());
        var twoEqual = new CpuTopology(Enumerable.Range(0, 16).Select(index => new CpuSetInfo((uint)index, 0, (byte)(index / 8))).ToArray(),
            new Dictionary<byte, long> { [0] = 32 * Mb, [1] = 32 * Mb });
        Assert.Empty(twoEqual.BackgroundSets()); // 7950X: two equal CCDs, the Windows scheduler already handles it
    }

    // ------------------------------------------------------------------ power plan, standby list, alerts

    [Fact]
    public void GamePowerPlanIsOptInAndNeverOnDualCcdX3d()
    {
        var on = new GuardianSettings(PowerPlanDuringGame: true);
        Assert.True(GuardianRules.UseGamePowerPlan(on, gameRunning: true, dualCcdX3d: false, planInstalled: true));
        Assert.False(GuardianRules.UseGamePowerPlan(on, gameRunning: true, dualCcdX3d: true, planInstalled: true));
        Assert.False(GuardianRules.UseGamePowerPlan(on, gameRunning: true, dualCcdX3d: false, planInstalled: false));
        Assert.False(GuardianRules.UseGamePowerPlan(new GuardianSettings(), gameRunning: true, dualCcdX3d: false, planInstalled: true));
    }

    [Fact]
    public void StandbyListIsPurgedOnlyUnderRealPressureAndAtMostEveryFiveMinutes()
    {
        var on = new GuardianSettings(StandbyPurge: true);
        var now = DateTimeOffset.Now;
        Assert.True(GuardianRules.ShouldPurgeStandby(on, 512 * Mb, 6 * Gb, null, now));
        Assert.False(GuardianRules.ShouldPurgeStandby(on, 2 * Gb, 6 * Gb, null, now));
        Assert.False(GuardianRules.ShouldPurgeStandby(on, 512 * Mb, Gb, null, now));
        Assert.False(GuardianRules.ShouldPurgeStandby(on, 512 * Mb, 6 * Gb, now.AddMinutes(-2), now));
        Assert.True(GuardianRules.ShouldPurgeStandby(on, 512 * Mb, 6 * Gb, now.AddMinutes(-6), now));
        Assert.False(GuardianRules.ShouldPurgeStandby(new GuardianSettings(), 512 * Mb, 6 * Gb, null, now));
    }

    [Fact]
    public void RamAndVramAlerts()
    {
        Assert.Empty(GuardianRules.Alerts(70, 60));
        Assert.Empty(GuardianRules.Alerts(null, null));
        Assert.Equal(2, GuardianRules.Alerts(93, 97).Count);
        Assert.Contains("Memória de vídeo", Assert.Single(GuardianRules.Alerts(50, 96)));
    }

    [Fact]
    public void ProcessGrowingForThirtyMinutesIsReportedOnce()
    {
        var tracker = new LeakTracker();
        var start = DateTimeOffset.Now;
        IReadOnlyList<string> reported = [];
        for (var minute = 0; minute <= 31; minute++)
        {
            var found = tracker.Record(start.AddMinutes(minute), [new ProcessSample(1, "LeakyApp", PriorityLevel.Normal, 1, Gb + minute * 40 * Mb),
                new ProcessSample(2, "Stable", PriorityLevel.Normal, 1, Gb + minute % 2 * 50 * Mb)]);
            if (found.Count > 0)
                reported = found;
        }
        Assert.Equal(["LeakyApp"], reported);
        Assert.Empty(tracker.Record(start.AddMinutes(32), [new ProcessSample(1, "LeakyApp", PriorityLevel.Normal, 1, 3 * Gb)]));
    }

    [Fact]
    public void SmallOrShortGrowthIsNotALeak()
    {
        var tracker = new LeakTracker();
        var start = DateTimeOffset.Now;
        for (var minute = 0; minute <= 31; minute++)
            Assert.Empty(tracker.Record(start.AddMinutes(minute), [new ProcessSample(1, "Small", PriorityLevel.Normal, 1, Gb + minute * Mb)]));
        var shortRun = new LeakTracker();
        for (var minute = 0; minute <= 10; minute++)
            Assert.Empty(shortRun.Record(start.AddMinutes(minute), [new ProcessSample(1, "Fast", PriorityLevel.Normal, 1, Gb + minute * 200 * Mb)]));
    }

    [Fact]
    public void GuardianSettingsParseTheNewOptions()
    {
        var settings = AppSettings.Parse("""
            { "guardian": { "gamePriority": "high", "touchProtectedGames": true, "efficiencyMode": false, "powerPlanDuringGame": true,
                            "standbyPurge": true, "lower": ["Discord"], "ignore": ["Spotify"] } }
            """).GuardianOrDefault;
        Assert.Equal((PriorityLevel.High, true, false, true, true), (settings.GamePriority, settings.TouchProtectedGames, settings.EfficiencyMode,
            settings.PowerPlanDuringGame, settings.StandbyPurge));
        Assert.Equal(["Discord"], settings.Lower);
        var defaults = new GuardianSettings();
        Assert.Equal((false, true, true, false, false, false), (defaults.TouchProtectedGames, defaults.EfficiencyMode, defaults.BackgroundCpuSets,
            defaults.GameCpuSets, defaults.PowerPlanDuringGame, defaults.StandbyPurge));
    }

    // ------------------------------------------------------------------ Defender exclusions (17.7)

    [Fact]
    public void DefenderExclusionsSkipBroadFolders()
    {
        var games = new[]
        {
            new InstalledGame(DataCatalog.Games().Single(game => game.Id == "cs2"), @"D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe"),
            new InstalledGame(DataCatalog.Games().Single(game => game.Id == "fortnite"), @"C:\Program Files\Fortnite.exe"),
            new InstalledGame(DataCatalog.Games().Single(game => game.Id == "apex"), @"E:\r5apex.exe")
        };
        var tweak = DefenderExclusions.Build(games, [@"C:\Program Files", @"C:\Windows"])!;
        var action = Assert.IsType<DefenderExclusionAction>(Assert.Single(tweak.Actions));
        Assert.Equal(@"D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\game\bin\win64", action.Path);
        Assert.Null(DefenderExclusions.Build([games[1], games[2]], [@"C:\Program Files"]));
    }
}

public sealed partial class TweakEngineTests
{
    [Fact]
    public void DefenderExclusionIsAddedAndRemovedOnRevert()
    {
        var tweak = DefenderExclusions.Build([new InstalledGame(DataCatalog.Games().Single(game => game.Id == "cs2"), @"D:\Games\CS2\cs2.exe")], [])!;
        Engine.Apply(tweak, dryRun: false);
        Assert.Contains(@"D:\Games\CS2", _windows.Exclusions);
        Assert.Empty(Engine.Detect(tweak));
        Engine.RevertAll(dryRun: false);
        Assert.Empty(_windows.Exclusions);
    }

    [Fact]
    public void ExclusionTheUserAlreadyHadStaysAfterRevert()
    {
        _windows.Exclusions.Add(@"D:\Games\CS2");
        var tweak = DefenderExclusions.Build([new InstalledGame(DataCatalog.Games().Single(game => game.Id == "cs2"), @"D:\Games\CS2\cs2.exe")], [])!;
        Assert.Empty(Engine.Apply(tweak, dryRun: false));
        Engine.RevertAll(dryRun: false);
        Assert.Contains(@"D:\Games\CS2", _windows.Exclusions);
    }
}
