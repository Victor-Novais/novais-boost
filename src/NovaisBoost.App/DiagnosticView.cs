using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using NovaisBoost.Core;
using Spectre.Console;

namespace NovaisBoost.App;

internal static class DiagnosticView
{
    private static readonly JsonSerializerOptions ReportJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static DiagnosticReport Run(AppServices app)
    {
        var report = Run(() =>
        {
            app.Refresh();
            return app.Pc;
        }, app.Log, Path.Combine(app.DataDirectory, "reports"));
        if (app.Profiles.Count > 0)
            AnsiConsole.MarkupLine($"[#C084FC]Perfis de hardware detectados:[/] {Markup.Escape(string.Join(", ", app.Profiles.Select(profile => profile.Name)))} " +
                "[grey](veja o menu [[3]])[/]");
        return report;
    }

    public static DiagnosticReport Run(Func<HardwareSnapshot> capture, IActionLog log, string? reportDirectory)
    {
        HardwareSnapshot pc = null!;
        AnsiConsole.Status().Spinner(Spinner.Known.Dots).SpinnerStyle(new Style(new Color(168, 85, 247)))
            .Start("Analisando o PC...", _ => pc = capture());
        var report = Core.Diagnostics.Evaluate(pc, DateOnly.FromDateTime(DateTime.Now));
        log.Info($"Diagnóstico concluído: nota {report.Score}, {report.Findings.Count} ponto(s) de atenção.");

        DrawHardware(pc);
        DrawScore(report);
        DrawFindings(report);
        DrawProcesses(pc);

        if (pc.ProbeErrors.Count > 0)
            AnsiConsole.MarkupLine($"[grey]Não foi possível ler: {Markup.Escape(string.Join("; ", pc.ProbeErrors))}[/]");
        if (reportDirectory is not null)
            Save(pc, report, reportDirectory, log);
        return report;
    }

    private static void DrawHardware(HardwareSnapshot pc)
    {
        var grid = new Grid().AddColumn(new GridColumn().NoWrap()).AddColumn();
        void Row(string label, string? value) => grid.AddRow($"[#C084FC]{label}[/]", Markup.Escape(value ?? "Indisponível"));

        Row("Windows", $"{pc.Os.Caption} {pc.Os.DisplayVersion} · build {pc.Os.Build}{(pc.Os.IsLaptop ? " · notebook" : "")}");
        Row("CPU", pc.Cpu is { } cpu ? $"{cpu.Name} · {cpu.Cores} núcleos / {cpu.Threads} threads" : null);
        foreach (var gpu in pc.Gpus)
            Row(gpu.IsIntegrated ? "GPU integrada" : "GPU", string.Join(" · ", new[]
            {
                gpu.Name,
                gpu.VramBytes is { } vram ? Core.Diagnostics.Gb(vram) : null,
                gpu.DriverVersion is { } version ? $"driver {version}" : null,
                gpu.ResizableBar switch { true => "ReBAR ligado", false => "ReBAR desligado", _ => null }
            }.OfType<string>()));
        if (pc.Memory is { } memory)
        {
            var modules = memory.Modules.GroupBy(module => (module.CapacityBytes, module.Kind, module.RatedMts, module.ConfiguredMts))
                .Select(group => $"{group.Count()}× {Core.Diagnostics.Gb(group.Key.CapacityBytes)} {group.Key.Kind.ToString().ToUpperInvariant()} " +
                    $"{group.Key.ConfiguredMts} MT/s" + (group.Key.RatedMts > group.Key.ConfiguredMts ? $" (nominal {group.Key.RatedMts})" : ""));
            Row("RAM", $"{Core.Diagnostics.Gb(memory.TotalBytes)} · {memory.UsedPercent:0}% em uso · {string.Join(", ", modules)}");
        }
        Row("Placa-mãe", pc.Board is { } board
            ? $"{board.Manufacturer} {board.Product} · BIOS {board.BiosVersion}{(board.BiosDate is { } date ? $" ({date:dd/MM/yyyy})" : "")}" : null);
        foreach (var disk in pc.Disks)
            Row(disk.IsSystem ? "Disco (sistema)" : "Disco", $"{disk.Name} · {disk.Bus} {disk.Media} · {Core.Diagnostics.Gb(disk.SizeBytes)}");
        foreach (var volume in pc.Volumes)
            Row($"Unidade {volume.Letter}", $"{Core.Diagnostics.Gb(volume.FreeBytes)} livres de {Core.Diagnostics.Gb(volume.TotalBytes)} ({volume.FreePercent:0}%)");
        foreach (var display in pc.Displays)
            Row("Monitor", $"{display.Name} · {display.Width}x{display.Height} · {display.CurrentHz} Hz (máx. {display.MaxHz} Hz)");
        foreach (var network in pc.Networks.Where(network => network.Connected))
            Row("Rede", $"{network.Name} · {(network.Wired ? "cabo" : "Wi-Fi")} · {network.LinkBitsPerSecond / 1_000_000} Mbps");
        Row("Energia", pc.PowerPlan?.Name);

        AnsiConsole.Write(new Panel(grid).Header("[bold] Hardware detectado [/]").Border(BoxBorder.Rounded).BorderColor(new Color(124, 58, 237)));
    }

