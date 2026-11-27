using NovaisBoost.Core;

namespace NovaisBoost.Tests;

public sealed partial class TweakEngineTests
{
    private static readonly AppxBloatEntry Clipchamp = new("Clipchamp.Clipchamp", "Clipchamp", "9P1J8S7CCWWT", "Editor de vídeo.");
    private static readonly AppxBloatEntry News = new("Microsoft.BingNews", "Notícias", "9WZDNCRFHVFW", "Notícias.");

    [Fact]
    public void StoreAppIsRemovedAndReinstalledFromTheStoreOnRevert()
    {
        _windows.Packages.Add(Clipchamp.Name);
        var tweak = Debloat.AppxTweak(Clipchamp)!;

        Assert.Single(Engine.Detect(tweak));
        Engine.Apply(tweak, dryRun: false);
        Assert.DoesNotContain(Clipchamp.Name, _windows.Packages);
        Assert.Empty(Engine.Detect(tweak));

        Engine.RevertAll(dryRun: false);
        Assert.Contains(Clipchamp.Name, _windows.Packages);
        Assert.Equal(["9P1J8S7CCWWT"], _windows.Reinstalled);
        Assert.Null(Snapshots.GetActive());
    }

    [Fact]
    public void AppThatIsNotInstalledNeedsNothing()
    {
        var tweak = Debloat.AppxTweak(Clipchamp)!;
        Assert.Empty(Engine.Apply(tweak, dryRun: false));
        Assert.Equal(0, _windows.Writes);
    }

    [Fact]
    public void FailedReinstallKeepsTheBackupToTryAgain()
    {
        _windows.Packages.Add(Clipchamp.Name);
        Engine.Apply(Debloat.AppxTweak(Clipchamp)!, dryRun: false);

        _windows.InstallFailure = new InvalidOperationException("Sem internet: não foi possível reinstalar pela Microsoft Store.");
        var error = Assert.Throws<InvalidOperationException>(() => Engine.RevertAll(dryRun: false));
        Assert.Contains("Microsoft Store", error.Message);
        Assert.True(Snapshots.GetActive()!.Pending);

        _windows.InstallFailure = null;
        Engine.RevertAll(dryRun: false);
        Assert.Contains(Clipchamp.Name, _windows.Packages);
        Assert.Null(Snapshots.GetActive());
    }

    [Fact]
    public void FailureInTheMiddleOfAnAppBatchRestoresTheAppsAlreadyRemoved()
    {
        _windows.Packages.UnionWith([Clipchamp.Name, News.Name]);
        var tweak = Tweak("apps", new AppxPackageAction(Clipchamp.Name, Clipchamp.StoreId), new AppxPackageAction(News.Name, News.StoreId));
        var writes = _windows.Writes;
        var failed = false;
        _windows.BeforeWrite = () =>
        {
            if (!failed && _windows.Writes == writes + 1)
            {
                failed = true;
                throw new IOException("Falha ao remover o segundo app.");
            }
        };

        Assert.Throws<IOException>(() => Engine.Apply(tweak, dryRun: false));
        _windows.BeforeWrite = null;
        Assert.Contains(Clipchamp.Name, _windows.Packages);
        Assert.Contains(News.Name, _windows.Packages);
        Assert.Null(Snapshots.GetActive());
    }

    [Fact]
    public void AppRevertInterruptedByARebootCanBeFinishedLater()
    {
        _windows.Packages.UnionWith([Clipchamp.Name, News.Name]);
        Engine.Apply(Debloat.AppxTweak(Clipchamp)!, dryRun: false);
        Engine.Apply(Debloat.AppxTweak(News)!, dryRun: false);

        var writes = _windows.Writes;
        _windows.BeforeWrite = () =>
        {
            if (_windows.Writes == writes + 1)
                throw new IOException("Desligado no meio da reversão.");
        };
        Assert.Throws<IOException>(() => Engine.RevertAll(dryRun: false));
        Assert.True(Snapshots.GetActive()!.Pending);

        _windows.BeforeWrite = null;
        Engine.RevertAll(dryRun: false);
        Assert.Contains(Clipchamp.Name, _windows.Packages);
        Assert.Contains(News.Name, _windows.Packages);
    }

