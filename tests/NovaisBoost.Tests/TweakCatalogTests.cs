using System.Text.Json.Nodes;
using NovaisBoost.Core;

namespace NovaisBoost.Tests;

public sealed class TweakCatalogTests
{
    private const string Valid = """
        {
          "id": "test-settings", "name": "Ajuste", "description": "Descrição do ajuste",
          "category": "Sistema", "level": "safe", "evidence": "documented", "risk": "Baixo", "requiresReboot": false,
          "compat": { "hardware": [], "antiCheat": [] },
          "actions": [
            { "path": "Software\\Example", "type": "registry", "hive": "currentUser",
              "valueName": "", "operation": "delete", "expected": null },
            { "type": "service", "name": "Example", "expected": "automaticDelayed" },
            { "type": "powercfg", "expected": "381b4222-f694-41f0-9685-ff5bb260df2e" }
          ]
        }
        """;

    [Fact]
    public void EmbeddedCatalogLoadsBothUniversalLevels()
    {
        var tweaks = TweakCatalog.LoadEmbedded();
        Assert.Equal(34, tweaks.Count);
        Assert.Equal(19, tweaks.Count(tweak => tweak.Level == TweakLevel.Safe));
        Assert.Equal(12, tweaks.Count(tweak => tweak.Level == TweakLevel.Competitive));
        Assert.Equal(3, tweaks.Count(tweak => tweak.Level == TweakLevel.Extreme));
        Assert.All(tweaks.Where(tweak => tweak.Actions.Any(action => action is NvidiaSettingAction)),
            tweak => Assert.Contains("gpu-nvidia", tweak.Compat.Hardware));
        Assert.Contains(tweaks, tweak => tweak.Actions.OfType<PowerPlanAction>().Any());
        Assert.Contains(tweaks, tweak => tweak.Actions.OfType<ScheduledTaskAction>().Any());
    }

    [Fact]
    public void LevelsOnlyContainProvenTweaks()
    {
        var tweaks = TweakCatalog.LoadEmbedded();
        foreach (var level in Enum.GetValues<TweakLevel>())
        {
            var selected = TweakLevels.Select(tweaks, level);
            Assert.NotEmpty(selected);
            Assert.All(selected, tweak => Assert.Contains(tweak.Evidence, new[] { TweakEvidence.Measured, TweakEvidence.Documented }));
            Assert.All(selected, tweak => Assert.True(tweak.Level <= level));
        }

        // Experimental tweaks exist, but only in the individual (custom) list.
        var experimental = tweaks.Where(tweak => tweak.Evidence == TweakEvidence.Experimental).Select(tweak => tweak.Id).ToArray();
        Assert.Equal(["mmcss-games-task", "priority-separation", "sysmain-off", "timer-resolution", "windows-suggestions"], experimental);
        Assert.DoesNotContain(TweakLevels.Select(tweaks, TweakLevel.Extreme), tweak => experimental.Contains(tweak.Id));

        // Hardware profiles recommend tweaks through the same review; they must not push unproven ones either.
        var byId = tweaks.ToDictionary(tweak => tweak.Id);
        Assert.All(DataCatalog.Profiles().SelectMany(profile => profile.Tweaks), id => Assert.True(byId[id].IsProven, id));
    }

    [Fact]
    public void PrioritySeparationIsExperimentalAndOutOfTheCompetitiveLevel()
    {
        var tweaks = TweakCatalog.LoadEmbedded();
        Assert.Equal(TweakEvidence.Experimental, tweaks.Single(tweak => tweak.Id == "priority-separation").Evidence);
        Assert.DoesNotContain(TweakLevels.Select(tweaks, TweakLevel.Competitive), tweak => tweak.Id == "priority-separation");
    }

