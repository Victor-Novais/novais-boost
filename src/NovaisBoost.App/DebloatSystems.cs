using System.Security.Principal;
using Microsoft.Win32;
using NovaisBoost.Core;
using Windows.Management.Deployment;

namespace NovaisBoost.App;

/// <summary>18.1: Store apps of the current user through PackageManager; reinstall through winget (msstore source).</summary>
internal sealed class WindowsAppxPackages(IActionLog log) : IAppxPackages
{
    private readonly PackageManager _manager = new();
    private Dictionary<string, global::Windows.ApplicationModel.Package>? _installed;

    public bool IsInstalled(string name) => Find(name) is not null;

    public void Remove(string name)
    {
        var package = Find(name) ?? throw new InvalidOperationException($"O app {name} não está instalado.");
        if (package.IsFramework || package.SignatureKind == global::Windows.ApplicationModel.PackageSignatureKind.System)
            throw new InvalidOperationException($"{name} é um componente do Windows e não pode ser removido.");
        // Current user only: no RemovalOptions.RemoveForAllUsers, provisioned packages stay.
        var result = _manager.RemovePackageAsync(package.Id.FullName).AsTask().GetAwaiter().GetResult();
        if (result.ExtendedErrorCode is { } error)
            throw new InvalidOperationException($"O Windows recusou remover {name}: {result.ErrorText} (0x{error.HResult:X8}).");
        _installed = null;
        log.Info($"App da Store removido para a conta atual: {package.Id.FullName}.");
    }

    public void Install(string name, string storeId)
    {
        var winget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
        if (!File.Exists(winget))
            throw new InvalidOperationException(
                $"O winget (Instalador de Aplicativo) não foi encontrado para reinstalar {name}. Instale-o pela Microsoft Store e use Reverter tudo de novo; o backup continua guardado.");
        try
        {
            WindowsCommand.Run(TimeSpan.FromMinutes(10), winget, "install", "--id", storeId, "--source", "msstore", "--exact",
                "--accept-package-agreements", "--accept-source-agreements", "--silent", "--disable-interactivity");
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or TimeoutException)
        {
            throw new InvalidOperationException(
                $"Não foi possível reinstalar {name} pela Microsoft Store ({error.Message}). Confira a internet e use Reverter tudo de novo; o backup continua guardado.", error);
        }
        _installed = null;
        if (Find(name) is null)
            throw new InvalidOperationException($"A Microsoft Store não confirmou a reinstalação de {name}. Use Reverter tudo de novo; o backup continua guardado.");
        log.Info($"App da Store reinstalado: {name} ({storeId}).");
    }

    /// <summary>The package list is read once and kept until something is removed or installed (it is slow to enumerate).</summary>
    private global::Windows.ApplicationModel.Package? Find(string name)
    {
        if (_installed is null)
        {
            using var identity = WindowsIdentity.GetCurrent();
            _installed = _manager.FindPackagesForUser(identity.User?.Value ?? string.Empty)
                .GroupBy(package => package.Id.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }
        return _installed.GetValueOrDefault(name);
    }
}

/// <summary>18.7 hibernation (powercfg) and 18.9 memory compression (MMAgent, needs the SysMain service running).</summary>
internal sealed class WindowsSystemFeatures : ISystemFeatures
{
    private const string PowerKey = @"SYSTEM\CurrentControlSet\Control\Power";

    public bool? IsEnabled(SystemFeature feature) => feature switch
    {
        SystemFeature.Hibernation => ReadHibernation(),
        SystemFeature.MemoryCompression => ReadMemoryCompression(),
        _ => null
    };

    public void Set(SystemFeature feature, bool enabled)
    {
        switch (feature)
        {
            case SystemFeature.Hibernation:
                WindowsCommand.Run("powercfg.exe", "/hibernate", enabled ? "on" : "off");
                break;
            case SystemFeature.MemoryCompression:
                try
                {
                    PowerShell($"{(enabled ? "Enable" : "Disable")}-MMAgent -MemoryCompression -ErrorAction Stop");
                }
                catch (System.ComponentModel.Win32Exception error)
                {
                    throw new InvalidOperationException("Não foi possível mudar a compressão de memória. Ela exige o serviço SysMain ligado " +
                        "(reverta o ajuste \"sysmain-off\" se ele estiver aplicado).", error);
                }
                break;
            default:
                throw new InvalidDataException($"Recurso desconhecido: {feature}.");
        }
    }

    /// <summary>The hiberfil.sys size, shown at review; null when it does not exist.</summary>
    public static long? HiberfilBytes()
    {
        try
        {
            var file = new FileInfo(Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", "hiberfil.sys"));
            return file.Exists ? file.Length : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool? ReadHibernation()
    {
        using var key = Registry.LocalMachine.OpenSubKey(PowerKey);
        return (key?.GetValue("HibernateEnabled") ?? key?.GetValue("HibernateEnabledDefault")) is int value ? value != 0 : null;
    }

    private static bool? ReadMemoryCompression()
    {
        try
        {
            return PowerShell("(Get-MMAgent -ErrorAction Stop).MemoryCompression").Trim() switch
            {
                "True" => true,
                "False" => false,
                _ => null
            };
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or TimeoutException)
        {
            return null;
        }
    }

    private static string PowerShell(string command) =>
        WindowsCommand.Run(TimeSpan.FromSeconds(30), Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command);
}

/// <summary>F18 tweaks that depend on what is installed on this PC.</summary>
internal static class DebloatTweaks
{
    public static IEnumerable<TweakDefinition> Generate(IActionLog log, IAppxPackages appx, IServiceController services)
    {
        var tweaks = new List<TweakDefinition>();
        void Add(string name, Func<IEnumerable<TweakDefinition?>> build)
        {
            try
            {
                tweaks.AddRange(build().OfType<TweakDefinition>());
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                log.Warning($"Ajuste de debloat '{name}' indisponível: {error.Message}");
            }
        }

        Add("appx", () => DataCatalog.AppxBloat().Where(entry => appx.IsInstalled(entry.Name)).Select(Debloat.AppxTweak));
        Add("oem", () => [Debloat.OemTweak(DataCatalog.OemBloat(), name => services.GetStartup(name) is not null)]);
        Add("startup", () =>
        {
            using var task = Registry.CurrentUser.OpenSubKey(Debloat.TeamsStartupTaskKey);
            var entries = StartupApps.List().Where(entry => entry.Enabled)
                .Select(entry => new StartupApproval(entry.Hive, entry.ApprovedKey, entry.Name, entry.Command));
            return [Debloat.StartupTweak(entries, task is not null)];
        });
        Add("hibernation", () => [Debloat.HibernationTweak(WindowsSystemFeatures.HiberfilBytes())]);
        return tweaks;
    }
}
