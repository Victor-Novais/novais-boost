using System.Text.RegularExpressions;

namespace NovaisBoost.Core;

/// <summary>A Store app that is safe to remove for the current user (data/bloat/appx.json).</summary>
/// <param name="StoreId">Microsoft Store id used by the revert (winget install --source msstore).</param>
public sealed record AppxBloatEntry(string Name, string Label, string StoreId, string Description);

/// <summary>Heavy services of a motherboard/peripheral vendor suite (data/bloat/oem.json). Lighting and fan services stay out.</summary>
public sealed record OemServiceGroup(string Vendor, string[] Services, string Warning);

/// <summary>A program in the Run keys or Startup folder, and where Task Manager keeps its "disabled" marker.</summary>
public sealed record StartupApproval(RegistryHiveName Hive, string ApprovedKey, string Name, string Command);

/// <summary>F18: builds the debloat tweaks from the bloat lists and what is present on this PC.</summary>
public static partial class Debloat
{
    public const string Category = "Debloat";
    private static readonly TweakCompatibility None = new([], []);

    /// <summary>Never removed, whatever a list says: the Store itself, Xbox/Game Bar (X3D), and apps people rely on.</summary>
    public static readonly IReadOnlySet<string> NeverRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.WindowsStore", "Microsoft.StorePurchaseApp", "Microsoft.DesktopAppInstaller", "Microsoft.XboxGamingOverlay",
        "Microsoft.XboxGameOverlay", "Microsoft.XboxIdentityProvider", "Microsoft.XboxSpeechToTextOverlay", "Microsoft.Xbox.TCUI",
        "Microsoft.GamingApp", "Microsoft.GamingServices", "Microsoft.WindowsCalculator", "Microsoft.Windows.Photos",
        "Microsoft.WindowsTerminal", "Microsoft.SecHealthUI", "Microsoft.HEIFImageExtension", "Microsoft.HEVCVideoExtension",
        "Microsoft.VP9VideoExtensions", "Microsoft.WebMediaExtensions", "Microsoft.WebpImageExtension", "Microsoft.AV1VideoExtension",
        "Microsoft.RawImageExtension", "Microsoft.VCLibs.140.00", "Microsoft.UI.Xaml.2.8", "Microsoft.NET.Native.Framework.2.2"
    };

    /// <summary>Package that keeps the new Teams' "start with Windows" switch (a StartupTask of the MSTeams package).</summary>
    public const string TeamsStartupTaskKey =
        @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\MSTeams_8wekyb3d8bbwe\TeamsTfwStartupTask";

    /// <summary>One tweak per app, so each can be chosen in the individual list. Null for a protected package.</summary>
    public static TweakDefinition? AppxTweak(AppxBloatEntry entry) => NeverRemove.Contains(entry.Name) ? null : new(
        $"appx-{Slug(entry.Name)}", $"Remover {entry.Label}",
        $"{entry.Description} Remove o app só da sua conta. Para voltar, Reverter reinstala pela Microsoft Store.",
        Category, TweakLevel.Competitive, TweakEvidence.Documented,
        "Baixo. O app some do menu Iniciar; a reinstalação no Reverter precisa de internet e da Microsoft Store.", false, None,
        [new AppxPackageAction(entry.Name, entry.StoreId)]);

    /// <summary>
    /// 18.4: vendor services present on this PC go to Manual (never Disabled): they start when the vendor app asks.
    /// A service already Manual or Disabled is left alone. Null when none is installed.
    /// </summary>
    public static TweakDefinition? OemTweak(IEnumerable<OemServiceGroup> groups, Func<string, bool> installed)
    {
        var present = groups.Select(group => group with { Services = group.Services.Where(installed).ToArray() })
            .Where(group => group.Services.Length > 0).ToArray();
        if (present.Length == 0)
            return null;
        return new("oem-services-manual", "Serviços de fabricantes sob demanda",
            $"Coloca em Manual os serviços de atualização, diagnóstico e telemetria de {string.Join(", ", present.Select(group => group.Vendor))}. " +
            string.Join(" ", present.Select(group => group.Warning)),
            Category, TweakLevel.Competitive, TweakEvidence.Experimental,
            "Médio. Se o RGB, as ventoinhas ou o app do fabricante pararem de funcionar, reverta este ajuste em Reverter / Backups.", true, None,
            present.SelectMany(group => group.Services).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(name => (TweakAction)new ServiceAction(name, ServiceStartup.Manual, [ServiceStartup.Manual, ServiceStartup.Disabled])).ToArray());
    }

    public static bool IsOneDriveOrTeams(StartupApproval entry) =>
        OneDriveOrTeams().IsMatch(entry.Name) || OneDriveOrTeams().IsMatch(Path(entry.Command));

    /// <summary>
    /// 18.3: OneDrive and Teams stop opening with Windows, the same way Task Manager does it. Nothing is uninstalled.
    /// </summary>
    /// <param name="teamsStartupTask">True when the new Teams' startup task exists for this user.</param>
    public static TweakDefinition? StartupTweak(IEnumerable<StartupApproval> entries, bool teamsStartupTask)
    {
        var actions = entries.Where(IsOneDriveOrTeams)
            .Select(entry => (TweakAction)new RegistryAction(entry.Hive, entry.ApprovedKey, entry.Name, RegistryOperation.Set,
                RegistryValue.From(RegistryValueType.Binary, new[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }))).ToList();
        // StartupTask State: 2 = enabled, 1 = disabled by the user, 0 = disabled.
        if (teamsStartupTask)
            actions.Add(new RegistryAction(RegistryHiveName.CurrentUser, TeamsStartupTaskKey, "State", RegistryOperation.Set,
                RegistryValue.From(RegistryValueType.Dword, 1), [RegistryValue.From(RegistryValueType.Dword, 0)]));
        if (actions.Count == 0)
            return null;
        return new("startup-onedrive-teams", "OneDrive e Teams fora da inicialização",
            "OneDrive e Teams deixam de abrir junto com o Windows (igual ao Gerenciador de Tarefas). Eles continuam instalados e " +
            "sincronizam normalmente quando você os abre.",
            Category, TweakLevel.Safe, TweakEvidence.Documented,
            "Nenhum. Abra o OneDrive pelo menu Iniciar quando quiser sincronizar.", false, None, actions.ToArray());
    }

    /// <summary>
    /// 18.7: hibernation off frees a file the size of a good part of the RAM. Custom list only (no FPS effect), never on laptops.
    /// </summary>
    public static TweakDefinition HibernationTweak(long? hiberfilBytes) => new("hibernation-off", "Desligar a hibernação",
        "Apaga o arquivo hiberfil.sys" + (hiberfilBytes is > 0 ? $" e libera {hiberfilBytes.Value / 1_073_741_824d:0.#} GB no disco do Windows" : "") +
        ". A Inicialização rápida também deixa de existir. Não muda o FPS: serve para liberar espaço.",
        "Armazenamento", TweakLevel.Extreme, TweakEvidence.Experimental,
        "Baixo em desktop. Hibernar deixa de aparecer no menu Ligar/Desligar.", false, new TweakCompatibility(["desktop"], []),
        [new SystemFeatureAction(SystemFeature.Hibernation, false)]);

    private static string Path(string command) => command.Trim().Trim('"');

    private static string Slug(string name) => SlugCleanup().Replace(name.ToLowerInvariant(), "-").Trim('-');

    [GeneratedRegex(@"onedrive|\bteams\b|ms-teams|msteams", RegexOptions.IgnoreCase)]
    private static partial Regex OneDriveOrTeams();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugCleanup();
}
