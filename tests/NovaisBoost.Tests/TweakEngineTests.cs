using NovaisBoost.Core;

namespace NovaisBoost.Tests;

public sealed class TweakEngineTests : IDisposable
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
        var examples = TweakCatalog.LoadEmbedded();
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

    private SnapshotManager CreateSnapshots(string owner) => new(_directory, owner, _points, _log);

    private TweakEngine CreateEngine(string owner) => new(_windows, _windows, _windows, CreateSnapshots(owner), _log);

    private static RegistryAction RegistrySet(string name) =>
        new(RegistryHiveName.CurrentUser, RegistryPath, name, RegistryOperation.Set, RegistryValue.From(RegistryValueType.Dword, 0));

    private static TweakDefinition Tweak(string id, params TweakAction[] actions) =>
        new(id, "Ajuste", "Descrição", "Teste", TweakLevel.Safe, "Baixo", false, new([], []), actions);

    private static string Render(RegistryState state) => $"{state.KeyExists}/{state.Value?.Kind}/{state.Value?.Value.GetRawText()}";

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private sealed class FakeWindows : IRegistry, IServiceController, IPowerConfig
    {
        private readonly Dictionary<(RegistryHiveName Hive, string Path), Dictionary<string, RegistryValue>> _keys = [];
        public Dictionary<string, ServiceStartup> Services { get; } = [];
        public Guid Scheme { get; private set; } = Guid.NewGuid();
        public int Writes { get; private set; }
        public Action? BeforeWrite { get; set; }

        public RegistryState Read(RegistryAction action) => _keys.TryGetValue((action.Hive, action.Path), out var key)
            ? new RegistryState(true, key.GetValueOrDefault(action.ValueName))
            : new RegistryState(false, null);

        public void Seed(RegistryAction action, RegistryValue value) => SetValue(action, new RegistryState(true, value));

        public void Write(RegistryAction action, RegistryState state)
        {
            OnWrite();
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

        public ServiceStartup GetStartup(string name) => Services[name];

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
