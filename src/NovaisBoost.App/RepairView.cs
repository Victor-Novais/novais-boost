using NovaisBoost.Core;
using Spectre.Console;

namespace NovaisBoost.App;

internal sealed record RepairScan(
    IReadOnlyList<(TweakDefinition Repair, IReadOnlyList<SettingChange> Changes)> Found,
    IReadOnlyList<(TweakDefinition Repair, string Reason)> Unreadable);

/// <summary>
/// [10] Modo Reparar: read-only scan for damage left by other optimizers, then the normal review flow
/// (simulation → restore point → backup → restore). Everything it changes can be undone in Reverter / Backups.
/// </summary>
internal static class RepairView
{
    /// <summary>Detection only: never writes. Works without administrator except for the boot configuration (BCD).</summary>
    public static RepairScan Scan(AppServices app)
    {
        var found = new List<(TweakDefinition, IReadOnlyList<SettingChange>)>();
        var unreadable = new List<(TweakDefinition, string)>();
        foreach (var repair in app.Repairs)
        {
            try
            {
                var changes = app.Engine.Detect(repair);
                if (changes.Count > 0)
                    found.Add((repair, changes));
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                unreadable.Add((repair, error.Message));
            }
        }
        app.Log.Info($"Varredura de reparos: {found.Count} alteração(ões) suspeita(s), {unreadable.Count} item(ns) sem leitura.");
        return new RepairScan(found, unreadable);
    }

    public static void Run(AppServices app, ConsoleUi ui)
    {
        AnsiConsole.MarkupLine("Procura alterações que \"pacotes de FPS\" e outros otimizadores deixam no Windows e que pioram o PC: " +
            "relógio de plataforma forçado, paginação desligada, serviços essenciais desativados, Defender e Windows Update " +
            "desligados por política, mitigações da CPU desligadas, valores de latência inválidos e pilha TCP alterada.");
        var scan = Spin(() => Scan(app));
        Draw(scan);
        if (scan.Found.Count > 0 && ConsoleUi.Confirm("Revisar e restaurar o padrão do Windows nesses itens?"))
            ui.ReviewAndApply(scan.Found.Select(item => item.Repair));
    }

    /// <summary>Read-only listing used by the --reparar command line option and by the menu.</summary>
    public static void Draw(RepairScan scan)
    {
        if (scan.Found.Count == 0)
            AnsiConsole.MarkupLine("[green]Nenhuma alteração suspeita encontrada.[/]");
        else
        {
            AnsiConsole.MarkupLine($"[bold yellow]Encontramos {scan.Found.Count} alteração(ões) de outros otimizadores ou pacotes de FPS:[/]");
            foreach (var (repair, changes) in scan.Found)
            {
                AnsiConsole.MarkupLine($"  [bold]• {Markup.Escape(repair.Name)}[/] [grey]({changes.Count} valor(es))[/]");
                AnsiConsole.MarkupLine($"    [grey]{Markup.Escape(repair.Description)}[/]");
                foreach (var change in changes)
                    AnsiConsole.MarkupLine($"    {Markup.Escape(change.Target)}: [red]{Markup.Escape(change.Current)}[/] → [green]{Markup.Escape(change.Expected)}[/]");
            }
        }
        foreach (var (repair, reason) in scan.Unreadable)
            AnsiConsole.MarkupLine($"[grey]– {Markup.Escape(repair.Name)}: não verificado ({Markup.Escape(reason)})[/]");
    }

    private static T Spin<T>(Func<T> work)
    {
        T result = default!;
        AnsiConsole.Status().Spinner(Spinner.Known.Dots).SpinnerStyle(new Style(new Color(168, 85, 247)))
            .Start("Procurando alterações de outros otimizadores...", _ => result = work());
        return result;
    }
}