    [Fact]
    public void MmcssIsSplitIntoDocumentedAndExperimentalTweaks()
    {
        var tweaks = TweakCatalog.LoadEmbedded().ToDictionary(tweak => tweak.Id);
        var responsiveness = tweaks["mmcss-responsiveness"];
        var gamesTask = tweaks["mmcss-games-task"];
        Assert.Equal(TweakEvidence.Documented, responsiveness.Evidence);
        Assert.Equal(TweakEvidence.Experimental, gamesTask.Evidence);
        Assert.Equal(["SystemResponsiveness", "NetworkThrottlingIndex"],
            responsiveness.Actions.Cast<RegistryAction>().Select(action => action.ValueName));
        Assert.All(responsiveness.Actions.Cast<RegistryAction>(), action => Assert.EndsWith(@"Multimedia\SystemProfile", action.Path));
        Assert.Equal(["GPU Priority", "Priority", "Scheduling Category", "SFIO Priority"],
            gamesTask.Actions.Cast<RegistryAction>().Select(action => action.ValueName));
        Assert.All(gamesTask.Actions.Cast<RegistryAction>(), action => Assert.EndsWith(@"Tasks\Games", action.Path));
    }

    [Fact]
    public void PowerPlanHasItsOwnBatteryValues()
    {
        var plan = TweakCatalog.LoadEmbedded().Single(tweak => tweak.Id == "power-plan").Actions.OfType<PowerPlanAction>().Single();
        PowerSettingValue Setting(string id) => plan.Settings.Single(setting => setting.Setting == Guid.Parse(id));
        var minimumState = Setting("893dee8e-2bef-41e0-89c6-b55d0929964c");
        var boostMode = Setting("be337238-0d82-4146-a960-4f3749d470c7");
        var usbSuspend = Setting("48e6b7a6-50f5-4782-a5d4-53bb8f07e226");

        // CPU items: battery (dc) values differ from plugged-in (ac) values, and ac keeps the F2 values.
        Assert.NotEqual(minimumState.Ac, minimumState.Dc);
        Assert.NotEqual(boostMode.Ac, boostMode.Dc);
        Assert.Equal((100u, 5u), (minimumState.Ac, minimumState.Dc));
        Assert.Equal((2u, 4u), (boostMode.Ac, boostMode.Dc)); // aggressive / efficient aggressive
        Assert.Equal((0u, 1u), (usbSuspend.Ac, usbSuspend.Dc)); // off / on
    }

