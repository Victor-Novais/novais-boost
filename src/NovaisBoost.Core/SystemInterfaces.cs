using System.Text.Json.Serialization;

namespace NovaisBoost.Core;

public interface IRegistry
{
    RegistryState Read(RegistryAction action);
    void Write(RegistryAction action, RegistryState state);
}

public interface IServiceController
{
    ServiceStartup GetStartup(string name);
    void SetStartup(string name, ServiceStartup startup);
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
public abstract record SettingState
{
    internal virtual bool Matches(SettingState other) => Equals(other);
    internal abstract string Display { get; }
}

public sealed record NativeRegistryValue(uint Type, byte[] Data);

public sealed record RegistryState(bool KeyExists, RegistryValue? Value, NativeRegistryValue? Native = null) : SettingState
{
    internal override bool Matches(SettingState other) =>
        other is RegistryState registry && KeyExists == registry.KeyExists &&
        (registry.Native is null || Native is not null &&
            Native.Type == registry.Native.Type && Native.Data.AsSpan().SequenceEqual(registry.Native.Data)) &&
        (Value is null ? registry.Value is null : Value.Matches(registry.Value));

    internal override string Display => Value is not null ? $"{Value.Kind} {Value.Value.GetRawText()}"
        : KeyExists ? "valor ausente" : "chave ausente";
}

public sealed record ServiceState(ServiceStartup Startup) : SettingState
{
    internal override string Display => Startup switch
    {
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
