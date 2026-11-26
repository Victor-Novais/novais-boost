using System.Diagnostics;
using System.Globalization;
using NovaisBoost.Core;
using Spectre.Console;

namespace NovaisBoost.App;

internal sealed class ConsoleUi
{
    private static readonly string[] Brand =
    [
        "███╗   ██╗ ██████╗ ██╗   ██╗ █████╗ ██╗███████╗",
        "████╗  ██║██╔═══██╗██║   ██║██╔══██╗██║██╔════╝",
        "██╔██╗ ██║██║   ██║██║   ██║███████║██║███████╗",
        "██║╚██╗██║██║   ██║╚██╗ ██╔╝██╔══██║██║╚════██║",
        "██║ ╚████║╚██████╔╝ ╚████╔╝ ██║  ██║██║███████║",
        "╚═╝  ╚═══╝ ╚═════╝   ╚═══╝  ╚═╝  ╚═╝╚═╝╚══════╝"
    ];

    private static readonly string[] Boost =
    [
        "██████╗  ██████╗  ██████╗ ███████╗████████╗",
        "██╔══██╗██╔═══██╗██╔═══██╗██╔════╝╚══██╔══╝",
        "██████╔╝██║   ██║██║   ██║███████╗   ██║",
        "██╔══██╗██║   ██║██║   ██║╚════██║   ██║",
        "██████╔╝╚██████╔╝╚██████╔╝███████║   ██║",
        "╚═════╝  ╚═════╝  ╚═════╝ ╚══════╝   ╚═╝"
    ];

    private static readonly Color[] Gradient =
    [
        new(124, 58, 237), new(142, 69, 241), new(159, 80, 245),
        new(168, 85, 247), new(182, 113, 250), new(192, 132, 252)
    ];

    private static readonly string[] Menu =
    [
        "Diagnóstico completo", "Otimização Universal", "Perfil do meu Hardware", "Jogos & Streaming", "Process Guardian",
        "Cronogramas", "Benchmark antes/depois", "Reverter / Backups", "Rede & Latência"
    ];

    private readonly AppServices _app;
    private readonly Views _views;

    public ConsoleUi(AppServices app)
    {
        _app = app;
        _views = new Views(app, this);
    }

    private TweakEngine Engine => _app.Engine;
    private IActionLog Log => _app.Log;

    public static void DrawBanner()
    {
        AnsiConsole.WriteLine();
        if (AnsiConsole.Profile.Width >= 50)
        {
            for (var row = 0; row < Brand.Length; row++)
                AnsiConsole.Write(new Text("  " + Brand[row] + Environment.NewLine, new Style(Gradient[row], decoration: Decoration.Bold)));
            foreach (var row in Boost)
                AnsiConsole.Write(new Text("       " + row + Environment.NewLine, new Style(Color.White, decoration: Decoration.Bold)));
        }
        else
        {
            AnsiConsole.MarkupLine("[bold #A855F7]NOVAIS[/] [bold white]boost[/]");
        }

        AnsiConsole.Write(new Rule().RuleStyle("#A855F7"));
        AnsiConsole.MarkupLine($"[grey]Mais FPS · Menos latência · Otimizador para Windows 11 · v{Program.Version}[/]");
        AnsiConsole.Write(new Rule().RuleStyle("#A855F7"));
        AnsiConsole.WriteLine();
    }

    public void Run()
    {
        while (true)
        {
            DrawMenu();
            if (Console.IsInputRedirected)
                return;

            var input = ReadChoice();
            if (input is null or "0")
                return;
            if (!int.TryParse(input, out var choice) || choice < 1 || choice > Menu.Length)
            {
                AnsiConsole.MarkupLine($"[yellow]Escolha uma opção de 0 a {Menu.Length}.[/]");
                continue;
            }

            Log.Info($"Menu: {Menu[choice - 1]}.");
            AnsiConsole.Write(new Rule($"[bold]{Markup.Escape(Menu[choice - 1])}[/]").RuleStyle("#7C3AED").LeftJustified());
            RunSafely(() =>
            {
                switch (choice)
                {
                    case 1: DiagnosticView.Run(_app); break;
                    case 2: ShowUniversal(); break;
                    case 3: _views.HardwareProfile(); break;
                    case 4: _views.Games(); break;
                    case 5: _views.Guardian(); break;
                    case 6: _views.Schedules(); break;
                    case 7: _views.Benchmark(); break;
                    case 8: ShowBackups(); break;
                    case 9: _views.Network(); break;
                }
            });
            AnsiConsole.WriteLine();
        }
    }

