namespace NovaisBoost.Core;

/// <summary>A Windows service that must not be disabled, with its default startup type (data/critical-services.json).</summary>
/// <param name="MinBuild">First Windows build where this default applies.</param>
public sealed record CriticalService(string Name, string Label, ServiceStartup Startup, int MinBuild);

/// <summary>
/// Repair mode (F14): undoes changes that "FPS packs" and aggressive optimizers leave behind, back to the Windows default.
/// Repairs go through the same engine (simulation, restore point, backup, revert) as every tweak; they only act on
/// damaged values and are never part of the optimization levels.
/// </summary>
public static class Repairs
{
    public const string Category = "Reparo";

    /// <summary>Catalog repairs plus one repair per critical service known for this Windows build.</summary>
    public static IReadOnlyList<TweakDefinition> All(string? osBuild) =>
        [.. TweakCatalog.LoadRepairs(), .. CriticalServices(DataCatalog.CriticalServices(), osBuild)];

    /// <summary>One repair per service, so a service Windows refuses to change does not block the others.</summary>
    public static IReadOnlyList<TweakDefinition> CriticalServices(IEnumerable<CriticalService> services, string? osBuild)
    {
        var build = int.TryParse(osBuild, out var number) ? number : int.MaxValue;
        var enabled = Enum.GetValues<ServiceStartup>().Where(startup => startup != ServiceStartup.Disabled).ToArray();
        return services.Where(service => build >= service.MinBuild).Select(service => new TweakDefinition(
                $"repair-service-{service.Name.ToLowerInvariant()}", $"Serviço essencial desativado: {service.Label}",
                $"O serviço {service.Name} está desativado. Ele é necessário para o Windows funcionar direito " +
                "(atualizações, áudio, rede, WMI ou agendador, conforme o serviço). Volta ao tipo de início padrão. " +
                "Origem comum: \"debloaters\" e listas de serviços para desligar.",
                Category, TweakLevel.Safe, TweakEvidence.Documented, "Baixo. Volta ao padrão do Windows; reinicie o PC.", true,
                new TweakCompatibility([], []), [new ServiceAction(service.Name, service.Startup, enabled)]))
            .ToArray();
    }
}
