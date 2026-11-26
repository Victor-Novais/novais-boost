using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;
using Microsoft.Win32;
using NovaisBoost.Core;

namespace NovaisBoost.App;

internal sealed class WindowsRegistry : IRegistry
{
    public RegistryState Read(RegistryAction action)
    {
        using var root = OpenHive(action.Hive);
        using var key = root.OpenSubKey(action.Path);
        if (key is null)
            return new RegistryState(false, null, null, CountMissingSegments(root, action.Path));
        var native = ReadValue(key.Handle, action.ValueName);
        if (native is null)
            return new RegistryState(true, null);

        return new RegistryState(true, DescribeValue(native, action), native);
    }

    public void Write(RegistryAction action, RegistryState state)
    {
        using var root = OpenHive(action.Hive);
        if (state.KeyExists)
        {
            // Creates every missing key in the path (policy keys usually do not exist yet).
            using var key = root.CreateSubKey(action.Path, writable: true)
                ?? throw new InvalidOperationException($"Não foi possível criar a chave {action.Path}.");
            if (state.Value is null)
                key.DeleteValue(action.ValueName, throwOnMissingValue: false);
            else if (state.Native is { } native)
                CheckRegistryResult(RegSetValueEx(key.Handle, action.ValueName, 0, native.Type, native.Data, native.Data.Length));
            else
            {
                var (data, kind) = ToNative(state.Value);
                key.SetValue(action.ValueName, data, kind);
            }
            return;
        }

        using (var key = root.OpenSubKey(action.Path, writable: true))
        {
            if (key is null)
                return;
            key.DeleteValue(action.ValueName, throwOnMissingValue: false);
            if (key.ValueCount > 0 || key.SubKeyCount > 0)
                return;
        }

        // Remove the keys created by the tweak, from the leaf upwards, stopping at the first one in use.
        var parts = action.Path.Split('\\');
        for (var removed = 0; removed < Math.Max(1, state.MissingSegments) && parts.Length - removed > 1; removed++)
        {
            var path = string.Join('\\', parts[..(parts.Length - removed)]);
            using (var key = root.OpenSubKey(path))
            {
                if (key is null)
                    continue;
                if (key.ValueCount > 0 || key.SubKeyCount > 0)
                    break;
            }
            root.DeleteSubKey(path, throwOnMissingSubKey: false);
        }
    }

    private static int CountMissingSegments(RegistryKey root, string path)
    {
        var parts = path.Split('\\');
        var missing = 1;
        while (missing < parts.Length)
        {
            using var ancestor = root.OpenSubKey(string.Join('\\', parts[..(parts.Length - missing)]));
            if (ancestor is not null)
                break;
            missing++;
        }
        return missing;
    }

    private static NativeRegistryValue? ReadValue(SafeRegistryHandle key, string name)
    {
        const int fileNotFound = 2;
        const int moreData = 234;
        var length = 0;
        var result = RegQueryValueEx(key, name, IntPtr.Zero, out _, null, ref length);
        if (result == fileNotFound)
            return null;
        CheckRegistryResult(result);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            // Supply a nonempty buffer even for zero-length values to distinguish a read from a size query.
            var data = new byte[Math.Max(length, 1)];
            length = data.Length;
            result = RegQueryValueEx(key, name, IntPtr.Zero, out var type, data, ref length);
            if (result == fileNotFound)
                return null;
            if (result == moreData)
                continue;
            CheckRegistryResult(result);
            return new NativeRegistryValue(type, length == data.Length ? data : data[..length]);
        }

