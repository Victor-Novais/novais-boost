using NovaisBoost.Core;

namespace NovaisBoost.Tests;

/// <summary>F14: new action types (BCD, paging file, TCP), acceptance rules and the repair catalog, all on fakes.</summary>
public sealed partial class TweakEngineTests
{
    private static IReadOnlyList<TweakDefinition> AllRepairs => Repairs.All("26200");
    private static TweakDefinition Repair(string id) => AllRepairs.Single(repair => repair.Id == id);

    // ------------------------------------------------------------------ BCD

    [Fact]
    public void BcdElementIsRemovedAndRestoredOnRevert()
    {
        _windows.Bcd["useplatformclock"] = "yes";
        _windows.Bcd["tscsyncpolicy"] = "legacy";
        var repair = Repair("repair-platform-clock");

        var changes = Engine.Detect(repair);
        Assert.Equal(2, changes.Count);
        Assert.Contains(changes, change => change.Target.Contains("useplatformclock") && change.Current == "yes");
        Engine.Apply(repair, dryRun: false);
        Assert.Empty(_windows.Bcd);
        Assert.Empty(Engine.Detect(repair));

        Engine.RevertAll(dryRun: false);
        Assert.Equal("yes", _windows.Bcd["useplatformclock"]);
        Assert.Equal("legacy", _windows.Bcd["tscsyncpolicy"]);
    }

    [Fact]
    public void CleanBootConfigurationNeedsNoRepair() => Assert.Empty(Engine.Detect(Repair("repair-platform-clock")));

    // ------------------------------------------------------------------ paging file

    [Fact]
    public void DisabledPagefileIsTurnedOnAndRevertTurnsItOffAgain()
    {
        _windows.Pagefile = new PagefileState(false, []);
        var repair = Repair("repair-pagefile");
        Assert.Single(Engine.Detect(repair));

        Engine.Apply(repair, dryRun: false);
        Assert.True(_windows.Pagefile.SystemManaged);
        Engine.RevertAll(dryRun: false);
        Assert.Equal((false, 0), (_windows.Pagefile.SystemManaged, _windows.Pagefile.Files.Length));
    }

    [Fact]
    public void ManualPagefileChosenByTheUserIsKept()
    {
        _windows.Pagefile = new PagefileState(false, [new PagefileSetting(@"C:\pagefile.sys", 16384, 16384)]);
        Assert.Empty(Engine.Detect(Repair("repair-pagefile")));
        _windows.Pagefile = new PagefileState(true, []);
        Assert.Empty(Engine.Detect(Repair("repair-pagefile")));
    }

    // ------------------------------------------------------------------ TCP

    [Fact]
    public void TcpStackIsRestoredAndRevertedToTheChangedValues()
    {
        _windows.Tcp["autotuninglevel"] = "disabled";
        _windows.Tcp["rss"] = "disabled";
        var repair = Repair("repair-tcp-defaults");

        Assert.Equal(2, Engine.Apply(repair, dryRun: false).Count);
        Assert.Equal(("normal", "enabled"), (_windows.Tcp["autotuninglevel"], _windows.Tcp["rss"]));
        Engine.RevertAll(dryRun: false);
        Assert.Equal(("disabled", "disabled"), (_windows.Tcp["autotuninglevel"], _windows.Tcp["rss"]));
    }

    // ------------------------------------------------------------------ failures (P2.0 item 2.4)

    [Fact]
    public void FailingRepairInABatchIsUndoneAndTheOthersContinue()
    {
        _windows.Bcd["useplatformclock"] = "yes";
        _windows.Pagefile = new PagefileState(false, []);
        _windows.Tcp["rss"] = "disabled";
        TweakDefinition[] batch = [Repair("repair-platform-clock"), Repair("repair-pagefile"), Repair("repair-tcp-defaults")];
        var engine = Engine;
        var reviewed = batch.ToDictionary(repair => repair.Id, engine.Detect);
        _windows.BeforeWrite = () =>
        {
            if (_windows.Writes == 1)
            {
                _windows.BeforeWrite = null;
                throw new IOException("WMI recusou a gravação.");
            }
        };

        var results = engine.ApplyBatch(batch, reviewed);

        Assert.Equal([null, "WMI recusou a gravação.", null], results.Select(result => result.Error));
        Assert.Empty(_windows.Bcd);
        Assert.Equal((false, 0), (_windows.Pagefile.SystemManaged, _windows.Pagefile.Files.Length));
        Assert.Equal("enabled", _windows.Tcp["rss"]);
        Assert.Equal(["repair-platform-clock", "repair-tcp-defaults"], Snapshots.GetActive()!.TweakIds);
    }