    [Fact]
    public void VbsOffWarnsAboutVanguardInsteadOfPromisingCompatibility()
    {
        var vbs = TweakCatalog.LoadEmbedded().Single(tweak => tweak.Id == "vbs-off");
        Assert.Equal(["Vanguard"], vbs.Compat.AntiCheat);
        Assert.DoesNotContain("Não afeta", vbs.Risk, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HVCI", vbs.Risk);
        Assert.Contains("religue", vbs.Risk);
    }

    [Fact]
    public void SchemaRequiresAKnownEvidence()
    {
        var json = JsonNode.Parse(Valid)!.AsObject();
        json.Remove("evidence");
        Assert.Throws<InvalidDataException>(() => TweakCatalog.Parse(json.ToJsonString(), "no-evidence.json"));
        json["evidence"] = "placebo";
        Assert.Throws<InvalidDataException>(() => TweakCatalog.Parse(json.ToJsonString(), "bad-evidence.json"));
    }

    [Fact]
    public void SchemaRejectsUnknownAntiCheat()
    {
        var json = JsonNode.Parse(Valid)!.AsObject();
        json["compat"]!["antiCheat"] = new JsonArray("Vangard");
        Assert.Throws<InvalidDataException>(() => TweakCatalog.Parse(json.ToJsonString(), "anti-cheat.json"));
    }

    [Fact]
    public void EachSystemTargetBelongsToASingleTweak()
    {
        var shared = TweakCatalog.LoadEmbedded()
            .SelectMany(tweak => tweak.Actions.Where(action => action is not PowerAction).Select(action => (tweak.Id, Key: action switch
            {
                RegistryAction registry => $"{registry.Hive}|{registry.Path}|{registry.ValueName}".ToLowerInvariant(),
                ServiceAction service => "service|" + service.Name.ToLowerInvariant(),
                ScheduledTaskAction task => "task|" + task.Path.ToLowerInvariant(),
                PowerPlanAction plan => "plan|" + plan.Id,
                NvidiaSettingAction nvidia => "nvidia|" + nvidia.SettingId,
                _ => throw new InvalidOperationException()
            })))
            .GroupBy(item => item.Key).Where(group => group.Count() > 1).Select(group => group.Key);
        Assert.Empty(shared);
    }

    [Fact]
    public void PowerPlanIsNeverOfferedToDualCcdX3dOrLaptops()
    {
        var plan = TweakCatalog.LoadEmbedded().Single(tweak => tweak.Id == "power-plan");
        Assert.Contains("not-dual-ccd-x3d", plan.Compat.Hardware);
        Assert.Contains("desktop", plan.Compat.Hardware);
    }

    [Fact]
    public void SchemaRejectsUnknownHardwareRequirement()
    {
        var json = JsonNode.Parse(Valid)!.AsObject();
        json["compat"]!["hardware"] = new JsonArray("quantum-cpu");
        Assert.Throws<InvalidDataException>(() => TweakCatalog.Parse(json.ToJsonString(), "compat.json"));
    }

    [Fact]
    public void ParsesScheduledTaskAndPowerPlanActions()
    {
        var json = JsonNode.Parse(Valid)!.AsObject();
        json["actions"] = new JsonArray(
            new JsonObject { ["type"] = "scheduledTask", ["path"] = @"\Microsoft\Windows\Example", ["expected"] = "disabled" },
            new JsonObject
            {
                ["type"] = "powerPlan", ["id"] = "4e6f7661-6973-426f-6f73-740000000001", ["name"] = "Teste",
                ["template"] = "e9a42b02-d5df-448d-aa00-03f14749eb61",
                ["settings"] = new JsonArray(new JsonObject
                {
                    ["subgroup"] = "54533251-82be-4824-96c1-47b60b740d00", ["setting"] = "0cc5b647-c1df-4637-891a-dec35c318583",
                    ["ac"] = 100, ["dc"] = 100
                })
            });
        var tweak = TweakCatalog.Parse(json.ToJsonString(), "new-types.json");
        Assert.Equal(@"\Microsoft\Windows\Example", Assert.IsType<ScheduledTaskAction>(tweak.Actions[0]).Path);
        Assert.Equal(100u, Assert.IsType<PowerPlanAction>(tweak.Actions[1]).Settings[0].Ac);
    }

    [Fact]
    public void LoadsAllOperationTypesRegardlessOfPropertyOrder()
    {
        var tweak = TweakCatalog.Parse(Valid, "valid.json");
        Assert.Collection(tweak.Actions,
            action =>
            {
                var registry = Assert.IsType<RegistryAction>(action);
                Assert.Equal("", registry.ValueName);
                Assert.Null(registry.Expected);
            },
            action => Assert.Equal(ServiceStartup.AutomaticDelayed, Assert.IsType<ServiceAction>(action).Expected),
            action => Assert.NotEqual(Guid.Empty, Assert.IsType<PowerAction>(action).Expected));
    }

    [Fact]
    public void InvalidJsonNamesTheFileAndLine()
    {
        var error = Assert.Throws<InvalidDataException>(() => TweakCatalog.Parse("{\nInvalid", "broken.json"));
        Assert.Contains("broken.json", error.Message);
        Assert.Contains("linha 2", error.Message);
    }

    [Theory]
    [InlineData("level", "unknown")]
    [InlineData("description", " ")]
    public void InvalidFieldNamesTheField(string key, string value)
    {
        var json = JsonNode.Parse(Valid)!.AsObject();
        json[key] = value;
        var error = Assert.Throws<InvalidDataException>(() => TweakCatalog.Parse(json.ToJsonString(), "field.json"));
        Assert.Contains("field.json", error.Message);
        Assert.Contains("/" + key, error.Message);
    }

    [Theory]
    [InlineData("operation", "\"set\"")]
    [InlineData("path", "\"Software\"")]
    [InlineData("valueName", "null")]
    public void SchemaRejectsInvalidRegistryOperations(string key, string value)
    {
        var json = JsonNode.Parse(Valid)!.AsObject();
        json["actions"]![0]![key] = JsonNode.Parse(value);
        var error = Assert.Throws<InvalidDataException>(() => TweakCatalog.Parse(json.ToJsonString(), "action.json"));
        Assert.Contains("/actions/0", error.Message);
    }

    [Fact]
    public void DuplicateTargetIsRejected()
    {
        var json = JsonNode.Parse(Valid)!.AsObject();
        var actions = json["actions"]!.AsArray();
        actions.Add(actions[0]!.DeepClone());
        var error = Assert.Throws<InvalidDataException>(() => TweakCatalog.Parse(json.ToJsonString(), "duplicate.json"));
        Assert.Contains("mesmo alvo", error.Message);
    }
}
