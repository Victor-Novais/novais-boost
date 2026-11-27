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

    private static string[] Menu =>
    [
        Strings.T("menu.diagnostic"), Strings.T("menu.universal"), Strings.T("menu.hardware"), Strings.T("menu.games"), Strings.T("menu.guardian"),
        Strings.T("menu.schedules"), Strings.T("menu.benchmark"), Strings.T("menu.backups"), Strings.T("menu.network"), Strings.T("menu.repair"),
        Strings.T("menu.settings")
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
                AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(Strings.T("menu.invalid", Menu.Length))}[/]");
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
                    case 10: RepairView.Run(_app, this); break;
                    case 11: new SettingsView(_app).Run(); break;
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
            var rows = (Menu.Length + 1) / 2;
            for (var index = 0; index < rows; index++)
                grid.AddRow(Item(index + 1, Menu[index]), index + rows < Menu.Length ? Item(index + rows + 1, Menu[index + rows]) : "");
            AnsiConsole.Write(grid);
        }
        else
        {
            for (var index = 0; index < Menu.Length; index++)
                AnsiConsole.MarkupLine(Item(index + 1, Menu[index]));
        }
        AnsiConsole.MarkupLine(Item(0, Strings.T("menu.exit")));
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
        // Experimental tweaks are never part of a level (unless measured as a real gain on this PC); tweaks measured as
        // worse on this PC are left out too.
        return TweakLevels.Select(all, level, _app.LocalEvidence, _app.CommunityEvidence);
    }

    /// <summary>Read-only preview of a level, used by the --simular command line option.</summary>
    public void Preview(TweakLevel level)
    {
        var (pending, skipped, alreadyApplied) = Simulate(LevelTweaks(level));
        DrawSkipped(skipped);
        AnsiConsole.MarkupLine($"[green]{alreadyApplied} ajuste(s) já aplicados.[/]");
        if (pending.Count > 0)
            DrawPending(pending, AntiCheatWarnings(pending), _app.LocalEvidence, _app.CommunityEvidence);
        foreach (var (tweak, changes) in pending)
        {
            AnsiConsole.MarkupLine($"[bold]{Markup.Escape(tweak.Name)}[/]");
            DrawPreview(changes);
        }
    }

    /// <summary>Simulates the tweaks, lets the user review and deselect, then applies them as one batch.</summary>
    /// <returns>The batch results (empty when the user cancels or nothing is pending).</returns>
    public IReadOnlyList<BatchResult> ReviewAndApply(IEnumerable<TweakDefinition> candidates)
    {
        var (pending, skipped, alreadyApplied) = Simulate(candidates);
        DrawSkipped(skipped);
        if (alreadyApplied > 0)
            AnsiConsole.MarkupLine($"[green]{alreadyApplied} ajuste(s) já estavam aplicados.[/]");
        if (pending.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]Nada a fazer: tudo já está aplicado.[/]");
            return [];
        }

        var warnings = AntiCheatWarnings(pending);
        var evidence = _app.LocalEvidence;
        while (true)
        {
            DrawPending(pending, warnings, evidence, _app.CommunityEvidence);
            AnsiConsole.MarkupLine($"{Item("A", "Aplicar todos")}   {Item("P", "Personalizar (tirar itens)")}   {Item("D", "Ver detalhes")}   {Item(0, "Voltar")}");
            switch (ReadChoice()?.ToUpperInvariant())
            {
                case "A":
                    if (Confirm($"Aplicar {pending.Count} ajuste(s) com ponto de restauração e backup?"))
                        return RunBatch(pending);
                    Log.Info("Aplicação em lote cancelada.");
                    return [];
                case "P":
                    AnsiConsole.Write("Números para NÃO aplicar (ex.: 2 5 9): ");
                    var remove = (Console.ReadLine() ?? "").Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
                        .Select(text => int.TryParse(text, out var number) ? number - 1 : -1)
                        .Where(index => index >= 0 && index < pending.Count).ToHashSet();
                    pending = pending.Where((_, index) => !remove.Contains(index)).ToList();
                    if (pending.Count == 0)
                        return [];
                    break;
                case "D":
                    foreach (var (tweak, changes) in pending)
                    {
                        AnsiConsole.MarkupLine($"[bold]{Markup.Escape(tweak.Name)}[/] [grey]— {Markup.Escape(tweak.Description)}[/]");
                        DrawPreview(changes);
                    }
                    break;
                default:
                    return [];
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

    private IReadOnlyList<BatchResult> RunBatch(List<(TweakDefinition Tweak, IReadOnlyList<SettingChange> Changes)> pending)
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
        {
            AnsiConsole.MarkupLine("[yellow]Reinicie o PC para concluir os ajustes que precisam de reinício.[/]");
            try
            {
                BootProtection.Arm(_app, _app.Owner);
                AnsiConsole.MarkupLine("[grey]Proteção ligada: se o Windows não chegar à área de trabalho 2 vezes seguidas, tudo é desfeito sozinho.[/]");
            }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
            {
                Log.Warning($"Não foi possível armar a proteção depois do reinício: {error.Message}");
            }
        }
        AnsiConsole.MarkupLine("[grey]Para desfazer tudo: menu [[8]] Reverter / Backups.[/]");
        _app.Refresh();
        return results;
    }

    /// <summary>Tweak id → warning, for tweaks whose "compat.antiCheat" matches a game installed on this PC.</summary>
    private Dictionary<string, string> AntiCheatWarnings(List<(TweakDefinition Tweak, IReadOnlyList<SettingChange> Changes)> pending)
    {
        var warnings = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!pending.Any(item => item.Tweak.Compat.AntiCheat.Length > 0))
            return warnings;
        var installed = _app.Games.Select(game => game.Game).ToArray();
        foreach (var (tweak, _) in pending)
            if (Compatibility.AntiCheatWarning(tweak.Compat, installed) is { } warning)
                warnings[tweak.Id] = warning;
        return warnings;
    }

    private static void DrawPending(List<(TweakDefinition Tweak, IReadOnlyList<SettingChange> Changes)> pending,
        IReadOnlyDictionary<string, string> warnings, LocalEvidence evidence, IReadOnlyDictionary<string, CommunityEvidence> community)
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
                $"{Markup.Escape(tweak.Name)}\n[grey]Risco: {Markup.Escape(tweak.Risk)}[/]" +
                    EvidenceNote(tweak, evidence, community) +
                    (warnings.ContainsKey(tweak.Id) ? "\n[bold yellow]⚠ Aviso de anti-cheat abaixo[/]" : ""),
                changes.Count.ToString(CultureInfo.InvariantCulture),
                tweak.RequiresReboot ? "[yellow]sim[/]" : "não");
        }
        AnsiConsole.Write(table);
        foreach (var (tweak, _) in pending)
            if (warnings.TryGetValue(tweak.Id, out var warning))
                AnsiConsole.MarkupLine($"[bold yellow]⚠ {Markup.Escape(tweak.Name)}:[/] [yellow]{Markup.Escape(warning)}[/]");
    }

    /// <summary>Review line about how the effect is known: measured here, measured worse here, or not proven.</summary>
    private static string EvidenceNote(TweakDefinition tweak, LocalEvidence evidence, IReadOnlyDictionary<string, CommunityEvidence> community) =>
        LocalNote(tweak, evidence) + (community.GetValueOrDefault(tweak.Id) is { } shared
            ? $"\n[{(Community.IsProven(shared) ? "green" : Community.IsProvenWorse(shared) ? "red" : "grey")}]{Markup.Escape(Community.Describe(shared))}[/]"
            : "");

    private static string LocalNote(TweakDefinition tweak, LocalEvidence evidence) => evidence.Latest(tweak.Id) switch
    {
        { Verdict: Verdict.RealGain } entry => $"\n[green]Medido neste PC: {entry.OnePercentLowChange:+0.0;-0.0}% no 1% low ({Markup.Escape(entry.Game)})[/]",
        { Verdict: Verdict.Worse } entry => $"\n[red]Piorou neste PC: {entry.OnePercentLowChange:+0.0;-0.0}% no 1% low ({Markup.Escape(entry.Game)})[/]",
        not null when !tweak.IsProven => "\n[yellow]Efeito não comprovado (experimental; dentro do ruído neste PC)[/]",
        _ when !tweak.IsProven => "\n[yellow]Efeito não comprovado (experimental)[/]",
        _ => ""
    };

    private static void DrawSkipped(List<(TweakDefinition Tweak, string Reason)> skipped)
    {
        foreach (var (tweak, reason) in skipped)
            AnsiConsole.MarkupLine($"[grey]– {Markup.Escape(tweak.Name)} não se aplica: {Markup.Escape(reason)}[/]");
    }

    private void ShowIndividual()
    {
        var tweaks = _app.AllTweaks.OrderBy(tweak => tweak.Level).ThenBy(tweak => tweak.Category, StringComparer.CurrentCulture).ToArray();
        for (var index = 0; index < tweaks.Length; index++)
            AnsiConsole.MarkupLine($"{Item(index + 1, tweaks[index].Name)} [grey]({Markup.Escape(tweaks[index].Category)}, nível {(int)tweaks[index].Level + 1})[/]" +
                (_app.LocalEvidence.Latest(tweaks[index].Id)?.Verdict == Verdict.RealGain ? " [green]medido neste PC[/]"
                    : tweaks[index].IsProven ? "" : " [yellow]experimental · efeito não comprovado[/]"));
        if (tweaks.Any(tweak => !tweak.IsProven))
            AnsiConsole.MarkupLine("[grey]Ajustes experimentais não entram nos níveis: só são aplicados se você escolher aqui.[/]");
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
            AnsiConsole.MarkupLine($"{Item(2, "Desinstalar o NOVAIS boost")}   {Item(0, "Voltar")}");
            if (ReadChoice() == "2")
                Uninstaller.Run(_app, Confirm);
            return;
        }

        AnsiConsole.WriteLine($"Backup ativo: {active.Path}");
        AnsiConsole.WriteLine($"Ajustes registrados: {string.Join(", ", active.TweakIds)} · Ponto de restauração: {active.RestorePointNumber}");
        if (active.Pending)
            AnsiConsole.MarkupLine("[yellow]Há uma operação pendente neste backup.[/]");
        AnsiConsole.MarkupLine(Item(1, "Reverter tudo"));
        AnsiConsole.MarkupLine(Item(2, "Desinstalar o NOVAIS boost (desfaz tudo e remove tarefas, perfis e plano de energia)"));
        AnsiConsole.MarkupLine(Item(0, "Voltar"));
        var choice = ReadChoice();
        if (choice == "2")
        {
            Uninstaller.Run(_app, Confirm);
            return;
        }
        if (choice != "1")
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

    internal static void DrawPreview(IReadOnlyList<SettingChange> changes)
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