    private static void DrawMenu()
    {
        if (AnsiConsole.Profile.Width >= 64)
        {
            var grid = new Grid().AddColumn().AddColumn();
            for (var index = 0; index < 5; index++)
                grid.AddRow(Item(index + 1, Menu[index]), index + 5 < Menu.Length ? Item(index + 6, Menu[index + 5]) : "");
            AnsiConsole.Write(grid);
        }
        else
        {
            for (var index = 0; index < Menu.Length; index++)
                AnsiConsole.MarkupLine(Item(index + 1, Menu[index]));
        }
        AnsiConsole.MarkupLine(Item(0, "Sair"));
    }

    private void ShowUniversal()
    {
        AnsiConsole.MarkupLine(Item(1, "Nível 1 · Seguro — para qualquer PC"));
        AnsiConsole.MarkupLine(Item(2, "Nível 2 · Competitivo — inclui o nível 1"));
        AnsiConsole.MarkupLine(Item(3, "Nível 3 · Extremo — PC dedicado a jogos, inclui 1 e 2"));
        AnsiConsole.MarkupLine(Item(4, "Ajustes individuais"));
        AnsiConsole.MarkupLine(Item(0, "Voltar"));
        switch (ReadChoice())
        {
            case "1":
                ReviewAndApply(LevelTweaks(TweakLevel.Safe));
                break;
            case "2":
                ReviewAndApply(LevelTweaks(TweakLevel.Competitive));
                break;
            case "3":
                AnsiConsole.MarkupLine("[bold red]Nível Extremo[/]: inclui ajustes que [bold]reduzem a segurança[/] do Windows (VBS/Integridade de memória) " +
                    "e desligam serviços úteis. Use só em PC dedicado a jogos.");
                if (Confirm("Entendi os riscos. Continuar?"))
                    ReviewAndApply(LevelTweaks(TweakLevel.Extreme));
                break;
            case "4":
                ShowIndividual();
                break;
        }
    }

    private IReadOnlyList<TweakDefinition> LevelTweaks(TweakLevel level)
    {
        IReadOnlyList<TweakDefinition> all = [];
        AnsiConsole.Status().Spinner(Spinner.Known.Dots).Start("Analisando hardware, dispositivos e jogos...", _ => all = _app.AllTweaks);
        // Per-machine tweaks for specific screens (DNS, startup apps) are offered there, not in the levels.
        return all.Where(tweak => tweak.Level <= level).ToArray();
    }

    /// <summary>Read-only preview of a level, used by the --simular command line option.</summary>
    public void Preview(TweakLevel level)
    {
        var (pending, skipped, alreadyApplied) = Simulate(LevelTweaks(level));
        DrawSkipped(skipped);
        AnsiConsole.MarkupLine($"[green]{alreadyApplied} ajuste(s) já aplicados.[/]");
        if (pending.Count > 0)
            DrawPending(pending);
        foreach (var (tweak, changes) in pending)
        {
            AnsiConsole.MarkupLine($"[bold]{Markup.Escape(tweak.Name)}[/]");
            DrawPreview(changes);
        }
    }

    /// <summary>Simulates the tweaks, lets the user review and deselect, then applies them as one batch.</summary>
    public void ReviewAndApply(IEnumerable<TweakDefinition> candidates)
    {
        var (pending, skipped, alreadyApplied) = Simulate(candidates);
        DrawSkipped(skipped);
        if (alreadyApplied > 0)
            AnsiConsole.MarkupLine($"[green]{alreadyApplied} ajuste(s) já estavam aplicados.[/]");
        if (pending.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]Nada a fazer: tudo já está aplicado.[/]");
            return;
        }