        throw new InvalidOperationException("O valor de registro mudou durante a leitura.");
    }

    private static RegistryValue DescribeValue(NativeRegistryValue native, RegistryAction action) => native.Type switch
    {
        0 => ByteArray(RegistryValueType.None, native.Data),
        1 => RegistryValue.From(RegistryValueType.String, RegistryText(native.Data)),
        2 => RegistryValue.From(RegistryValueType.ExpandString, RegistryText(native.Data)),
        3 => ByteArray(RegistryValueType.Binary, native.Data),
        4 when native.Data.Length == 4 => RegistryValue.From(RegistryValueType.Dword, BinaryPrimitives.ReadInt32LittleEndian(native.Data)),
        7 => RegistryValue.From(RegistryValueType.MultiString, RegistryText(native.Data).Split('\0', StringSplitOptions.RemoveEmptyEntries)),
        11 when native.Data.Length == 8 => RegistryValue.From(RegistryValueType.Qword, BinaryPrimitives.ReadInt64LittleEndian(native.Data)),
        _ => throw new InvalidDataException($"Tipo de registro sem suporte para backup em {action.Path} [{action.ValueName}].")
    };

    private static string RegistryText(byte[] data) => Encoding.Unicode.GetString(data).TrimEnd('\0');

    private static RegistryValue ByteArray(RegistryValueType kind, byte[] bytes) =>
        RegistryValue.From(kind, Array.ConvertAll(bytes, value => (int)value));

    private static void CheckRegistryResult(int result)
    {
        if (result != 0)
            throw new Win32Exception(result, $"Erro do Windows ao acessar o Registro (código {result}).");
    }

    [DllImport("advapi32.dll", EntryPoint = "RegQueryValueExW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int RegQueryValueEx(
        SafeRegistryHandle key, string name, IntPtr reserved, out uint type, [Out] byte[]? data, ref int length);

    [DllImport("advapi32.dll", EntryPoint = "RegSetValueExW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int RegSetValueEx(
        SafeRegistryHandle key, string name, int reserved, uint type, byte[] data, int length);

    private static RegistryKey OpenHive(RegistryHiveName hive) => RegistryKey.OpenBaseKey(hive switch
    {
        RegistryHiveName.CurrentUser => RegistryHive.CurrentUser,
        RegistryHiveName.LocalMachine => RegistryHive.LocalMachine,
        _ => throw new InvalidDataException("Hive de registro desconhecido.")
    }, RegistryView.Registry64);

    private static (object Data, RegistryValueKind Kind) ToNative(RegistryValue value) => value.Kind switch
    {
        RegistryValueType.String => (value.Value.GetString()!, RegistryValueKind.String),
        RegistryValueType.ExpandString => (value.Value.GetString()!, RegistryValueKind.ExpandString),
        RegistryValueType.Dword => (value.Value.GetInt32(), RegistryValueKind.DWord),
        RegistryValueType.Qword => (value.Value.GetInt64(), RegistryValueKind.QWord),
        RegistryValueType.MultiString => (value.Value.EnumerateArray().Select(text => text.GetString()!).ToArray(), RegistryValueKind.MultiString),
        RegistryValueType.Binary => (value.Value.EnumerateArray().Select(number => number.GetByte()).ToArray(), RegistryValueKind.Binary),
        RegistryValueType.None => (value.Value.EnumerateArray().Select(number => number.GetByte()).ToArray(), RegistryValueKind.None),
        _ => throw new InvalidDataException("Tipo de valor de registro desconhecido.")
    };
}

internal sealed class WindowsServiceController : IServiceController
{
    public ServiceStartup? GetStartup(string name)
    {
        if (!Regex.IsMatch(name, @"\A[a-zA-Z0-9_.-]+\z"))
            throw new InvalidDataException("Nome de serviço inválido.");

        var rows = Wmi.Query($"SELECT StartMode, DelayedAutoStart FROM Win32_Service WHERE Name = '{name}'", item =>
            (item["StartMode"] as string, item["DelayedAutoStart"] as bool?));
        if (rows.Length == 0)
            return null;
        return rows.Single() switch
        {
            ("Auto", true) => ServiceStartup.AutomaticDelayed,
            ("Auto", false) => ServiceStartup.Automatic,
            ("Manual", _) => ServiceStartup.Manual,
            ("Disabled", _) => ServiceStartup.Disabled,
            _ => throw new InvalidOperationException($"Tipo de inicialização indisponível para o serviço {name}.")
        };
    }

    public void SetStartup(string name, ServiceStartup startup)
    {
        var mode = startup switch
        {
            ServiceStartup.Automatic => "auto",
            ServiceStartup.AutomaticDelayed => "delayed-auto",
            ServiceStartup.Manual => "demand",
            ServiceStartup.Disabled => "disabled",
            _ => throw new InvalidDataException("Tipo de inicialização desconhecido.")
        };
        WindowsCommand.Run("sc.exe", "config", name, "start=", mode);
    }
}

internal sealed class WindowsPowerConfig : IPowerConfig
{
    public Guid GetActiveScheme()
    {
        var output = WindowsCommand.Run("powercfg.exe", "/getactivescheme");
        var match = Regex.Match(output, @"\b[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}\b");
        return Guid.TryParseExact(match.Value, "D", out var scheme) ? scheme
            : throw new InvalidOperationException("O Windows não informou o identificador do plano de energia ativo.");
    }

    public void SetActiveScheme(Guid scheme) => WindowsCommand.Run("powercfg.exe", "/setactive", scheme.ToString("D"));
}

internal static class WindowsCommand
{
    public static string Run(string executable, params string[] arguments) => Run(TimeSpan.FromSeconds(15), executable, arguments);

    public static string Run(TimeSpan timeout, string executable, params string[] arguments)
    {
        var info = new ProcessStartInfo(Path.IsPathRooted(executable) ? executable : Path.Combine(Environment.SystemDirectory, executable))
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Falha ao iniciar {executable}.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            if (!process.HasExited)
                process.Kill();
            throw new TimeoutException($"Tempo esgotado ao executar {executable}.");
        }

        Task.WhenAll(output, error).GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new Win32Exception(process.ExitCode, $"{executable} retornou o código {process.ExitCode}.");
        return output.Result;
    }
}
