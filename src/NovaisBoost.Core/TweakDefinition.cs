using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovaisBoost.Core;

public enum TweakLevel { Safe, Competitive, Extreme }
public enum RegistryHiveName { CurrentUser, LocalMachine }
public enum RegistryOperation { Set, Delete }
public enum RegistryValueType { String, ExpandString, Dword, Qword, MultiString, Binary, None }
public enum ServiceStartup { Automatic, AutomaticDelayed, Manual, Disabled }
public enum TaskSetting { Enabled, Disabled }

/// <summary>How the effect of a tweak is known.</summary>
/// <remarks>Measured: proven in a benchmark. Documented: described in official Microsoft or vendor documentation.
/// Experimental: neither; offered only in the individual (custom) list, never in a level.</remarks>
public enum TweakEvidence { Measured, Documented, Experimental }

/// <param name="Hardware">Requirement tokens checked by <see cref="Compatibility.Check"/>; a failed one blocks the tweak.</param>
/// <param name="AntiCheat">Anti-cheats that may refuse this tweak. Not a block: a warning at review when a game using one is installed.</param>
public sealed record TweakCompatibility(string[] Hardware, string[] AntiCheat);

public sealed record TweakDefinition(
    string Id, string Name, string Description, string Category, TweakLevel Level, TweakEvidence Evidence,
    string Risk, bool RequiresReboot, TweakCompatibility Compat, TweakAction[] Actions)
{
    /// <summary>Only proven tweaks (measured or documented) can be part of a level.</summary>
    [JsonIgnore]
    public bool IsProven => Evidence != TweakEvidence.Experimental;
}

public static class TweakLevels
{
    /// <summary>
    /// The tweaks a level offers: everything up to that level, except experimental ones (those stay in the custom list).
    /// With <paramref name="local"/>, a tweak measured as a real gain on this PC counts as proven, and one measured
    /// as worse on this PC is left out.
    /// </summary>
    public static IReadOnlyList<TweakDefinition> Select(IEnumerable<TweakDefinition> tweaks, TweakLevel level, LocalEvidence? local = null) =>
        tweaks.Where(tweak => tweak.Level <= level && IsProven(tweak, local) && local?.IsWorse(tweak.Id) != true).ToArray();

    public static bool IsProven(TweakDefinition tweak, LocalEvidence? local) =>
        (local?.Effective(tweak) ?? tweak.Evidence) != TweakEvidence.Experimental;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RegistryAction), "registry")]
[JsonDerivedType(typeof(ServiceAction), "service")]
[JsonDerivedType(typeof(PowerAction), "powercfg")]
[JsonDerivedType(typeof(ScheduledTaskAction), "scheduledTask")]
[JsonDerivedType(typeof(PowerPlanAction), "powerPlan")]
[JsonDerivedType(typeof(NvidiaSettingAction), "nvidiaSetting")]
[JsonDerivedType(typeof(DnsAction), "dns")]
public abstract record TweakAction
{
    internal abstract string Target { get; }
    internal virtual string Identity => Target;
}

/// <param name="Accept">Other values that already satisfy the tweak (e.g. a stricter setting made by the user).</param>
public sealed record RegistryAction(
    RegistryHiveName Hive, string Path, string ValueName, RegistryOperation Operation,
    RegistryValue? Expected, RegistryValue[]? Accept = null) : TweakAction
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

public sealed record ScheduledTaskAction(string Path, TaskSetting Expected) : TweakAction
{
    internal override string Target => $"Tarefa {Path}";
}

public sealed record PowerSettingValue(Guid Subgroup, Guid Setting, uint Ac, uint Dc);

/// <summary>A DWORD setting in the NVIDIA driver's global (base) profile.</summary>
public sealed record NvidiaSettingAction(uint SettingId, string Name, uint Expected) : TweakAction
{
    internal override string Target => $"NVIDIA: {Name}";
    internal override string Identity => $"nvidia:{SettingId:X8}";
}

/// <summary>IPv4 DNS servers of one network adapter. An empty list means "automatic (DHCP)".</summary>
public sealed record DnsAction(Guid InterfaceId, string Alias, string[] Servers) : TweakAction
{
    internal override string Target => $"DNS de {Alias}";
    internal override string Identity => $"dns:{InterfaceId:D}";
}

/// <summary>A power plan owned by NOVAIS boost: created from a template on apply, deleted on revert.</summary>
public sealed record PowerPlanAction(Guid Id, string Name, Guid Template, PowerSettingValue[] Settings) : TweakAction
{
    internal override string Target => $"Plano de energia {Name}";
    internal override string Identity => $"plan:{Id:D}";
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
