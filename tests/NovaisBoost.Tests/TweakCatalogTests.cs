using System.Text.Json.Nodes;
using NovaisBoost.Core;

namespace NovaisBoost.Tests;

public sealed class TweakCatalogTests
{
    private const string Valid = """
        {
          "id": "test-settings", "name": "Ajuste", "description": "Descrição do ajuste",
          "category": "Sistema", "level": "safe", "risk": "Baixo", "requiresReboot": false,
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
        Assert.Equal(33, tweaks.Count);
        Assert.Equal(19, tweaks.Count(tweak => tweak.Level == TweakLevel.Safe));
        Assert.Equal(11, tweaks.Count(tweak => tweak.Level == TweakLevel.Competitive));
        Assert.Equal(3, tweaks.Count(tweak => tweak.Level == TweakLevel.Extreme));
        Assert.All(tweaks.Where(tweak => tweak.Actions.Any(action => action is NvidiaSettingAction)),
            tweak => Assert.Contains("gpu-nvidia", tweak.Compat.Hardware));
        Assert.Contains(tweaks, tweak => tweak.Actions.OfType<PowerPlanAction>().Any());
        Assert.Contains(tweaks, tweak => tweak.Actions.OfType<ScheduledTaskAction>().Any());
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
