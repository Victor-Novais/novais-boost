using NovaisBoost.Core;

namespace NovaisBoost.Tests;

public sealed partial class TweakEngineTests : IDisposable
{
    private const string Owner = "test-account";
    private const string RegistryPath = @"Software\Example";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NovaisBoost.Tests", Guid.NewGuid().ToString("N"));
    private readonly FakeWindows _windows = new();
    private readonly FakeRestorePoints _points = new();
    private readonly RecordingLog _log = new();

    private SnapshotManager Snapshots => CreateSnapshots(Owner);
    private TweakEngine Engine => CreateEngine(Owner);

    [Fact]
    public void RegistrySetRestoresThePreviousTypeAndValue()
    {
        var action = RegistrySet("Value");
        _windows.Seed(action, RegistryValue.From(RegistryValueType.ExpandString, @"%TEMP%\dir"));
        var tweak = Tweak("registry", action);

        Assert.Single(Engine.Detect(tweak));
        Engine.Apply(tweak, dryRun: false);
        Assert.Empty(Engine.Detect(tweak));
        Engine.Revert(tweak.Id, dryRun: false);

        var before = _windows.Read(action);
        Assert.True(before.KeyExists);
        Assert.Equal(RegistryValueType.ExpandString, before.Value?.Kind);
        Assert.Equal(@"%TEMP%\dir", before.Value?.Value.GetString());
        Assert.Null(Snapshots.GetActive());
    }

    [Fact]
    public void RegistryDeleteRestoresAnExistingDefaultValue()
    {
        var action = new RegistryAction(RegistryHiveName.CurrentUser, RegistryPath, "", RegistryOperation.Delete, null);
        _windows.Seed(action, RegistryValue.From(RegistryValueType.Dword, -1));
        var tweak = Tweak("delete", action);

        Assert.Single(Engine.Apply(tweak, dryRun: false));
        Assert.Empty(Engine.Detect(tweak));
        Assert.Equal(new RegistryState(true, null), _windows.Read(action));
        Engine.Revert(tweak.Id, dryRun: false);

        Assert.Equal(-1, _windows.Read(action).Value?.Value.GetInt32());
    }

    [Fact]
    public void ServiceStartupRoundTripsIncludingDelayedAutomatic()
    {
        var tweak = Tweak("service", new ServiceAction("FakeService", ServiceStartup.Disabled));
        _windows.Services["FakeService"] = ServiceStartup.AutomaticDelayed;

        Assert.Single(Engine.Detect(tweak));
        Engine.Apply(tweak, dryRun: false);
        Assert.Empty(Engine.Detect(tweak));
        Assert.Equal(ServiceStartup.Disabled, _windows.Services["FakeService"]);
        Engine.Revert(tweak.Id, dryRun: false);

        Assert.Equal(ServiceStartup.AutomaticDelayed, _windows.Services["FakeService"]);
    }

    [Fact]
    public void ActivePowerSchemeRoundTrips()
    {
        var original = _windows.Scheme;
        var expected = Guid.NewGuid();
        var tweak = Tweak("power", new PowerAction(expected));

        Assert.Single(Engine.Detect(tweak));
        Engine.Apply(tweak, dryRun: false);
        Assert.Empty(Engine.Detect(tweak));
        Assert.Equal(expected, _windows.Scheme);
        Engine.Revert(tweak.Id, dryRun: false);

        Assert.Equal(original, _windows.Scheme);
    }

    [Fact]
    public void DefaultApplyAndRevertAreReadOnly()
    {
        _windows.Services["FakeService"] = ServiceStartup.Manual;
        var tweak = Tweak("mixed", RegistrySet("Value"), new ServiceAction("FakeService", ServiceStartup.Disabled),
            new PowerAction(Guid.NewGuid()));

        Assert.Equal(3, Engine.Apply(tweak).Count);
        Assert.Equal(0, _windows.Writes);
        Assert.Equal(0, _points.Requests);
        Assert.False(Directory.Exists(_directory));

        Engine.Apply(tweak, dryRun: false);
        var snapshot = Snapshots.GetActive()!;
        var saved = File.ReadAllText(snapshot.Path);
        Assert.Equal(3, Engine.Revert(tweak.Id).Count);
        Assert.Equal(3, Engine.RevertAll().Count);
        Assert.Equal(3, _windows.Writes);
        Assert.Equal(1, _points.Requests);
        Assert.Equal(saved, File.ReadAllText(snapshot.Path));
    }