    [Fact]
    public void BloatListIsSafeAndEveryAppHasAStoreIdForTheRevert()
    {
        var entries = DataCatalog.AppxBloat();
        Assert.NotEmpty(entries);
        Assert.All(entries, entry =>
        {
            Assert.DoesNotContain(entry.Name, Debloat.NeverRemove);
            Assert.Matches("^[0-9A-Z]{12,14}$", entry.StoreId);
            Assert.DoesNotContain("Xbox", entry.Name, StringComparison.OrdinalIgnoreCase);
        });
        var tweaks = entries.Select(Debloat.AppxTweak).ToArray();
        Assert.All(tweaks, tweak => Assert.NotNull(tweak));
        Assert.Equal(tweaks.Length, tweaks.Select(tweak => tweak!.Id).Distinct().Count());
        Assert.All(tweaks, tweak => Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", tweak!.Id));
        Assert.Null(Debloat.AppxTweak(new AppxBloatEntry("Microsoft.WindowsStore", "Store", "9WZDNCRFJBMP", "Loja")));
        Assert.Null(Debloat.AppxTweak(new AppxBloatEntry("Microsoft.XboxGamingOverlay", "Game Bar", "9NZKPSTSNW4P", "Game Bar")));
    }

    [Fact]
    public void OemServicesGoToManualOnlyWhenInstalledAndNeverToDisabled()
    {
        var groups = DataCatalog.OemBloat();
        Assert.Null(Debloat.OemTweak(groups, _ => false));

        var tweak = Debloat.OemTweak(groups, name => name is "ASUSOptimization" or "CorsairGamingAudioConfig")!;
        Assert.Equal(TweakEvidence.Experimental, tweak.Evidence);
        Assert.Contains("RGB", tweak.Description);
        var services = tweak.Actions.Cast<ServiceAction>().ToArray();
        Assert.Equal(["ASUSOptimization", "CorsairGamingAudioConfig"], services.Select(service => service.Name));
        Assert.All(services, service => Assert.Equal(ServiceStartup.Manual, service.Expected));

        // Lighting / main suite services are never in the list.
        Assert.DoesNotContain(groups.SelectMany(group => group.Services), name =>
            name is "LightingService" or "CorsairService" or "ArmouryCrateService" or "NahimicService" or "Mystic_Light_Service");

        _windows.Services["ASUSOptimization"] = ServiceStartup.Automatic;
        _windows.Services["CorsairGamingAudioConfig"] = ServiceStartup.Disabled;
        Engine.Apply(tweak, dryRun: false);
        Assert.Equal(ServiceStartup.Manual, _windows.Services["ASUSOptimization"]);
        Assert.Equal(ServiceStartup.Disabled, _windows.Services["CorsairGamingAudioConfig"]);
        Engine.RevertAll(dryRun: false);
        Assert.Equal(ServiceStartup.Automatic, _windows.Services["ASUSOptimization"]);
    }

    [Fact]
    public void OnlyOneDriveAndTeamsLeaveTheStartup()
    {
        StartupApproval Entry(string name, string command) =>
            new(RegistryHiveName.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", name, command);
        var entries = new[]
        {
            Entry("OneDrive", @"""C:\Users\a\AppData\Local\Microsoft\OneDrive\OneDrive.exe"" /background"),
            Entry("com.squirrel.Teams.Teams", @"C:\Users\a\AppData\Local\Microsoft\Teams\Update.exe --processStart ""Teams.exe"""),
            Entry("Discord", @"C:\Users\a\AppData\Local\Discord\Update.exe --processStart Discord.exe"),
            Entry("Steam", @"""C:\Program Files (x86)\Steam\steam.exe"" -silent")
        };

        Assert.Null(Debloat.StartupTweak(entries[2..], teamsStartupTask: false));
        var tweak = Debloat.StartupTweak(entries, teamsStartupTask: true)!;
        var names = tweak.Actions.Cast<RegistryAction>().Select(action => action.ValueName).ToArray();
        Assert.Equal(["OneDrive", "com.squirrel.Teams.Teams", "State"], names);
        Assert.Equal(TweakLevel.Safe, tweak.Level);

        var task = new RegistryAction(RegistryHiveName.CurrentUser, Debloat.TeamsStartupTaskKey, "State", RegistryOperation.Set, null);
        _windows.Seed(task, RegistryValue.From(RegistryValueType.Dword, 2));
        Engine.Apply(tweak, dryRun: false);
        Assert.Equal(1, _windows.Read(task).Value!.Value.GetInt32());
        Engine.RevertAll(dryRun: false);
        Assert.Equal(2, _windows.Read(task).Value!.Value.GetInt32());
    }

    [Fact]
    public void HibernationIsCustomOnlyDesktopOnlyAndRoundTrips()
    {
        _compatibility = _ => null;
        var tweak = Debloat.HibernationTweak(17_179_869_184);
        Assert.Contains("16 GB", tweak.Description);
        Assert.Equal(TweakEvidence.Experimental, tweak.Evidence);
        Assert.Contains("desktop", tweak.Compat.Hardware);
        Assert.Empty(TweakLevels.Select([tweak], TweakLevel.Extreme));

        _windows.Features[SystemFeature.Hibernation] = true;
        Engine.Apply(tweak, dryRun: false);
        Assert.False(_windows.Features[SystemFeature.Hibernation]);
        Engine.RevertAll(dryRun: false);
        Assert.True(_windows.Features[SystemFeature.Hibernation]);
    }

    [Fact]
    public void UnavailableFeatureHasNothingToChange()
    {
        _compatibility = _ => null;
        var tweak = Debloat.HibernationTweak(null);
        Assert.DoesNotContain("GB", tweak.Description);
        Assert.Empty(Engine.Apply(tweak, dryRun: false));
        Assert.Equal(0, _windows.Writes);
    }

    [Fact]
    public void MemoryCompressionTweakIsExperimentalAndRoundTrips()
    {
        var tweak = TweakCatalog.LoadEmbedded().Single(item => item.Id == "memory-compression-off");
        Assert.Equal(TweakEvidence.Experimental, tweak.Evidence);
        Assert.Contains("ram-32gb", tweak.Compat.Hardware);
        _compatibility = _ => null;

        _windows.Features[SystemFeature.MemoryCompression] = true;
        Engine.Apply(tweak, dryRun: false);
        Assert.False(_windows.Features[SystemFeature.MemoryCompression]);
        Engine.RevertAll(dryRun: false);
        Assert.True(_windows.Features[SystemFeature.MemoryCompression]);
    }

    [Fact]
    public void RepairsDoNotUndoTheFeatureUpdateDeferral()
    {
        var defer = TweakCatalog.LoadEmbedded().Single(item => item.Id == "defer-feature-updates");
        Assert.Contains("not-home", defer.Compat.Hardware);
        var values = defer.Actions.Cast<RegistryAction>().Select(action => action.ValueName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(Repairs.All("26200").SelectMany(repair => repair.Actions).OfType<RegistryAction>(),
            action => values.Contains(action.ValueName));
    }
}
