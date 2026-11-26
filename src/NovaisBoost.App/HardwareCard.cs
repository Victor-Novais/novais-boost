using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using NovaisBoost.Core;
using Spectre.Console;

namespace NovaisBoost.App;

internal static class HardwareCard
{
    public static void Draw(IActionLog log)
    {
        var windows = TryRead("Windows", "SELECT Caption, BuildNumber FROM Win32_OperatingSystem",
            item => $"{item["Caption"]} · build {item["BuildNumber"]}", log);
        var cpu = TryRead("CPU", "SELECT Name FROM Win32_Processor", item => item["Name"] as string, log);
        var gpu = TryRead("GPU", "SELECT Name FROM Win32_VideoController", item => item["Name"] as string, log);
        var ram = TryRead("RAM", "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem",
            item => item["TotalPhysicalMemory"] is ulong bytes
                ? $"{(bytes / 1_073_741_824d).ToString("0.#", CultureInfo.GetCultureInfo("pt-BR"))} GiB"
                : null, log);

        var grid = new Grid().AddColumn().AddColumn();
        grid.AddRow("[grey]Windows[/]", Markup.Escape(windows));
        grid.AddRow("[grey]CPU[/]", Markup.Escape(cpu));
        grid.AddRow("[grey]GPU[/]", Markup.Escape(gpu));
        grid.AddRow("[grey]RAM[/]", Markup.Escape(ram));
        AnsiConsole.Write(new Panel(grid).Header("Este PC").Border(BoxBorder.Rounded));
        AnsiConsole.WriteLine();
    }

    private static string TryRead(string label, string query, Func<ManagementBaseObject, string?> read, IActionLog log)
    {
        try
        {
            var values = Wmi.Query(query, read).Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim()).Distinct().ToArray();
            if (values.Length > 0)
                return string.Join(" / ", values);
            log.Warning($"Sem dados WMI para {label}.");
        }
        catch (Exception error) when (error is ManagementException or COMException or UnauthorizedAccessException)
        {
            log.Warning($"Consulta WMI de {label} falhou: {error.Message}");
        }
        return "Indisponível";
    }
}