    [Fact]
    public void EveryWriteFollowsARestorePointAndPersistedBackup()
    {
        var action = RegistrySet("Expected");
        var tweak = Tweak("verify-order", action);
        _windows.BeforeWrite = () =>
        {
            var active = Snapshots.GetActive();
            Assert.NotNull(active);
            Assert.True(active.Pending);
            Assert.True(_points.Requests > 0);
            Assert.Contains("Expected", File.ReadAllText(active.Path));
            if (_points.Requests == 2)
                Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(active.Path)!, "pre-revert-*.json"));
        };

        Engine.Apply(tweak, dryRun: false);
        Engine.RevertAll(dryRun: false);
        Assert.Equal(2, _windows.Writes);
        Assert.False(_windows.Read(action).KeyExists);
    }

    [Fact]
    public void EmbeddedExamplesRestoreACombinationOfMissingKeysMissingValuesAndExistingValues()
    {
        string[] ids = ["game-dvr-background", "mouse-acceleration", "visual-transparency"];
        var examples = TweakCatalog.LoadEmbedded().Where(tweak => ids.Contains(tweak.Id)).ToArray();
        var actions = examples.SelectMany(tweak => tweak.Actions).Cast<RegistryAction>().ToArray();
        var mouse = actions.Single(action => action.ValueName == "MouseSpeed");
        var capture = actions.First(action => action.ValueName == "HistoricalCaptureEnabled");
        _windows.Seed(mouse, RegistryValue.From(RegistryValueType.String, "1"));
        _windows.Seed(capture, RegistryValue.From(RegistryValueType.Dword, 1));
        var before = actions.Select(_windows.Read).ToArray();

        foreach (var tweak in examples)
        {
            Assert.NotEmpty(Engine.Apply(tweak, dryRun: false));
            Assert.Empty(Engine.Detect(tweak));
        }
        var active = Snapshots.GetActive();
        Assert.NotNull(active);
        Assert.Equal(3, active.TweakCount);
        Assert.False(active.Pending);
        Assert.True(File.Exists(active.Path));
        Assert.Equal(6, Engine.RevertAll(dryRun: false).Count);

        Assert.Equal(before.Select(Render), actions.Select(action => Render(_windows.Read(action))));
        Assert.Null(Snapshots.GetActive());
        Assert.True(File.Exists(active.Path));
    }

    [Fact]
    public void RevertAllRestoresFirstStateOfOverlappingTargetsAndKeys()
    {
        var first = RegistrySet("First");
        var second = RegistrySet("Second");
        Engine.Apply(Tweak("first", first), dryRun: false);
        Engine.Apply(Tweak("second", second, first with { Expected = RegistryValue.From(RegistryValueType.Dword, 9) }), dryRun: false);

        Assert.Equal(2, Engine.RevertAll(dryRun: false).Count);
        Assert.False(_windows.Read(first).KeyExists);
        Assert.False(_windows.Read(second).KeyExists);
    }

    [Fact]
    public void DisabledProtectionPreventsWrites()
    {
        _points.Failure = new SystemProtectionDisabledException();
        Assert.Throws<SystemProtectionDisabledException>(() => Engine.Apply(Tweak("blocked", RegistrySet("Value")), dryRun: false));
        Assert.Equal(0, _windows.Writes);
        Assert.Null(Snapshots.GetActive());
    }

    [Fact]
    public void ApplyRollsBackAPartialWrite()
    {
        var first = RegistrySet("First");
        var second = RegistrySet("Second");
        _windows.BeforeWrite = () =>
        {
            if (_windows.Writes == 1)
            {
                _windows.BeforeWrite = null;
                throw new IOException("Simulated failure.");
            }
        };

        Assert.Throws<IOException>(() => Engine.Apply(Tweak("partial", first, second), dryRun: false));
        Assert.False(_windows.Read(first).KeyExists);
        Assert.Null(Snapshots.GetActive());
    }

    [Fact]
    public void InterruptedReversalKeepsTheBackupForRetry()
    {
        var first = RegistrySet("First");
        var second = RegistrySet("Second");
        Engine.Apply(Tweak("initial", first, second), dryRun: false);
        _windows.BeforeWrite = () =>
        {
            if (_windows.Writes == 3)
                throw new IOException("Simulated failure.");
        };

        Assert.Throws<IOException>(() => Engine.RevertAll(dryRun: false));
        Assert.True(Snapshots.GetActive()!.Pending);
        Assert.Throws<InvalidOperationException>(() => Engine.Apply(Tweak("another", RegistrySet("Another")), dryRun: false));
        _windows.BeforeWrite = null;
        Engine.RevertAll(dryRun: false);

        Assert.False(_windows.Read(first).KeyExists);
        Assert.Null(Snapshots.GetActive());
    }

    [Fact]
    public void JournalIsIsolatedByAccount()
    {
        var tweak = Tweak("registry", RegistrySet("Value"));
        Engine.Apply(tweak, dryRun: false);
        Assert.Null(CreateSnapshots("different-account").GetActive());
        Assert.Empty(CreateEngine("different-account").RevertAll(dryRun: false));
        Assert.NotNull(Snapshots.GetActive());
    }

    [Fact]
    public void ChangedPreviewRequiresReviewAgain()
    {
        var action = RegistrySet("Value");
        var tweak = Tweak("preview", action);
        var reviewed = Engine.Apply(tweak);
        _windows.Seed(action, RegistryValue.From(RegistryValueType.Dword, 99));

        Assert.Throws<InvalidOperationException>(() => Engine.Apply(tweak, dryRun: false, reviewedChanges: reviewed));
        Assert.Equal(0, _windows.Writes);
        Assert.Equal(0, _points.Requests);
    }

    [Fact]
    public void ChangesWhileCreatingRestorePointAreReviewedAgainBeforeWriting()
    {
        var action = RegistrySet("Value");
        var tweak = Tweak("preview", action);
        var reviewed = Engine.Apply(tweak);
        _points.OnEnsure = () => _windows.Seed(action, RegistryValue.From(RegistryValueType.Dword, 99));

        Assert.Throws<InvalidOperationException>(() => Engine.Apply(tweak, dryRun: false, reviewedChanges: reviewed));
        Assert.Null(Snapshots.GetActive());
        Assert.Equal(0, _windows.Writes);
    }

    [Fact]
    public void AConcurrentWriterCannotAcquireTheBackupLock()
    {
        var tweak = Tweak("first", RegistrySet("Value"));
        var concurrent = Tweak("second", RegistrySet("Another"));
        _windows.BeforeWrite = () => Assert.Throws<IOException>(() => Engine.Apply(concurrent, dryRun: false));

        Engine.Apply(tweak, dryRun: false);
        Assert.Equal(1, _windows.Writes);
        Assert.Equal(1, Snapshots.GetActive()!.TweakCount);
    }

    [Fact]
    public void UnverifiedCompatibilityBlocksApplication()
    {
        var tweak = Tweak("blocked", RegistrySet("Value")) with { Compat = new(["Windows 11"], ["Vanguard"]) };
        Assert.Single(Engine.Apply(tweak));
        Assert.Throws<InvalidOperationException>(() => Engine.Apply(tweak, dryRun: false));
        Assert.Equal(0, _windows.Writes);
    }

    [Fact]
    public void RevertLatestUndoesOnlyTheTestedGroup()
    {
        var (first, second, third) = (RegistrySet("First"), RegistrySet("Second"), RegistrySet("Third"));
        _windows.Seed(first, RegistryValue.From(RegistryValueType.Dword, 5));
        _windows.Seed(third, RegistryValue.From(RegistryValueType.Dword, 7));
        Engine.Apply(Tweak("base", first), dryRun: false);
        Engine.Apply(Tweak("group-a", second), dryRun: false);
        Engine.Apply(Tweak("group-b", third), dryRun: false);

        var preview = Engine.RevertLatest(["group-a", "group-b", "not-applied"]);
        Assert.Equal(2, preview.Count);
        Engine.RevertLatest(["group-a", "group-b", "not-applied"], dryRun: false, reviewedChanges: preview);

        Assert.Equal(0, _windows.Read(first).Value?.Value.GetInt32()); // still applied
        Assert.Null(_windows.Read(second).Value);
        Assert.Equal(7, _windows.Read(third).Value?.Value.GetInt32());
        Assert.Equal(["base"], Snapshots.GetActive()!.TweakIds);
        Assert.Empty(Engine.RevertLatest(["group-a"]));
    }

    [Fact]
    public void RevertLatestRefusesWhenSomethingWasAppliedAfterTheGroup()
    {
        Engine.Apply(Tweak("group", RegistrySet("First")), dryRun: false);
        Engine.Apply(Tweak("later", RegistrySet("Second")), dryRun: false);
        var error = Assert.Throws<InvalidOperationException>(() => Engine.RevertLatest(["group", "other"]));
        Assert.Contains("depois", error.Message);
        Assert.Equal(["group", "later"], Snapshots.GetActive()!.TweakIds);
    }

    [Fact]
    public void RevertLatestOfTheWholeBackupClosesIt()
    {
        Engine.Apply(Tweak("only", RegistrySet("First")), dryRun: false);
        Engine.RevertLatest(["only"], dryRun: false);
        Assert.Null(Snapshots.GetActive());
        Assert.False(_windows.Read(RegistrySet("First")).KeyExists);
    }

    [Fact]
    public void AntiCheatListAloneDoesNotBlockApplication()
    {
        var tweak = Tweak("anti-cheat-warning", RegistrySet("Value")) with { Compat = new([], ["Vanguard"]) };
        Assert.Null(Engine.CheckCompatibility(tweak));
        Engine.Apply(tweak, dryRun: false);
        Assert.Equal(1, _windows.Writes);
    }

    [Fact]
    public void LegacyMmcssGamingBackupStillReverts()
    {
        // A backup written before F11, when "mmcss-gaming" was a single tweak (state.json as saved by v1).
        const string profile = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
        string Entry(string path, string name, string expected, string before) => $$"""
            { "action": { "type": "registry", "hive": "localMachine", "path": {{System.Text.Json.JsonSerializer.Serialize(path)}}, "valueName": "{{name}}",
                          "operation": "set", "expected": {{expected}}, "accept": null },
              "before": { "type": "registry", "keyExists": true, "value": {{before}}, "native": null, "missingSegments": 0 } }
            """;
        static string Dword(int value) => $$"""{ "kind": "dword", "value": {{value}} }""";
        static string Text(string value) => $$"""{ "kind": "string", "value": "{{value}}" }""";
        var games = profile + @"\Tasks\Games";
        var settings = string.Join(",", Entry(profile, "SystemResponsiveness", Dword(10), Dword(20)),
            Entry(profile, "NetworkThrottlingIndex", Dword(-1), Dword(10)),
            Entry(games, "Priority", Dword(6), Dword(2)),
            Entry(games, "Scheduling Category", Text("High"), Text("Medium")),
            Entry(games, "SFIO Priority", Text("High"), Text("Normal")));
        var folder = Path.Combine(_directory, "backups", $"20261101-120000000-{Owner}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "state.json"), $$"""
            { "version": 1, "owner": "{{Owner}}", "restorePointNumber": 7, "pending": false, "closed": false,
              "tweaks": [ { "id": "mmcss-gaming", "settings": [ {{settings}} ] } ] }
            """);

        var catalog = TweakCatalog.LoadEmbedded();
        var split = catalog.Where(tweak => tweak.Id is "mmcss-responsiveness" or "mmcss-games-task").ToArray();
        Assert.Equal(2, split.Length);
        Assert.DoesNotContain(catalog, tweak => tweak.Id == "mmcss-gaming");
        foreach (var action in split.SelectMany(tweak => tweak.Actions).Cast<RegistryAction>())
            _windows.Seed(action, action.Expected!);
        Assert.All(split, tweak => Assert.Empty(Engine.Detect(tweak)));
        Assert.Equal(["mmcss-gaming"], Snapshots.GetActive()!.TweakIds);

        Assert.Equal(5, Engine.RevertAll(dryRun: false).Count);

        int? Read(string name) => split.SelectMany(tweak => tweak.Actions).Cast<RegistryAction>()
            .Where(action => action.ValueName == name).Select(action => _windows.Read(action).Value)
            .Select(value => value?.Kind == RegistryValueType.Dword ? value.Value.GetInt32() : (int?)null).Single();
        string? ReadText(string name) => split.SelectMany(tweak => tweak.Actions).Cast<RegistryAction>()
            .Where(action => action.ValueName == name).Select(action => _windows.Read(action).Value?.Value.GetString()).Single();
        Assert.Equal(20, Read("SystemResponsiveness"));
        Assert.Equal(10, Read("NetworkThrottlingIndex"));
        Assert.Equal(2, Read("Priority"));
        Assert.Equal(8, Read("GPU Priority")); // was already 8 before: never captured, left as is
        Assert.Equal("Medium", ReadText("Scheduling Category"));
        Assert.Equal("Normal", ReadText("SFIO Priority"));
        Assert.Null(Snapshots.GetActive());
    }

    [Fact]
    public void ScheduledTaskRoundTripsAndMissingTasksAreSkipped()
    {
        _windows.Tasks[@"\Telemetry"] = true;
        var tweak = Tweak("tasks", new ScheduledTaskAction(@"\Telemetry", TaskSetting.Disabled),
            new ScheduledTaskAction(@"\DoesNotExist", TaskSetting.Disabled));

        Assert.Single(Engine.Detect(tweak));
        Engine.Apply(tweak, dryRun: false);
        Assert.False(_windows.Tasks[@"\Telemetry"]);
        Assert.False(_windows.Tasks.ContainsKey(@"\DoesNotExist"));
        Engine.RevertAll(dryRun: false);

        Assert.True(_windows.Tasks[@"\Telemetry"]);
    }

    [Fact]
    public void NvidiaSettingReturnsToDriverDefaultOnRevert()
    {
        var tweak = Tweak("nvidia", new NvidiaSettingAction(0x1057EB71, "Power management mode", 1));
        Assert.Equal("padrão do driver", Assert.Single(Engine.Detect(tweak)).Current);

        Engine.Apply(tweak, dryRun: false);
        Assert.Equal(1u, _windows.Nvidia[0x1057EB71]);
        Assert.Empty(Engine.Detect(tweak));

        Engine.RevertAll(dryRun: false);
        Assert.False(_windows.Nvidia.ContainsKey(0x1057EB71));
    }

    [Fact]
    public void DnsReturnsToAutomaticOnRevert()
    {
        var adapter = Guid.NewGuid();
        var tweak = Tweak("dns", new DnsAction(adapter, "Ethernet", ["1.1.1.1", "1.0.0.1"]));

        var change = Assert.Single(Engine.Apply(tweak, dryRun: false));
        Assert.Equal("automático (DHCP)", change.Current);
        Assert.Equal(["1.1.1.1", "1.0.0.1"], _windows.Dns[adapter]);

        Engine.RevertAll(dryRun: false);
        Assert.Empty(_windows.Dns[adapter]);
    }

    [Fact]
    public void ActiveBackupListsAppliedTweakIds()
    {
        Engine.Apply(Tweak("first", RegistrySet("First")), dryRun: false);
        Engine.Apply(Tweak("second", RegistrySet("Second")), dryRun: false);
        Assert.Equal(["first", "second"], Snapshots.GetActive()!.TweakIds);
    }

    [Fact]
    public void AcceptedValueIsLeftUntouched()
    {
        var action = RegistrySet("Telemetry") with
        {
            Expected = RegistryValue.From(RegistryValueType.Dword, 1),
            Accept = [RegistryValue.From(RegistryValueType.Dword, 0)]
        };
        _windows.Seed(action, RegistryValue.From(RegistryValueType.Dword, 0));
        Assert.Empty(Engine.Detect(Tweak("accept", action)));

        _windows.Seed(action, RegistryValue.From(RegistryValueType.Dword, 3));
        Assert.Single(Engine.Detect(Tweak("accept", action)));
    }

    [Fact]
    public void MissingServicesAreSkipped()
    {
        _windows.Services["Installed"] = ServiceStartup.Manual;
        var tweak = Tweak("services", new ServiceAction("Installed", ServiceStartup.Disabled), new ServiceAction("Missing", ServiceStartup.Disabled));

        Assert.Single(Engine.Apply(tweak, dryRun: false));
        Assert.False(_windows.Services.ContainsKey("Missing"));
    }

    [Fact]
    public void OwnPowerPlanIsCreatedActivatedAndDeletedOnRevert()
    {
        var original = _windows.Scheme;
        var id = Guid.NewGuid();
        PowerSettingValue[] settings = [new(Guid.NewGuid(), Guid.NewGuid(), 100, 100), new(Guid.NewGuid(), Guid.NewGuid(), 0, 0)];
        var tweak = Tweak("plan", new PowerPlanAction(id, "NOVAIS boost", Guid.NewGuid(), settings), new PowerAction(id));

        Assert.Equal(2, Engine.Apply(tweak, dryRun: false).Count);
        Assert.Equal(id, _windows.Scheme);
        Assert.Equal(settings, _windows.Plans[id]);
        Assert.Empty(Engine.Detect(tweak));

        // A setting changed by hand is detected and fixed on the next apply.
        _windows.Plans[id] = [settings[0] with { Ac = 50 }, settings[1]];
        Assert.Single(Engine.Detect(tweak));

        Engine.RevertAll(dryRun: false);
        Assert.Equal(original, _windows.Scheme);
        Assert.False(_windows.Plans.ContainsKey(id));
    }

    [Fact]
    public void RevertRemovesTheAncestorKeysTheTweakCreated()
    {
        _windows.MissingSegments = 3;
        Engine.Apply(Tweak("policy", RegistrySet("Policy")), dryRun: false);
        _windows.MissingSegments = 0;
        Engine.RevertAll(dryRun: false);

        Assert.Equal(3, _windows.LastRegistryWrite!.MissingSegments);
        Assert.False(_windows.LastRegistryWrite.KeyExists);
    }

    [Fact]
    public void HardwareRequirementsAreCheckedBeforeWriting()
    {
        var tweak = Tweak("desktop-only", RegistrySet("Value")) with { Compat = new(["desktop"], []) };
        Assert.Throws<InvalidOperationException>(() => Engine.Apply(tweak, dryRun: false));

        _compatibility = _ => "requer um desktop";
        var error = Assert.Throws<InvalidOperationException>(() => Engine.Apply(tweak, dryRun: false));
        Assert.Contains("requer um desktop", error.Message);
        Assert.Equal(0, _windows.Writes);

        _compatibility = _ => null;
        Assert.Single(Engine.Apply(tweak, dryRun: false));
    }

    [Fact]
    public void BatchUsesOneRestorePointAndContinuesAfterAFailure()
    {
        var first = Tweak("first", RegistrySet("First"));
        var broken = Tweak("broken", RegistrySet("Broken"));
        var third = Tweak("third", RegistrySet("Third"));
        var engine = Engine;
        var reviewed = new[] { first, broken, third }.ToDictionary(tweak => tweak.Id, engine.Detect);
        _windows.BeforeWrite = () =>
        {
            if (_windows.Writes == 1)
            {
                _windows.BeforeWrite = null;
                throw new IOException("Simulated failure.");
            }
        };

        var results = engine.ApplyBatch([first, broken, third], reviewed);

        Assert.Equal([null, "Simulated failure.", null], results.Select(result => result.Error));
        Assert.Equal(1, _points.Requests);
        Assert.Null(_windows.Read(RegistrySet("Broken")).Value);
        Assert.Equal(2, Snapshots.GetActive()!.TweakCount);
        engine.RevertAll(dryRun: false);
        Assert.False(_windows.Read(RegistrySet("First")).KeyExists);
        Assert.False(_windows.Read(RegistrySet("Third")).KeyExists);
    }

    private SnapshotManager CreateSnapshots(string owner) => new(_directory, owner, _points, _log);

    private TweakEngine CreateEngine(string owner) =>
        new(_windows, _windows, _windows, CreateSnapshots(owner), _log, _windows, _windows, _compatibility, _windows, _windows,
            _windows, _windows, _windows, _windows, _windows, _windows, _windows, _windows, _windows, _windows, _windows);

    private Func<TweakCompatibility, string?>? _compatibility;

    private static RegistryAction RegistrySet(string name) =>
        new(RegistryHiveName.CurrentUser, RegistryPath, name, RegistryOperation.Set, RegistryValue.From(RegistryValueType.Dword, 0));

    private static TweakDefinition Tweak(string id, params TweakAction[] actions) =>
        new(id, "Ajuste", "Descrição", "Teste", TweakLevel.Safe, TweakEvidence.Documented, "Baixo", false, new([], []), actions);

    private static string Render(RegistryState state) => $"{state.KeyExists}/{state.Value?.Kind}/{state.Value?.Value.GetRawText()}";

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private sealed class FakeWindows : IRegistry, IServiceController, IPowerConfig, IScheduledTasks, IPowerPlans, INvidiaSettings, IDnsSettings,
        IBcdStore, IPagefile, ITcpSettings, IConfigFiles, INvidiaProfiles, IAmdSettings, IDefenderExclusions, IAppxPackages, ISystemFeatures, IDirectXSettings, IDevices
    {
        public string? DirectX { get; set; }

        string? IDirectXSettings.Read(string key) => DirectXSettings.Get(DirectX, key);

        void IDirectXSettings.Write(string key, string? value)
        {
            OnWrite();
            DirectX = DirectXSettings.Set(DirectX, key, value);
        }

        public Dictionary<string, bool> Devices { get; } = new(StringComparer.OrdinalIgnoreCase);

        bool? IDevices.IsEnabled(string instanceId) => Devices.TryGetValue(instanceId, out var enabled) ? enabled : null;

        void IDevices.SetEnabled(string instanceId, bool enabled)
        {
            OnWrite();
            Devices[instanceId] = enabled;
        }

        public HashSet<string> Packages { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Reinstalled { get; } = [];
        public Exception? InstallFailure { get; set; }

        bool IAppxPackages.IsInstalled(string name) => Packages.Contains(name);

        void IAppxPackages.Remove(string name)
        {
            OnWrite();
            Packages.Remove(name);
        }

        void IAppxPackages.Install(string name, string storeId)
        {
            OnWrite();
            if (InstallFailure is not null)
                throw InstallFailure;
            Reinstalled.Add(storeId);
            Packages.Add(name);
        }

        public Dictionary<SystemFeature, bool> Features { get; } = [];

        bool? ISystemFeatures.IsEnabled(SystemFeature feature) => Features.TryGetValue(feature, out var enabled) ? enabled : null;

        void ISystemFeatures.Set(SystemFeature feature, bool enabled)
        {
            OnWrite();
            Features[feature] = enabled;
        }

        public HashSet<string> Exclusions { get; } = new(StringComparer.OrdinalIgnoreCase);

        bool IDefenderExclusions.Contains(string path) => Exclusions.Contains(path);

        void IDefenderExclusions.Set(string path, bool excluded)
        {
            OnWrite();
            if (excluded)
                Exclusions.Add(path);
            else
                Exclusions.Remove(path);
        }

        /// <summary>Driver profiles: name → (predefined, apps, settings). Mirrors how NvidiaDrs treats them.</summary>
        public Dictionary<string, (bool Predefined, HashSet<string> Apps, Dictionary<uint, uint> Settings)> Profiles { get; } = [];
        public Dictionary<string, bool> Amd { get; } = [];
        public bool AmdAvailable { get; set; } = true;

        private string? ProfileOf(string exe) => Profiles.FirstOrDefault(pair => pair.Value.Apps.Contains(exe)).Key;

        NvidiaProfileState INvidiaProfiles.Read(string exe, uint settingId) => ProfileOf(exe) is { } name
            ? Profiles[name].Settings.TryGetValue(settingId, out var value) ? new(true, true, value) : new(true, false, 0)
            : new(false, false, 0);

        void INvidiaProfiles.Write(string exe, uint settingId, NvidiaProfileState state)
        {
            OnWrite();
            var name = ProfileOf(exe);
            if (!state.AppKnown)
            {
                if (name is not null && name.StartsWith("NOVAIS boost - ", StringComparison.Ordinal))
                    Profiles.Remove(name);
                else if (name is not null)
                    Profiles[name].Settings.Remove(settingId);
                return;
            }
            if (name is null)
            {
                name = "NOVAIS boost - " + exe;
                Profiles[name] = (false, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { exe }, []);
            }
            if (state.Defined)
                Profiles[name].Settings[settingId] = state.Value;
            else
                Profiles[name].Settings.Remove(settingId);
        }

        bool IAmdSettings.IsAvailable => AmdAvailable;
        AmdSettingState IAmdSettings.Read(string feature) => new(Amd.GetValueOrDefault(feature));

        void IAmdSettings.Write(string feature, AmdSettingState state)
        {
            OnWrite();
            Amd[feature] = state.Enabled;
        }

        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Running { get; } = new(StringComparer.OrdinalIgnoreCase);

        ConfigFileState IConfigFiles.Read(ConfigFileAction action) => ConfigFiles.Read(action, Files.GetValueOrDefault(action.Path));

        void IConfigFiles.Write(ConfigFileAction action, ConfigFileState state)
        {
            OnWrite();
            Files[action.Path] = ConfigFiles.Compute(action, Files[action.Path], state);
        }

        bool IConfigFiles.IsInUse(ConfigFileAction action) => action.LockedBy.Any(Running.Contains);

        public Dictionary<string, string> Bcd { get; } = new(StringComparer.OrdinalIgnoreCase);
        public PagefileState Pagefile { get; set; } = new(true, []);
        public Dictionary<string, string> Tcp { get; } = new(StringComparer.OrdinalIgnoreCase) { ["autotuninglevel"] = "normal", ["rss"] = "enabled" };

        BcdState IBcdStore.Read(string element) => new(Bcd.GetValueOrDefault(element));

        void IBcdStore.Write(string element, BcdState state)
        {
            OnWrite();
            if (state.Value is null)
                Bcd.Remove(element);
            else
                Bcd[element] = state.Value;
        }

        PagefileState IPagefile.Read() => Pagefile;

        void IPagefile.Write(PagefileState state)
        {
            OnWrite();
            Pagefile = state.SystemManaged ? new PagefileState(true, []) : state;
        }

        TcpState ITcpSettings.Read(string setting) => new(Tcp[setting]);

        void ITcpSettings.Write(string setting, TcpState state)
        {
            OnWrite();
            Tcp[setting] = state.Value;
        }

        public Dictionary<uint, uint> Nvidia { get; } = [];
        public Dictionary<Guid, string[]> Dns { get; } = [];

        public NvidiaSettingState Read(uint settingId) =>
            Nvidia.TryGetValue(settingId, out var value) ? new NvidiaSettingState(true, value) : new NvidiaSettingState(false, 0);

        public void Write(uint settingId, NvidiaSettingState state)
        {
            OnWrite();
            if (state.Defined)
                Nvidia[settingId] = state.Value;
            else
                Nvidia.Remove(settingId);
        }

        public DnsState Read(DnsAction action) => new(Dns.GetValueOrDefault(action.InterfaceId, []));

        public void Write(DnsAction action, DnsState state)
        {
            OnWrite();
            Dns[action.InterfaceId] = state.Servers;
        }

        public Dictionary<string, bool> Tasks { get; } = [];
        public Dictionary<Guid, PowerSettingValue[]> Plans { get; } = [];
        public int MissingSegments { get; set; }
        public RegistryState? LastRegistryWrite { get; private set; }

        public TaskState Get(string path) => Tasks.TryGetValue(path, out var enabled) ? new TaskState(true, enabled) : new TaskState(false, false);

        public void SetEnabled(string path, bool enabled)
        {
            OnWrite();
            Tasks[path] = enabled;
        }

        public PowerPlanState Read(PowerPlanAction plan) =>
            Plans.TryGetValue(plan.Id, out var settings) ? new PowerPlanState(true, settings) : new PowerPlanState(false, null);

        public void Write(PowerPlanAction plan, PowerPlanState state)
        {
            OnWrite();
            if (state.Exists)
                Plans[plan.Id] = state.Settings!;
            else
            {
                Assert.NotEqual(plan.Id, Scheme);
                Plans.Remove(plan.Id);
            }
        }

        private readonly Dictionary<(RegistryHiveName Hive, string Path), Dictionary<string, RegistryValue>> _keys = [];
        public Dictionary<string, ServiceStartup> Services { get; } = [];
        public Guid Scheme { get; private set; } = Guid.NewGuid();
        public int Writes { get; private set; }
        public Action? BeforeWrite { get; set; }

        public RegistryState Read(RegistryAction action) => _keys.TryGetValue((action.Hive, action.Path), out var key)
            ? new RegistryState(true, key.GetValueOrDefault(action.ValueName))
            : new RegistryState(false, null, null, MissingSegments);

        public void Seed(RegistryAction action, RegistryValue value) => SetValue(action, new RegistryState(true, value));

        public void Write(RegistryAction action, RegistryState state)
        {
            OnWrite();
            LastRegistryWrite = state;
            SetValue(action, state);
        }

        private void SetValue(RegistryAction action, RegistryState state)
        {
            var address = (action.Hive, action.Path);
            if (!_keys.TryGetValue(address, out var key))
            {
                if (!state.KeyExists)
                    return;
                key = new Dictionary<string, RegistryValue>(StringComparer.OrdinalIgnoreCase);
                _keys.Add(address, key);
            }

            if (state.Value is not null)
                key[action.ValueName] = state.Value;
            else
                key.Remove(action.ValueName);
            if (!state.KeyExists && key.Count == 0)
                _keys.Remove(address);
        }

        public ServiceStartup? GetStartup(string name) => Services.TryGetValue(name, out var startup) ? startup : null;

        public void SetStartup(string name, ServiceStartup startup)
        {
            OnWrite();
            Services[name] = startup;
        }

        public Guid GetActiveScheme() => Scheme;

        public void SetActiveScheme(Guid scheme)
        {
            OnWrite();
            Scheme = scheme;
        }

        private void OnWrite()
        {
            BeforeWrite?.Invoke();
            Writes++;
        }
    }

    private sealed class FakeRestorePoints : IRestorePointProvider
    {
        public int Requests { get; private set; }
        public Exception? Failure { get; set; }
        public Action? OnEnsure { get; set; }

        public RestorePoint Ensure()
        {
            Requests++;
            if (Failure is not null)
                throw Failure;
            OnEnsure?.Invoke();
            return new RestorePoint(42, Requests > 1);
        }
    }

    private sealed class RecordingLog : IActionLog
    {
        public List<string> Messages { get; } = [];
        public void Info(string message) => Messages.Add(message);
        public void Warning(string message) => Messages.Add(message);
        public void Error(string message) => Messages.Add(message);
    }
}