    [Fact]
    public void RevertInterruptedByARebootCanBeFinishedLater()
    {
        _windows.Bcd["disabledynamictick"] = "yes";
        _windows.Tcp["autotuninglevel"] = "disabled";
        Engine.Apply(Repair("repair-platform-clock"), dryRun: false);
        Engine.Apply(Repair("repair-tcp-defaults"), dryRun: false);

        // The PC goes down in the middle of "Reverter tudo": the first restored value is written, the second is not.
        var writes = _windows.Writes;
        _windows.BeforeWrite = () =>
        {
            if (_windows.Writes == writes + 1)
                throw new IOException("Desligado no meio da reversão.");
        };
        Assert.Throws<IOException>(() => Engine.RevertAll(dryRun: false));
        Assert.True(Snapshots.GetActive()!.Pending);

        // Next start: a new engine reads the same backup and finishes.
        _windows.BeforeWrite = null;
        Engine.RevertAll(dryRun: false);
        Assert.Equal("yes", _windows.Bcd["disabledynamictick"]);
        Assert.Equal("disabled", _windows.Tcp["autotuninglevel"]);
        Assert.Null(Snapshots.GetActive());
    }

    // ------------------------------------------------------------------ acceptance rules

    [Theory]
    [InlineData(2, false)]
    [InlineData(38, false)]
    [InlineData(63, false)]
    [InlineData(255, true)]
    [InlineData(-1, true)]
    public void PrioritySeparationIsRepairedOnlyOutOfRange(int value, bool repaired)
    {
        var action = Repair("repair-latency-values").Actions.Cast<RegistryAction>().Single(item => item.ValueName == "Win32PrioritySeparation");
        _windows.Seed(action, RegistryValue.From(RegistryValueType.Dword, value));
        Assert.Equal(repaired, Engine.Detect(Repair("repair-latency-values")).Any(change => change.Target.Contains("Win32PrioritySeparation")));
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(8, false)]
    [InlineData(72, false)]
    public void MitigationOverrideIsRemovedOnlyWhenItDisablesProtections(int value, bool repaired)
    {
        var repair = Repair("repair-cpu-mitigations");
        _windows.Seed((RegistryAction)repair.Actions[0], RegistryValue.From(RegistryValueType.Dword, value));
        Assert.Equal(repaired, Engine.Detect(repair).Count > 0);
    }

    [Fact]
    public void PolicyValueSetToZeroIsAlreadyFineAndOneIsRemoved()
    {
        var repair = Repair("repair-windows-update-policies");
        var noAutoUpdate = repair.Actions.Cast<RegistryAction>().Single(action => action.ValueName == "NoAutoUpdate");
        _windows.Seed(noAutoUpdate, RegistryValue.From(RegistryValueType.Dword, 0));
        Assert.Empty(Engine.Detect(repair));
        _windows.Seed(noAutoUpdate, RegistryValue.From(RegistryValueType.Dword, 1));
        Engine.Apply(repair, dryRun: false);
        Assert.Null(_windows.Read(noAutoUpdate).Value);
    }

    [Theory]
    [InlineData(ServiceStartup.Disabled, true)]
    [InlineData(ServiceStartup.Manual, false)]
    [InlineData(ServiceStartup.Automatic, false)]
    public void CriticalServiceIsRepairedOnlyWhenDisabled(ServiceStartup current, bool repaired)
    {
        _windows.Services["Audiosrv"] = current;
        var repair = Repair("repair-service-audiosrv");
        Assert.Equal(repaired, Engine.Detect(repair).Count > 0);
        if (!repaired)
            return;
        Engine.Apply(repair, dryRun: false);
        Assert.Equal(ServiceStartup.Automatic, _windows.Services["Audiosrv"]);
        Engine.RevertAll(dryRun: false);
        Assert.Equal(ServiceStartup.Disabled, _windows.Services["Audiosrv"]);
    }

    // ------------------------------------------------------------------ the repair catalog