    private static void DrawScore(DiagnosticReport report)
    {
        var color = report.Score >= 80 ? "green" : report.Score >= 60 ? "yellow" : "red";
        const int width = 40;
        var filled = (int)Math.Round(report.Score / 100d * width);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  [bold]Nota de saúde:[/] [bold {color}]{report.Score}/100[/]");
        AnsiConsole.MarkupLine($"  [{color}]{new string('█', filled)}[/][grey]{new string('░', width - filled)}[/]");
        AnsiConsole.WriteLine();
    }

    private static void DrawFindings(DiagnosticReport report)
    {
        if (report.Findings.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]Nenhum problema encontrado.[/]");
            return;
        }

        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey)
            .AddColumn(new TableColumn("").NoWrap()).AddColumn("Problema").AddColumn("O que fazer");
        foreach (var finding in report.Findings)
        {
            var badge = finding.Severity switch
            {
                Severity.Critical => "[bold red]CRÍTICO[/]",
                Severity.Warning => "[bold yellow]ATENÇÃO[/]",
                _ => "[bold blue]INFO[/]"
            };
            table.AddRow(new Markup(badge),
                new Markup($"[bold]{Markup.Escape(finding.Title)}[/]\n[grey]{Markup.Escape(finding.Detail)}[/]"),
                new Markup(Markup.Escape(finding.Recommendation)));
        }
        AnsiConsole.Write(table);
    }

    private static void DrawProcesses(HardwareSnapshot pc)
    {
        if (pc.TopProcesses.Count == 0)
            return;
        var table = new Table().Border(TableBorder.Simple).Title("[bold]Maiores consumidores de RAM[/]")
            .AddColumn("Processo").AddColumn(new TableColumn("Instâncias").RightAligned()).AddColumn(new TableColumn("RAM").RightAligned());
        foreach (var process in pc.TopProcesses)
            table.AddRow(Markup.Escape(process.Name), process.Instances.ToString(CultureInfo.InvariantCulture), Core.Diagnostics.Gb(process.WorkingSetBytes));
        AnsiConsole.Write(table);
    }

    private static void Save(HardwareSnapshot pc, DiagnosticReport report, string directory, IActionLog log)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"diagnostico-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new { pc, report }, ReportJson));
            log.Info($"Relatório de diagnóstico salvo em {path}.");
            AnsiConsole.MarkupLine($"[grey]Relatório salvo em {Markup.Escape(path)}[/]");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            log.Warning($"Falha ao salvar o relatório: {error.Message}");
        }
    }
}

internal sealed class NullLog : IActionLog
{
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message) { }
}