        while (true)
        {
            DrawPending(pending);
            AnsiConsole.MarkupLine($"{Item("A", "Aplicar todos")}   {Item("P", "Personalizar (tirar itens)")}   {Item("D", "Ver detalhes")}   {Item(0, "Voltar")}");
            switch (ReadChoice()?.ToUpperInvariant())
            {
                case "A":
                    if (Confirm($"Aplicar {pending.Count} ajuste(s) com ponto de restauração e backup?"))
                        RunBatch(pending);
                    else
                        Log.Info("Aplicação em lote cancelada.");
                    return;
                case "P":
                    AnsiConsole.Write("Números para NÃO aplicar (ex.: 2 5 9): ");
                    var remove = (Console.ReadLine() ?? "").Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
                        .Select(text => int.TryParse(text, out var number) ? number - 1 : -1)
                        .Where(index => index >= 0 && index < pending.Count).ToHashSet();
                    pending = pending.Where((_, index) => !remove.Contains(index)).ToList();
                    if (pending.Count == 0)
                        return;
                    break;
                case "D":
                    foreach (var (tweak, changes) in pending)
                    {
                        AnsiConsole.MarkupLine($"[bold]{Markup.Escape(tweak.Name)}[/] [grey]— {Markup.Escape(tweak.Description)}[/]");
                        DrawPreview(changes);
                    }
                    break;
                default:
                    return;
            }
        }
    }

    private (List<(TweakDefinition Tweak, IReadOnlyList<SettingChange> Changes)> Pending, List<(TweakDefinition Tweak, string Reason)> Skipped, int AlreadyApplied)
        Simulate(IEnumerable<TweakDefinition> candidates)
    {
        var ordered = candidates.OrderBy(tweak => tweak.Level).ThenBy(tweak => tweak.Category, StringComparer.CurrentCulture)
            .ThenBy(tweak => tweak.Name, StringComparer.CurrentCulture).ToArray();
        var pending = new List<(TweakDefinition Tweak, IReadOnlyList<SettingChange> Changes)>();
        var skipped = new List<(TweakDefinition Tweak, string Reason)>();
        var alreadyApplied = 0;
        var avoided = _app.AvoidedTweaks;

        AnsiConsole.Status().Spinner(Spinner.Known.Dots).SpinnerStyle(new Style(new Color(168, 85, 247))).Start("Simulando os ajustes...", _ =>
        {
            foreach (var tweak in ordered)
            {
                if (avoided.Contains(tweak.Id))
                {
                    skipped.Add((tweak, "o perfil deste hardware desaconselha este ajuste"));
                    continue;
                }
                if (Engine.CheckCompatibility(tweak) is { } reason)
                {
                    skipped.Add((tweak, reason));
                    continue;
                }
                try
                {
                    var changes = Engine.Detect(tweak);
                    if (changes.Count == 0)
                        alreadyApplied++;
                    else
                        pending.Add((tweak, changes));
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    Log.Warning($"Simulação de {tweak.Id} falhou: {error.Message}");
                    skipped.Add((tweak, error.Message));
                }
            }
        });
        return (pending, skipped, alreadyApplied);
    }

    private void RunBatch(List<(TweakDefinition Tweak, IReadOnlyList<SettingChange> Changes)> pending)
    {
        var reviewed = pending.ToDictionary(item => item.Tweak.Id, item => item.Changes);
        IReadOnlyList<BatchResult> results = [];
        AnsiConsole.Progress().Columns(new TaskDescriptionColumn(), new ProgressBarColumn().CompletedStyle(new Style(new Color(168, 85, 247))), new PercentageColumn())
            .Start(context =>
            {
                var task = context.AddTask("Criando ponto de restauração...", maxValue: pending.Count);
                var started = false;
                results = Engine.ApplyBatch(pending.Select(item => item.Tweak).ToArray(), reviewed, tweak =>
                {
                    if (started)
                        task.Increment(1);
                    started = true;
                    task.Description = Markup.Escape(tweak.Name);
                });
                task.Value = task.MaxValue;
                task.Description = "Concluído";
            });

        var ok = results.Where(result => result.Error is null).ToArray();
        var failed = results.Where(result => result.Error is not null).ToArray();
        AnsiConsole.MarkupLine($"[bold green]{ok.Length} ajuste(s) aplicados[/]{(failed.Length > 0 ? $" · [bold red]{failed.Length} falharam (e foram desfeitos)[/]" : "")}");
        foreach (var failure in failed)
            AnsiConsole.MarkupLine($"  [red]✗[/] {Markup.Escape(failure.Tweak.Name)}: [grey]{Markup.Escape(failure.Error!)}[/]");
        if (ok.Any(result => result.Tweak.RequiresReboot))
            AnsiConsole.MarkupLine("[yellow]Reinicie o PC para concluir os ajustes que precisam de reinício.[/]");
        AnsiConsole.MarkupLine("[grey]Para desfazer tudo: menu [[8]] Reverter / Backups.[/]");
        _app.Refresh();
    }

    private static void DrawPending(List<(TweakDefinition Tweak, IReadOnlyList<SettingChange> Changes)> pending)
    {
        var table = new Table().Border(TableBorder.Rounded).BorderColor(new Color(124, 58, 237))
            .AddColumn(new TableColumn("#").RightAligned()).AddColumn("Nível").AddColumn("Categoria").AddColumn("Ajuste")
            .AddColumn(new TableColumn("Alterações").RightAligned()).AddColumn("Reinício");
        for (var index = 0; index < pending.Count; index++)
        {
            var (tweak, changes) = pending[index];
            table.AddRow(
                (index + 1).ToString(CultureInfo.InvariantCulture),
                tweak.Level switch { TweakLevel.Safe => "[green]1[/]", TweakLevel.Competitive => "[yellow]2[/]", _ => "[red]3[/]" },
                Markup.Escape(tweak.Category),
                $"{Markup.Escape(tweak.Name)}\n[grey]Risco: {Markup.Escape(tweak.Risk)}[/]",
                changes.Count.ToString(CultureInfo.InvariantCulture),
                tweak.RequiresReboot ? "[yellow]sim[/]" : "não");
        }
        AnsiConsole.Write(table);
    }

    private static void DrawSkipped(List<(TweakDefinition Tweak, string Reason)> skipped)
    {
        foreach (var (tweak, reason) in skipped)
            AnsiConsole.MarkupLine($"[grey]– {Markup.Escape(tweak.Name)} não se aplica: {Markup.Escape(reason)}[/]");
    }

    private void ShowIndividual()
    {
        var tweaks = _app.AllTweaks.OrderBy(tweak => tweak.Level).ThenBy(tweak => tweak.Category, StringComparer.CurrentCulture).ToArray();
        for (var index = 0; index < tweaks.Length; index++)
            AnsiConsole.MarkupLine($"{Item(index + 1, tweaks[index].Name)} [grey]({Markup.Escape(tweaks[index].Category)}, nível {(int)tweaks[index].Level + 1})[/]");
        AnsiConsole.MarkupLine(Item(0, "Voltar"));
        AnsiConsole.Write("Números dos ajustes (ex.: 1 4 7): ");
        var chosen = (Console.ReadLine() ?? "").Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(text => int.TryParse(text, out var number) ? number - 1 : -1)
            .Where(index => index >= 0 && index < tweaks.Length).Distinct().Select(index => tweaks[index]).ToArray();
        if (chosen.Length > 0)
            ReviewAndApply(chosen);
    }

    private void ShowBackups()
    {
        var active = _app.Snapshots.GetActive();
        if (active is null)
        {
            AnsiConsole.MarkupLine("[grey]Nenhum backup ativo para esta conta.[/]");
            return;
        }

        AnsiConsole.WriteLine($"Backup ativo: {active.Path}");
        AnsiConsole.WriteLine($"Ajustes registrados: {string.Join(", ", active.TweakIds)} · Ponto de restauração: {active.RestorePointNumber}");
        if (active.Pending)
            AnsiConsole.MarkupLine("[yellow]Há uma operação pendente neste backup.[/]");
        AnsiConsole.MarkupLine(Item(1, "Reverter tudo"));
        AnsiConsole.MarkupLine(Item(0, "Voltar"));
        if (ReadChoice() != "1")
            return;

        var changes = Engine.RevertAll();
        DrawPreview(changes);
        if (!Confirm("Reverter tudo e encerrar o backup ativo?"))
        {
            Log.Info("Reversão cancelada.");
            return;
        }
        Engine.RevertAll(dryRun: false, reviewedChanges: changes);
        _app.Refresh();
        AnsiConsole.MarkupLine("[green]Reversão concluída. Reinicie o PC para desfazer os ajustes que dependem de reinício.[/]");
    }

    private void RunSafely(Action operation)
    {
        try
        {
            operation();
        }
        catch (SystemProtectionDisabledException error)
        {
            Log.Warning(error.Message);
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(error.Message)}[/]");
            if (!Confirm("Abrir a Proteção do Sistema para ativá-la?"))
                return;

            try
            {
                var path = Path.Combine(Environment.SystemDirectory, "SystemPropertiesProtection.exe");
                using var settings = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                Log.Info("Configuração da Proteção do Sistema aberta.");
            }
            catch (Exception launchError)
            {
                ShowError(launchError);
            }
        }
        catch (Exception error)
        {
            ShowError(error);
        }
    }

    private void ShowError(Exception error)
    {
        Log.Error(error.ToString());
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(error.Message)}[/]");
    }

    private static void DrawPreview(IReadOnlyList<SettingChange> changes)
    {
        AnsiConsole.MarkupLine("[bold #C084FC]Simulação[/]");
        if (changes.Count == 0)
            AnsiConsole.WriteLine("O estado já corresponde ao esperado.");
        foreach (var change in changes)
            AnsiConsole.MarkupLine($"  {Markup.Escape(change.Target)}\n  [grey]{Markup.Escape(change.Current)}[/] → [#C084FC]{Markup.Escape(change.Expected)}[/]");
    }

    public static string Item(int index, string name) => Item(index.ToString(CultureInfo.InvariantCulture), name);

    public static string Item(string key, string name) => $"[bold #C084FC][[{key}]][/] {Markup.Escape(name)}";

    public static string? ReadChoice()
    {
        AnsiConsole.Write("Opção: ");
        return Console.ReadLine()?.Trim();
    }

    public static bool Confirm(string question)
    {
        AnsiConsole.Write($"{question} [s/N] ");
        var answer = Console.ReadLine()?.Trim();
        return string.Equals(answer, "s", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(answer, "sim", StringComparison.OrdinalIgnoreCase);
    }
}