    [Fact]
    public void RepairCatalogLoadsAndStaysOutOfTheOptimization()
    {
        var repairs = AllRepairs;
        Assert.Equal(7 + DataCatalog.CriticalServices().Count, repairs.Count);
        Assert.All(repairs, repair =>
        {
            Assert.Equal(Repairs.Category, repair.Category);
            Assert.StartsWith("repair-", repair.Id);
            Assert.Equal(TweakEvidence.Documented, repair.Evidence);
        });
        var catalog = TweakCatalog.LoadEmbedded();
        Assert.DoesNotContain(catalog, tweak => tweak.Category == Repairs.Category || tweak.Id.StartsWith("repair-", StringComparison.Ordinal));
        Assert.Empty(repairs.Select(repair => repair.Id).Intersect(catalog.Select(tweak => tweak.Id)));
    }

    [Fact]
    public void RepairsNeverUndoANovaisBoostTweak()
    {
        // Where a repair and a catalog tweak touch the same value, the value the tweak writes must be accepted by the repair.
        var catalog = TweakCatalog.LoadEmbedded().SelectMany(tweak => tweak.Actions.OfType<RegistryAction>()).ToArray();
        foreach (var repairAction in AllRepairs.SelectMany(repair => repair.Actions.OfType<RegistryAction>()))
            foreach (var ours in catalog.Where(action => action.Hive == repairAction.Hive &&
                         string.Equals(action.Path, repairAction.Path, StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(action.ValueName, repairAction.ValueName, StringComparison.OrdinalIgnoreCase)))
                Assert.True(ours.Expected is not null && repairAction.Accepts(ours.Expected), $"{repairAction.ValueName} desfaria um ajuste do catálogo.");

        var servicesWeChange = TweakCatalog.LoadEmbedded().SelectMany(tweak => tweak.Actions.OfType<ServiceAction>()).Select(action => action.Name);
        Assert.Empty(DataCatalog.CriticalServices().Select(service => service.Name).Intersect(servicesWeChange, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void CriticalServicesFollowTheWindowsBuild()
    {
        Assert.Empty(Repairs.CriticalServices(DataCatalog.CriticalServices(), "19045"));
        Assert.Equal(DataCatalog.CriticalServices().Count, Repairs.CriticalServices(DataCatalog.CriticalServices(), "22631").Count);
    }

    [Fact]
    public void HealthyWindowsHasNothingToRepair()
    {
        // Fake defaults: no BCD overrides, system-managed paging file, TCP at defaults; Windows defaults in the registry.
        foreach (var service in DataCatalog.CriticalServices())
            _windows.Services[service.Name] = service.Startup;
        foreach (var action in Repair("repair-latency-values").Actions.Cast<RegistryAction>().Where(action => action.Expected is not null))
            _windows.Seed(action, action.Expected!);
        Assert.All(AllRepairs, repair => Assert.Empty(Engine.Detect(repair)));
    }

    [Fact]
    public void VmDamageScriptRefusesToRunOutsideAVm()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "NovaisBoost.slnx")))
            root = Path.GetDirectoryName(root) ?? throw new DirectoryNotFoundException("Raiz do repositório não encontrada.");
        var script = File.ReadAllText(Path.Combine(root, "scripts", "vm-damage.ps1"));
        var guard = script.IndexOf("if (-not $isVm)", StringComparison.Ordinal);
        var confirm = script.IndexOf("if (-not $IUnderstandThisIsAVm)", StringComparison.Ordinal);
        var firstChange = script.IndexOf("bcdedit /set", StringComparison.Ordinal);
        Assert.True(guard > 0 && confirm > 0 && guard < firstChange && confirm < firstChange, "As travas precisam vir antes de qualquer alteração.");
    }

    [Fact]
    public void SchemaRejectsInvalidBcdAndTcpActions()
    {
        const string template = """
            {
              "id": "repair-x", "name": "Reparo", "description": "Descrição", "category": "Reparo", "level": "safe",
              "evidence": "documented", "risk": "Baixo", "requiresReboot": false, "compat": { "hardware": [], "antiCheat": [] },
              "actions": [ ACTION ]
            }
            """;
        Assert.Throws<InvalidDataException>(() => TweakCatalog.Parse(template.Replace("ACTION", """{ "type": "bcd", "element": "use platform; del", "expected": null }"""), "bcd.json"));
        Assert.Throws<InvalidDataException>(() => TweakCatalog.Parse(template.Replace("ACTION", """{ "type": "tcpGlobal", "setting": "chimney", "expected": "enabled" }"""), "tcp.json"));
        var valid = TweakCatalog.Parse(template.Replace("ACTION", """{ "type": "bcd", "element": "useplatformclock", "expected": null }"""), "ok.json");
        Assert.IsType<BcdAction>(Assert.Single(valid.Actions));
    }
}
