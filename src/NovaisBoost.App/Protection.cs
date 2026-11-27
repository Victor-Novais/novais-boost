using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Win32;
using NovaisBoost.Core;
using Spectre.Console;

namespace NovaisBoost.App;

/// <summary>20.4: arms the boot counter after changes that need a restart, and runs the two halves of the check.</summary>
internal static class BootProtection
{
    public static void Arm(AppServices app, string owner)
    {
        BootGuard.Save(app.DataDirectory, new BootGuardState(owner, DateTimeOffset.Now));
        TaskSchedulerService.Register(Schedules.BootGuard);
        TaskSchedulerService.Register(Schedules.BootConfirm);
        app.Log.Info("Proteção depois do reinício armada: se o Windows não chegar à área de trabalho 2 vezes seguidas, tudo é desfeito.");
    }

    /// <summary>Startup task (SYSTEM). Returns the exit code.</summary>
    public static int OnBoot(string dataDirectory, IActionLog log)
    {
        if (BootGuard.Load(dataDirectory) is not { } state)
            return 0;
        var (next, decision) = BootGuard.OnBoot(state);
        BootGuard.Save(dataDirectory, next);
        if (decision != BootDecision.Revert)
        {
            log.Info($"Proteção depois do reinício: boot {next.Attempts} de {BootGuard.MaxFailedBoots} sem confirmação.");
            return 0;
        }
        log.Warning("Proteção depois do reinício: o Windows não chegou à área de trabalho em 2 boots seguidos. Desfazendo tudo.");
        try
        {
            using var hive = UserHive.Mount(state.Owner, log);
            var app = AppServices.Create(dataDirectory, log, state.Owner, userSid: state.Owner);
            app.Engine.RevertAll(dryRun: false);
            log.Warning("Proteção depois do reinício: Reverter tudo concluído. Reinicie o PC.");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Whatever is left (apps from the Store, values of the user profile) is finished at the next logon.
            log.Error($"Proteção depois do reinício: a reversão ficou pendente para o próximo logon: {error.Message}");
        }
        return 0;
    }

    /// <summary>Logon task (owner). The PC reached the desktop: disarm, and finish a revert the boot task left pending.</summary>
    public static int OnLogon(AppServices app)
    {
        var state = BootGuard.Load(app.DataDirectory);
        if (state?.Triggered == true)
        {
            try
            {
                if (app.Snapshots.GetActive() is not null)
                    app.Engine.RevertAll(dryRun: false);
                Notifier.Show("NOVAIS boost · Proteção", "O Windows não iniciou bem depois dos ajustes, então tudo foi desfeito. Reinicie o PC.", app.Log);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                app.Log.Error($"Proteção: não foi possível concluir Reverter tudo no logon: {error.Message}. Use o menu [8].");
                Notifier.Show("NOVAIS boost · Proteção", "A reversão automática ficou incompleta. Abra o NOVAIS boost e use Reverter / Backups.", app.Log);
                return 1;
            }
        }
        Disarm(app.DataDirectory, app.Log);
        return 0;
    }

    public static void Disarm(string dataDirectory, IActionLog log)
    {
        BootGuard.Clear(dataDirectory);
        foreach (var schedule in new[] { Schedules.BootGuard, Schedules.BootConfirm })
            try
            {
                TaskSchedulerService.Remove(schedule);
            }
            catch (Win32Exception)
            {
                // Already gone.
            }
        log.Info("Proteção depois do reinício desligada: o PC iniciou normalmente.");
    }
}

/// <summary>Loads the owner's NTUSER.DAT under HKEY_USERS\&lt;sid&gt; when nobody is logged on (SYSTEM at boot).</summary>
internal sealed partial class UserHive : IDisposable
{
    private readonly string? _mounted;

    private UserHive(string? mounted) => _mounted = mounted;

    public static UserHive Mount(string sid, IActionLog log)
    {
        using (var loaded = Registry.Users.OpenSubKey(sid))
            if (loaded is not null)
                return new UserHive(null);
        using var profile = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{sid}");
        var folder = profile?.GetValue("ProfileImagePath") as string;
        if (folder is null)
            return new UserHive(null);
        EnablePrivilege("SeRestorePrivilege");
        EnablePrivilege("SeBackupPrivilege");
        var result = RegLoadKeyW(new IntPtr(unchecked((int)0x80000003)) /* HKEY_USERS */, sid, Path.Combine(Environment.ExpandEnvironmentVariables(folder), "NTUSER.DAT"));
        if (result != 0)
        {
            log.Warning($"Não foi possível carregar o perfil do usuário (erro {result}); valores do usuário ficam para o próximo logon.");
            return new UserHive(null);
        }
        return new UserHive(sid);
    }

