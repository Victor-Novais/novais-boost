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

public interface INvidiaSettings
{
    NvidiaSettingState Read(uint settingId);
    void Write(uint settingId, NvidiaSettingState state);
}

public interface IDnsSettings
{
    DnsState Read(DnsAction action);
    void Write(DnsAction action, DnsState state);
}

/// <summary>Boot configuration of the running Windows ({current}). Values come back canonical ("yes"/"no" for booleans).</summary>
public interface IBcdStore
{
    BcdState Read(string element);
    void Write(string element, BcdState state);
}

public interface IPagefile
{
    PagefileState Read();
    void Write(PagefileState state);
}

/// <summary>Global TCP settings (netsh int tcp ... global).</summary>
public interface ITcpSettings
{
    TcpState Read(string setting);
    void Write(string setting, TcpState state);
}

/// <summary>Game configuration files. The real implementation adds file IO, the read-only check and the process check.</summary>
public interface IConfigFiles
{
    ConfigFileState Read(ConfigFileAction action);
    void Write(ConfigFileAction action, ConfigFileState state);

    /// <summary>True while the game that owns the file is running (it would overwrite the change when it closes).</summary>
    bool IsInUse(ConfigFileAction action);
}

/// <summary>Per-application NVIDIA driver profiles (DRS).</summary>
public interface INvidiaProfiles
{
    NvidiaProfileState Read(string exe, uint settingId);
    void Write(string exe, uint settingId, NvidiaProfileState state);
}

/// <summary>Radeon Software settings through ADLX. IsAvailable is false when the driver or the integration is missing.</summary>
public interface IAmdSettings
{
    bool IsAvailable { get; }
    AmdSettingState Read(string feature);
    void Write(string feature, AmdSettingState state);
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
[JsonDerivedType(typeof(NvidiaSettingState), "nvidiaSetting")]
[JsonDerivedType(typeof(DnsState), "dns")]
[JsonDerivedType(typeof(BcdState), "bcd")]
[JsonDerivedType(typeof(PagefileState), "pagefile")]
[JsonDerivedType(typeof(TcpState), "tcpGlobal")]
[JsonDerivedType(typeof(ConfigFileState), "configFile")]
[JsonDerivedType(typeof(NvidiaProfileState), "nvidiaProfile")]
[JsonDerivedType(typeof(AmdSettingState), "amdSetting")]
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

/// <param name="Defined">False when the global profile uses the driver default (no explicit value).</param>
public sealed record NvidiaSettingState(bool Defined, uint Value) : SettingState
{
    internal override bool Matches(SettingState other) =>
        other is NvidiaSettingState nvidia && Defined == nvidia.Defined && (!Defined || Value == nvidia.Value);

    internal override string Display => Defined ? $"0x{Value:X}" : "padrão do driver";
}

public sealed record DnsState(string[] Servers) : SettingState
{
    internal override bool Matches(SettingState other) => other is DnsState dns && Servers.SequenceEqual(dns.Servers);
    internal override string Display => Servers.Length == 0 ? "automático (DHCP)" : string.Join(", ", Servers);
}

/// <param name="Value">Null when the element is not set (Windows default).</param>
public sealed record BcdState(string? Value) : SettingState
{
    internal override bool Matches(SettingState other) =>
        other is BcdState bcd && string.Equals(Value, bcd.Value, StringComparison.OrdinalIgnoreCase);
    internal override string Display => Value ?? "padrão do Windows (não definido)";
}

/// <summary>One manual paging file (sizes in MB; 0 and 0 = size managed by the system on that drive).</summary>
public sealed record PagefileSetting(string Path, uint InitialMb, uint MaximumMb);

/// <param name="Files">Manual paging files. Ignored when SystemManaged is true.</param>
public sealed record PagefileState(bool SystemManaged, PagefileSetting[] Files) : SettingState
{
    internal override bool Matches(SettingState other) =>
        other is PagefileState pagefile && SystemManaged == pagefile.SystemManaged &&
        (SystemManaged || Files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(pagefile.Files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)));

    internal override string Display => SystemManaged ? "gerenciado pelo sistema"
        : Files.Length == 0 ? "desligado (sem arquivo de paginação)"
        : string.Join(", ", Files.Select(file => file.InitialMb == 0 && file.MaximumMb == 0 ? $"{file.Path} (tamanho do sistema)"
            : $"{file.Path} ({file.InitialMb}-{file.MaximumMb} MB)"));
}

/// <param name="Value">Current value of the key; null when the key is missing.</param>
/// <param name="Original">The whole file (base64) as it was read; kept in the backup so revert can restore it byte for byte.</param>
public sealed record ConfigFileState(bool FileExists, string? Value, string? Original = null) : SettingState
{
    internal override bool Matches(SettingState other) =>
        other is ConfigFileState config && FileExists == config.FileExists && string.Equals(Value, config.Value, StringComparison.Ordinal);
    internal override string Display => !FileExists ? "arquivo não encontrado" : Value ?? "chave ausente";
}

/// <param name="AppKnown">Some profile lists the executable (predefined by NVIDIA or created before).</param>
/// <param name="Defined">The setting is set in that profile (otherwise the profile/driver default applies).</param>
public sealed record NvidiaProfileState(bool AppKnown, bool Defined, uint Value) : SettingState
{
    internal override bool Matches(SettingState other) =>
        other is NvidiaProfileState state && AppKnown == state.AppKnown && Defined == state.Defined && (!Defined || Value == state.Value);
    internal override string Display => !AppKnown ? "sem perfil no driver" : Defined ? $"0x{Value:X}" : "padrão do perfil";
}

public sealed record AmdSettingState(bool Enabled) : SettingState
{
    internal override string Display => Enabled ? "ligado" : "desligado";
}

public sealed record TcpState(string Value) : SettingState
{
    internal override bool Matches(SettingState other) => other is TcpState tcp && string.Equals(Value, tcp.Value, StringComparison.OrdinalIgnoreCase);
    internal override string Display => Value;
}

public sealed record PowerPlanState(bool Exists, PowerSettingValue[]? Settings) : SettingState
{
    internal override bool Matches(SettingState other) =>
        other is PowerPlanState plan && Exists == plan.Exists && (!Exists || (Settings ?? []).SequenceEqual(plan.Settings ?? []));

    internal override string Display => !Exists ? "ausente" : $"presente ({Settings?.Length ?? 0} configurações)";
}
