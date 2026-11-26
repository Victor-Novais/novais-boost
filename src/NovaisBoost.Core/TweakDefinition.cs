using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovaisBoost.Core;

public enum TweakLevel { Safe, Competitive, Extreme }
public enum RegistryHiveName { CurrentUser, LocalMachine }
public enum RegistryOperation { Set, Delete }
public enum RegistryValueType { String, ExpandString, Dword, Qword, MultiString, Binary, None }
public enum ServiceStartup { Automatic, AutomaticDelayed, Manual, Disabled }

public sealed record TweakCompatibility(string[] Hardware, string[] AntiCheat);

public sealed record TweakDefinition(
    string Id, string Name, string Description, string Category, TweakLevel Level,
    string Risk, bool RequiresReboot, TweakCompatibility Compat, TweakAction[] Actions);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RegistryAction), "registry")]
[JsonDerivedType(typeof(ServiceAction), "service")]
[JsonDerivedType(typeof(PowerAction), "powercfg")]
public abstract record TweakAction
{
    internal abstract string Target { get; }
    internal virtual string Identity => Target;
}

public sealed record RegistryAction(
    RegistryHiveName Hive, string Path, string ValueName, RegistryOperation Operation,
    RegistryValue? Expected) : TweakAction
{
    internal override string Target => $@"{Key} [{(ValueName.Length == 0 ? "(Padrão)" : ValueName)}]";
    internal override string Identity => $"{Key}\0{ValueName}";
    internal string Key => $@"{(Hive == RegistryHiveName.CurrentUser ? "HKCU" : "HKLM")}\{Path}";
}

public sealed record ServiceAction(string Name, ServiceStartup Expected) : TweakAction
{
    internal override string Target => $"Serviço {Name}";
}

public sealed record PowerAction(Guid Expected) : TweakAction
{
    internal override string Target => "Plano de energia ativo";
}

public sealed record RegistryValue(RegistryValueType Kind, JsonElement Value)
{
    public static RegistryValue From<T>(RegistryValueType kind, T value) =>
        new(kind, JsonSerializer.SerializeToElement(value));

    public bool Matches(RegistryValue? other) =>
        other is not null && Kind == other.Kind && JsonElement.DeepEquals(Value, other.Value);

    internal void Validate()
    {
        var valid = Kind switch
        {
            RegistryValueType.String or RegistryValueType.ExpandString =>
                Value.ValueKind == JsonValueKind.String && !Value.GetString()!.Contains('\0'),
            RegistryValueType.Dword => Value.ValueKind == JsonValueKind.Number && Value.TryGetInt32(out _),
            RegistryValueType.Qword => Value.ValueKind == JsonValueKind.Number && Value.TryGetInt64(out _),
            RegistryValueType.MultiString => Value.ValueKind == JsonValueKind.Array &&
                Value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrEmpty(item.GetString()) && !item.GetString()!.Contains('\0')),
            RegistryValueType.Binary or RegistryValueType.None => Value.ValueKind == JsonValueKind.Array &&
                Value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.Number && item.TryGetByte(out _)),
            _ => false
        };
        if (!valid)
            throw new InvalidDataException($"Valor de registro inválido para o tipo {Kind}.");
    }
}
