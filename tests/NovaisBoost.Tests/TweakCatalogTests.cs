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
    public void LoadsTheThreeEmbeddedExamples()
    {
        var tweaks = TweakCatalog.LoadEmbedded();
        Assert.Equal(new[] { "game-dvr-background", "mouse-acceleration", "visual-transparency" }, tweaks.Select(tweak => tweak.Id));
        Assert.All(tweaks, tweak => Assert.All(tweak.Actions, action => Assert.IsType<RegistryAction>(action)));
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
