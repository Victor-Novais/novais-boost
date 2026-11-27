using NovaisBoost.Core;

namespace NovaisBoost.App;

/// <summary>Everything a screen or scheduled task needs, created once per run. Expensive data is loaded lazily.</summary>
internal sealed class AppServices
{
    private HardwareSnapshot? _pc;
    private IReadOnlyList<InstalledGame>? _games;
    private IReadOnlyList<TweakDefinition>? _dynamic;
    private IReadOnlyList<HardwareProfile>? _profiles;
    private LocalEvidence? _evidence;
    private IReadOnlyList<TweakDefinition>? _repairs;

    private AppServices(string dataDirectory, IActionLog log, SnapshotManager snapshots)
    {
        DataDirectory = dataDirectory;
        Log = log;
        Snapshots = snapshots;
        Probe = new WindowsHardwareProbe(log, () => Games);
        Appx = new WindowsAppxPackages(log);
        var nvidia = new NvidiaDrs();
        Engine = new TweakEngine(new WindowsRegistry(), new WindowsServiceController(), new WindowsPowerConfig(), snapshots, log,
            new WindowsScheduledTasks(log), new WindowsPowerPlans(), compat => Compatibility.Check(compat, Pc),
            nvidia, new WindowsDnsSettings(), new WindowsBcdStore(), new WindowsPagefile(), new WindowsTcpSettings(), new WindowsConfigFiles(),
            nvidia, Amd, new WindowsDefenderExclusions(), Appx, new WindowsSystemFeatures(),
            new WindowsDirectXSettings(), new WindowsDevices());
    }

    public string DataDirectory { get; }
    public AmdAdlx Amd { get; } = new();
    public WindowsAppxPackages Appx { get; }
    public IActionLog Log { get; }
    public SnapshotManager Snapshots { get; }
    public WindowsHardwareProbe Probe { get; }
    public TweakEngine Engine { get; }
    public IReadOnlyList<TweakDefinition> StaticTweaks { get; } = TweakCatalog.LoadEmbedded();

    public static AppServices Create(string dataDirectory, IActionLog log, string owner) =>
        new(dataDirectory, log, new SnapshotManager(dataDirectory, owner, new WindowsRestorePoints(), log));

    public HardwareSnapshot Pc => _pc ??= Probe.Capture();
    public IReadOnlyList<InstalledGame> Games => _games ??= GameLibrary.Detect(DataCatalog.Games(), Log);
    public IReadOnlyList<TweakDefinition> DynamicTweaks => _dynamic ??=
        [.. new DeviceTweaks(Log).Generate(Games, Pc), .. DebloatTweaks.Generate(Log, Appx, new WindowsServiceController())];
    public IReadOnlyList<TweakDefinition> AllTweaks => [.. StaticTweaks, .. DynamicTweaks];

    /// <summary>Repair mode items (never part of AllTweaks or the levels).</summary>
    public IReadOnlyList<TweakDefinition> Repairs => _repairs ??= Core.Repairs.All(Environment.OSVersion.Version.Build.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public IReadOnlyList<HardwareProfile> Profiles => _profiles ??= ProfileMatcher.Match(DataCatalog.Profiles(), Pc);

    /// <summary>A/B test results measured on this PC (evidence.json).</summary>
    public LocalEvidence LocalEvidence => _evidence ??= LocalEvidence.Load(DataDirectory, Log);

    /// <summary>Tweak ids a matched hardware profile says must not run on this PC.</summary>
    public IReadOnlySet<string> AvoidedTweaks => Profiles.SelectMany(profile => profile.Avoid).ToHashSet(StringComparer.Ordinal);

    /// <summary>Forget cached detection (after a reboot-free change such as installing a game or applying tweaks).</summary>
    public void Refresh()
    {
        _pc = null;
        _games = null;
        _dynamic = null;
        _profiles = null;
        _evidence = null;
    }

    /// <summary>Read on every call so a change in settings.json takes effect the next time the Guardian starts.</summary>
    public AppSettings Settings => AppSettings.Load(DataDirectory, Log);

    public GuardianPolicy GuardianPolicy()
    {
        var games = DataCatalog.Games().Where(game => game.Id != "obs").ToArray();
        var settings = Settings.GuardianOrDefault;
        return new(new ProcessCatalog(DataCatalog.Processes()), games.SelectMany(game => game.Executables), settings.GamePriority, settings,
            games.Where(game => game.ProtectedByAntiCheat).SelectMany(game => game.Executables));
    }
}