    public void Dispose()
    {
        if (_mounted is not null)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            _ = RegUnLoadKeyW(new IntPtr(unchecked((int)0x80000003)), _mounted);
        }
    }

    private static void EnablePrivilege(string name)
    {
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 0x0020 | 0x0008, out var token))
            return;
        try
        {
            if (!LookupPrivilegeValueW(null, name, out var luid))
                return;
            var privileges = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 0x2 };
            AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint Count;
        public long Luid;
        public uint Attributes;
    }

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegLoadKeyW(IntPtr key, string subKey, string file);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegUnLoadKeyW(IntPtr key, string subKey);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValueW(string? system, string name, out long luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustTokenPrivileges(IntPtr token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, ref TokenPrivileges state,
        uint length, IntPtr previous, IntPtr returned);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);
}

/// <summary>
/// 20.3: checks releases/latest.json, downloads the new exe, checks SHA256 and Authenticode (same publisher as the running exe)
/// and swaps it in place. The previous exe is kept as "NOVAIS boost.anterior.exe" for --voltar-versao.
/// </summary>
internal static partial class Updater
{
    private const string PreviousName = "NOVAIS boost.anterior.exe";

    public static UpdateManifest? Check(IActionLog log)
    {
        try
        {
            using var http = Client(TimeSpan.FromSeconds(6));
            var manifest = Updates.Parse(http.GetStringAsync(Updates.ManifestUrl).GetAwaiter().GetResult());
            return Updates.IsNewer(Program.Version, manifest.Version) ? manifest : null;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or InvalidDataException or System.Text.Json.JsonException)
        {
            log.Info($"Atualização: não foi possível verificar ({error.Message}).");
            return null;
        }
    }

