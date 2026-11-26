using System.Text.Json.Serialization;

namespace NovaisBoost.Core;

public interface IRegistry
{
    RegistryState Read(RegistryAction action);
    void Write(RegistryAction action, RegistryState state);
}

public interface IServiceController
{
    /// <summary>Returns null when the service is not installed.</summary>
    ServiceStartup? GetStartup(string name);
    void SetStartup(string name, ServiceStartup startup);
}

public interface IScheduledTasks
{
    TaskState Get(string path);
    void SetEnabled(string path, bool enabled);
}

public interface IPowerPlans
{
    PowerPlanState Read(PowerPlanAction plan);
    void Write(PowerPlanAction plan, PowerPlanState state);
}

public interface IPowerConfig
{
    Guid GetActiveScheme();
    void SetActiveScheme(Guid scheme);
}

public interface IRestorePointProvider
{
    RestorePoint Ensure();
}

public sealed record RestorePoint(long SequenceNumber, bool Reused);

public sealed class SystemProtectionDisabledException()
    : InvalidOperationException("A Proteção do Sistema está desativada. Ative-a na unidade do Windows e tente novamente.");

public interface IActionLog
{
    void Info(string message);
    void Warning(string message);
    void Error(string message);
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RegistryState), "registry")]
[JsonDerivedType(typeof(ServiceState), "service")]
[JsonDerivedType(typeof(PowerState), "powercfg")]
[JsonDerivedType(typeof(TaskState), "scheduledTask")]
[JsonDerivedType(typeof(PowerPlanState), "powerPlan")]
public abstract record SettingState
{
    internal virtual bool Matches(SettingState other) => Equals(other);
    internal abstract string Display { get; }
}

public sealed record NativeRegistryValue(uint Type, byte[] Data);

/// <param name="MissingSegments">When the key is absent: how many trailing path segments did not exist,
/// so a revert removes exactly the keys that were created.</param>
public sealed record RegistryState(bool KeyExists, RegistryValue? Value, NativeRegistryValue? Native = null, int MissingSegments = 0) : SettingState
{
    internal override bool Matches(SettingState other) =>
        other is RegistryState registry && KeyExists == registry.KeyExists &&
        (registry.Native is null || Native is not null &&
            Native.Type == registry.Native.Type && Native.Data.AsSpan().SequenceEqual(registry.Native.Data)) &&
        (Value is null ? registry.Value is null : Value.Matches(registry.Value));

    // "Key absent" and "value absent" read the same to the user, and a key created by an earlier
    // tweak in the same batch must not invalidate the reviewed preview of the next one.
    internal override string Display => Value is not null ? $"{Value.Kind} {Value.Value.GetRawText()}" : "não definido";
}

public sealed record ServiceState(ServiceStartup? Startup) : SettingState
{
    internal override string Display => Startup switch
    {
        null => "não instalado",
        ServiceStartup.Automatic => "automático",
        ServiceStartup.AutomaticDelayed => "automático (atrasado)",
        ServiceStartup.Manual => "manual",
        ServiceStartup.Disabled => "desativado",
        _ => throw new InvalidDataException("Tipo de inicialização inválido.")
    };
}

public sealed record PowerState(Guid Scheme) : SettingState
{
    internal override string Display => Scheme.ToString("D");
}

public sealed record TaskState(bool Exists, bool Enabled) : SettingState
{
    internal override string Display => !Exists ? "não existe" : Enabled ? "ativada" : "desativada";
}

public sealed record PowerPlanState(bool Exists, PowerSettingValue[]? Settings) : SettingState
{
    internal override bool Matches(SettingState other) =>
        other is PowerPlanState plan && Exists == plan.Exists && (!Exists || (Settings ?? []).SequenceEqual(plan.Settings ?? []));

    internal override string Display => !Exists ? "ausente" : $"presente ({Settings?.Length ?? 0} configurações)";
}