    public static void Install(UpdateManifest manifest, IActionLog log)
    {
        var current = Environment.ProcessPath ?? throw new InvalidOperationException("Executável não encontrado.");
        if (!string.Equals(Path.GetExtension(current), ".exe", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileNameWithoutExtension(current).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A atualização automática só funciona na versão publicada (NOVAIS boost.exe).");
        var folder = Path.GetDirectoryName(current)!;
        var download = Path.Combine(folder, "NOVAIS boost.novo.exe");

        using (var http = Client(TimeSpan.FromMinutes(5)))
        {
            var bytes = http.GetByteArrayAsync(manifest.Url).GetAwaiter().GetResult();
            if (!Updates.HashMatches(bytes, manifest.Sha256))
                throw new InvalidDataException("O SHA256 do arquivo baixado não confere com o latest.json. Nada foi trocado.");
            File.WriteAllBytes(download, bytes);
        }
        try
        {
            if (!IsTrusted(download))
                throw new InvalidDataException("A assinatura digital (Authenticode) do arquivo baixado não é válida. Nada foi trocado.");
            if (Publisher(current) is not { } publisher || !string.Equals(publisher, Publisher(download), StringComparison.Ordinal))
                throw new InvalidDataException("O arquivo baixado não foi assinado pelo mesmo autor desta versão. Nada foi trocado.");
            var previous = Path.Combine(folder, PreviousName);
            File.Delete(previous);
            File.Move(current, previous);   // Windows lets a running exe be renamed.
            File.Move(download, current);
            log.Info($"Atualizado para {manifest.Version}. Versão anterior guardada em {previous}.");
        }
        finally
        {
            File.Delete(download);
        }
    }

    /// <summary>--voltar-versao: swaps the current exe with the one kept by the last update.</summary>
    public static bool Rollback(IActionLog log)
    {
        var current = Environment.ProcessPath!;
        var previous = Path.Combine(Path.GetDirectoryName(current)!, PreviousName);
        if (!File.Exists(previous))
            return false;
        var swap = current + ".troca";
        File.Move(current, swap);
        File.Move(previous, current);
        File.Move(swap, previous);
        log.Info("Versão anterior restaurada (--voltar-versao).");
        return true;
    }

    private static HttpClient Client(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"NOVAIS-boost/{Program.Version}");
        return http;
    }

    private static string? Publisher(string file)
    {
        try
        {
#pragma warning disable SYSLIB0057 // The signer certificate of a signed PE file; X509CertificateLoader does not read Authenticode.
            return X509Certificate.CreateFromSignedFile(file).Subject;
#pragma warning restore SYSLIB0057
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    /// <summary>WinVerifyTrust with WINTRUST_ACTION_GENERIC_VERIFY_V2, no UI, whole chain revocation check.</summary>
    private static bool IsTrusted(string file)
    {
        var path = Marshal.StringToCoTaskMemUni(file);
        var fileInfo = Marshal.AllocHGlobal(32);
        var data = Marshal.AllocHGlobal(96);
        try
        {
            for (var offset = 0; offset < 32; offset++) Marshal.WriteByte(fileInfo, offset, 0);
            for (var offset = 0; offset < 96; offset++) Marshal.WriteByte(data, offset, 0);
            Marshal.WriteInt32(fileInfo, 0, 32);           // WINTRUST_FILE_INFO.cbStruct
            Marshal.WriteIntPtr(fileInfo, 8, path);        // pcwszFilePath
            Marshal.WriteInt32(data, 0, 88);               // WINTRUST_DATA.cbStruct (x64)
            Marshal.WriteInt32(data, 24, 2);               // dwUIChoice = WTD_UI_NONE
            Marshal.WriteInt32(data, 28, 0x1);             // fdwRevocationChecks = WTD_REVOKE_WHOLECHAIN
            Marshal.WriteInt32(data, 32, 1);               // dwUnionChoice = WTD_CHOICE_FILE
            Marshal.WriteIntPtr(data, 40, fileInfo);       // pFile
            Marshal.WriteInt32(data, 48, 1);               // dwStateAction = WTD_STATEACTION_VERIFY
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            var result = WinVerifyTrust(new IntPtr(-1), ref action, data);
            Marshal.WriteInt32(data, 48, 2);               // WTD_STATEACTION_CLOSE
            WinVerifyTrust(new IntPtr(-1), ref action, data);
            return result == 0;
        }
        finally
        {
            Marshal.FreeHGlobal(data);
            Marshal.FreeHGlobal(fileInfo);
            Marshal.FreeCoTaskMem(path);
        }
    }

    [LibraryImport("wintrust.dll")]
    private static partial int WinVerifyTrust(IntPtr window, ref Guid action, IntPtr data);
}

/// <summary>20.7: opt-in error report. Saved locally always; sent only with an endpoint configured.</summary>
internal static class ErrorReporter
{
    public static void Report(Exception error, string? dataDirectory)
    {
        if (dataDirectory is null)
            return;
        var settings = AppSettings.Load(dataDirectory, new NullLog()).ErrorReportsOrDefault;
        if (!settings.Enabled)
            return;
        var report = ErrorReports.Create(error, Program.Version, Environment.OSVersion.Version.Build.ToString(System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Now, Environment.UserName, Environment.MachineName);
        var json = ErrorReports.ToJson(report);
        var folder = Path.Combine(dataDirectory, "reports", "errors");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, $"erro-{DateTime.Now:yyyyMMdd-HHmmss}.json"), json);
        if (settings.Endpoint is null)
            return;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            http.PostAsync(settings.Endpoint, new StringContent(json, Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
        }
        catch (Exception sendError) when (sendError is HttpRequestException or TaskCanceledException)
        {
            // Kept on disk; nothing else to do.
        }
    }
}

/// <summary>20.8: undoes everything and removes what NOVAIS boost left on the PC.</summary>
internal static class Uninstaller
{
    public static void Run(AppServices app, Func<string, bool> confirm)
    {
        if (app.Snapshots.GetActive() is not null && confirm("Desfazer todos os ajustes antes (Reverter tudo)? Recomendado."))
            app.Engine.RevertAll(dryRun: false);

        foreach (var schedule in Schedules.All.Append(Schedules.BootGuard).Append(Schedules.BootConfirm))
            if (TaskSchedulerService.IsRegistered(schedule))
                TaskSchedulerService.Remove(schedule);
        try
        {
            WindowsCommand.Run("schtasks.exe", "/delete", "/tn", Schedules.Folder.TrimEnd('\\'), "/f");
        }
        catch (Win32Exception)
        {
            // The folder is removed with its last task on most builds.
        }
        try
        {
            var profiles = NvidiaDrs.DeleteOwnProfiles();
            if (profiles > 0)
                AnsiConsole.MarkupLine($"[grey]{profiles} perfil(is) NVIDIA \"NOVAIS boost - ...\" removido(s).[/]");
        }
        catch (Exception error) when (error is InvalidOperationException or DllNotFoundException)
        {
            app.Log.Warning($"Desinstalação: perfis NVIDIA não verificados: {error.Message}");
        }
        if (PowerPlans.NovaisPlanInstalled() && PowerPlans.Active() != PowerPlans.Novais)
            WindowsCommand.Run("powercfg.exe", "/delete", PowerPlans.Novais.ToString("D"));
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\AppUserModelId\NovaisBoost.App", throwOnMissingSubKey: false);
        BootGuard.Clear(app.DataDirectory);
        app.Log.Info("Desinstalação: tarefas, perfis próprios, plano de energia e registro de notificações removidos.");

        if (confirm($"Apagar também os backups, relatórios e logs em {app.DataDirectory}? (sem eles, não dá para reverter depois)"))
        {
            AnsiConsole.MarkupLine("[grey]A pasta será apagada quando o programa fechar.[/]");
            var script = $"/c timeout /t 3 /nobreak >nul & rmdir /s /q \"{app.DataDirectory}\"";
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), script) { CreateNoWindow = true, UseShellExecute = false });
        }
        AnsiConsole.MarkupLine("[green]Pronto. Agora é só apagar a pasta do NOVAIS boost.exe.[/]");
    }
}
