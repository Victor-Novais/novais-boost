using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using NovaisBoost.Core;
using Spectre.Console;

namespace NovaisBoost.App;

/// <summary>An A/B test in progress, saved after every step so it survives the reboot some groups need.</summary>
internal sealed record ProofState(
    string Game, string Executable, string Group, int Rounds, int Seconds, DateTimeOffset CreatedAt,
    List<BenchmarkRun> Before, List<BenchmarkRun> After,
    string[]? AppliedIds = null, string[]? AppliedNames = null, bool NeedsReboot = false, DateTimeOffset? AppliedAt = null);

/// <summary>
/// "Provar ajustes": benchmark before → apply ONE group of tweaks → reboot if needed → benchmark after → statistical verdict.
/// The verdict becomes local evidence (evidence.json) and an HTML report; a group that did not help can be reverted alone.
/// </summary>
internal sealed class ProofView(AppServices app, ConsoleUi ui)
{
    private const int RoundSeconds = 60;
    private static readonly Color Purple = new(168, 85, 247);

    private string StatePath => Path.Combine(app.DataDirectory, "benchmarks", "prova-em-andamento.json");

    public void Run()
    {
        var presentMon = Benchmark.FindPresentMon(app.DataDirectory, app.Log);
        if (presentMon is null)
        {
            Views.PresentMonMissing(app);
            return;
        }

        var state = Load();
        if (state is not null)
        {
            AnsiConsole.MarkupLine($"Teste em andamento: [bold]{Markup.Escape(state.Group)}[/] em {Markup.Escape(state.Game)} " +
                $"({state.Before.Count}/{state.Rounds} antes, {state.After.Count}/{state.Rounds} depois).");
            AnsiConsole.MarkupLine($"{ConsoleUi.Item("C", "Continuar")}   {ConsoleUi.Item("D", "Descartar o teste")}   {ConsoleUi.Item(0, "Voltar")}");
            switch (ConsoleUi.ReadChoice()?.ToUpperInvariant())
            {
                case "C":
                    break;
                case "D":
                    Discard();
                    AnsiConsole.MarkupLine("[grey]Teste descartado. Os ajustes já aplicados continuam (desfaça em Reverter / Backups).[/]");
                    return;
                default:
                    return;
            }
        }
        else
        {
            state = Start();
            if (state is null)
                return;
        }

        if (!CaptureRounds(presentMon, state, after: false))
            return;

        if (state.AppliedIds is null)
        {
            state = Apply(state);
            if (state is null)
                return;
        }

        if (state.NeedsReboot && !RebootedSince(state.AppliedAt))
        {
            AnsiConsole.MarkupLine("[yellow]Este grupo só vale depois de reiniciar.[/] Reinicie o PC e volte em " +
                "[bold]Benchmark → Provar ajustes[/] para medir o \"depois\".");
            return;
        }

        if (!CaptureRounds(presentMon, state, after: true))
            return;
        Finish(state);
    }

    private ProofState? Start()
    {
        AnsiConsole.MarkupLine($"Como funciona: {BenchmarkSession.MinRounds} a {BenchmarkSession.MaxRounds} rodadas de {RoundSeconds} s " +
            "[bold]antes[/], aplicar [bold]um grupo[/] de ajustes, reiniciar se precisar e repetir as rodadas [bold]depois[/]. " +
            "Use sempre a mesma cena (ex.: mapa de treino, mesmo ponto, sem bots).");
        var game = Views.PickGame(app);
        if (game is null)
            return null;

        var groups = Spin("Procurando grupos com ajustes pendentes...", PendingGroups);
        if (groups.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]Não há ajustes pendentes para testar: tudo já está aplicado.[/]");
            return null;
        }
        for (var index = 0; index < groups.Count; index++)
            AnsiConsole.MarkupLine($"{ConsoleUi.Item(index + 1, groups[index].Group)} [grey]({groups[index].Count} ajuste(s))[/]");
        AnsiConsole.Write("Grupo para testar: ");
        if (!int.TryParse(Console.ReadLine(), out var chosen) || chosen < 1 || chosen > groups.Count)
            return null;

        AnsiConsole.Write($"Rodadas por lado ({BenchmarkSession.MinRounds} a {BenchmarkSession.MaxRounds}, padrão {BenchmarkSession.MinRounds}): ");
        var rounds = int.TryParse(Console.ReadLine(), out var typed) ? Math.Clamp(typed, BenchmarkSession.MinRounds, BenchmarkSession.MaxRounds)
            : BenchmarkSession.MinRounds;

        var state = new ProofState(game.Value.Name, game.Value.Exe, groups[chosen - 1].Group, rounds, RoundSeconds, DateTimeOffset.Now, [], []);
        Save(state);
        app.Log.Info($"Teste A/B iniciado: grupo {state.Group}, {state.Game}, {rounds} rodadas.");
        return state;
    }

    /// <summary>Categories with at least one tweak that is compatible, allowed by the hardware profile and not applied yet.</summary>
    private IReadOnlyList<(string Group, int Count)> PendingGroups()
    {
        var avoided = app.AvoidedTweaks;
        return app.AllTweaks
            .Where(tweak => !avoided.Contains(tweak.Id) && app.Engine.CheckCompatibility(tweak) is null)
            .Where(tweak =>
            {
                try
                {
                    return app.Engine.Detect(tweak).Count > 0;
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    return false;
                }
            })
            .GroupBy(tweak => tweak.Category, StringComparer.CurrentCulture)
            .Select(group => (group.Key, group.Count()))
            .OrderBy(group => group.Key, StringComparer.CurrentCulture).ToArray();
    }

    private bool CaptureRounds(string presentMon, ProofState state, bool after)
    {
        var runs = after ? state.After : state.Before;
        var side = after ? "depois" : "antes";
        while (runs.Count < state.Rounds)
        {
            var round = runs.Count + 1;
            AnsiConsole.MarkupLine($"[bold #C084FC]Rodada {round}/{state.Rounds} ({side})[/]: abra {Markup.Escape(state.Game)} na mesma cena. " +
                $"Os primeiros {BenchmarkSession.WarmupSeconds:0} s são descartados.");
            if (!ConsoleUi.Confirm("Começar a captura agora?"))
            {
                AnsiConsole.MarkupLine("[grey]Teste pausado. Continue depois em Benchmark → Provar ajustes.[/]");
                return false;
            }
            try
            {
                var run = Spin($"Capturando {state.Seconds} s...", () => Benchmark.Capture(presentMon, state.Executable, state.Game,
                    $"prova-{side}-{round}", state.Seconds, app, BenchmarkSession.WarmupSeconds));
                runs.Add(run);
                Save(state);
                Views.DrawRun(run);
            }
            catch (Exception error) when (error is InvalidOperationException or InvalidDataException or TimeoutException or IOException)
            {
                app.Log.Warning($"Rodada {round} ({side}) falhou: {error.Message}");
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(error.Message)}[/]");
                if (!ConsoleUi.Confirm("Tentar esta rodada de novo?"))
                    return false;
            }
        }
        return true;
    }

    private ProofState? Apply(ProofState state)
    {
        AnsiConsole.MarkupLine($"[bold]Agora aplique só o grupo {Markup.Escape(state.Group)}.[/] Tire itens se quiser; o resultado vale para o que for aplicado.");
        var candidates = app.AllTweaks.Where(tweak => string.Equals(tweak.Category, state.Group, StringComparison.CurrentCulture)).ToArray();
        var applied = ui.ReviewAndApply(candidates).Where(result => result.Error is null && result.Changes.Count > 0).ToArray();
        if (applied.Length == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Nenhum ajuste foi aplicado; não há o que comparar.[/] O \"antes\" continua salvo.");
            return null;
        }
        state = state with
        {
            AppliedIds = applied.Select(result => result.Tweak.Id).ToArray(),
            AppliedNames = applied.Select(result => result.Tweak.Name).ToArray(),
            NeedsReboot = applied.Any(result => result.Tweak.RequiresReboot),
            AppliedAt = DateTimeOffset.Now
        };
        Save(state);
        return state;
    }

    private void Finish(ProofState state)
    {
        var result = BenchmarkSession.Evaluate(state.Before.Select(run => run.Stats).ToArray(), state.After.Select(run => run.Stats).ToArray());
        DrawResult(state, result);

        var oneLow = result[BenchmarkMetric.OnePercentLowFps];
        var average = result[BenchmarkMetric.AverageFps];
        var evidence = app.LocalEvidence.With(state.AppliedIds!.Select(id => new LocalEvidenceEntry(id, state.Group, state.Game,
            result.Overall, average.ChangePercent, oneLow.ChangePercent, oneLow.CiLowPercent, oneLow.CiHighPercent, DateTimeOffset.Now)));
        evidence.Save(app.DataDirectory);
        app.Refresh();

        var report = SaveReport(state, result);
        app.Log.Info($"Teste A/B de {state.Group} em {state.Game}: {BenchmarkSession.Describe(result.Overall)} " +
            $"(1% low {oneLow.ChangePercent:+0.0;-0.0}%, IC {oneLow.CiLowPercent:+0.0;-0.0}% a {oneLow.CiHighPercent:+0.0;-0.0}%). Relatório: {report}.");
        AnsiConsole.MarkupLine($"Relatório: [bold]{Markup.Escape(report)}[/]");
        Discard();

        if (result.Overall != Verdict.RealGain &&
            ConsoleUi.Confirm($"O grupo {state.Group} deu \"{BenchmarkSession.Describe(result.Overall)}\". Reverter só este grupo?"))
            RevertGroup(state.AppliedIds!);
        if (ConsoleUi.Confirm("Abrir o relatório?"))
            OpenReport(report, app.Log);
    }

    private void RevertGroup(string[] ids)
    {
        var changes = app.Engine.RevertLatest(ids);
        ConsoleUi.DrawPreview(changes);
        if (changes.Count == 0 || !ConsoleUi.Confirm("Confirmar a reversão deste grupo?"))
            return;
        app.Engine.RevertLatest(ids, dryRun: false, reviewedChanges: changes);
        app.Refresh();
        AnsiConsole.MarkupLine("[green]Grupo revertido.[/] Reinicie o PC se algum ajuste do grupo pedia reinício.");
    }

    private static void DrawResult(ProofState state, SessionResult result)
    {
        var table = new Table().Title($"[bold]{Markup.Escape(state.Group)} · {Markup.Escape(state.Game)}[/]").Border(TableBorder.Rounded).BorderColor(Purple)
            .AddColumn("Métrica").AddColumn(new TableColumn("Antes").RightAligned()).AddColumn(new TableColumn("Depois").RightAligned())
            .AddColumn(new TableColumn("Diferença").RightAligned()).AddColumn(new TableColumn("IC 95%").RightAligned()).AddColumn("Veredito");
        foreach (var metric in result.Metrics)
        {
            var format = metric.Metric == BenchmarkMetric.P99FrameTimeMs ? "0.0" : "0";
            table.AddRow(BenchmarkSession.Name(metric.Metric),
                $"{metric.Before.Mean.ToString(format, CultureInfo.CurrentCulture)} ± {metric.Before.StdDev.ToString(format, CultureInfo.CurrentCulture)}",
                $"{metric.After.Mean.ToString(format, CultureInfo.CurrentCulture)} ± {metric.After.StdDev.ToString(format, CultureInfo.CurrentCulture)}",
                $"{metric.ChangePercent:+0.0;-0.0;0.0}%", $"{metric.CiLowPercent:+0.0;-0.0;0.0}% a {metric.CiHighPercent:+0.0;-0.0;0.0}%",
                VerdictMarkup(metric.Verdict));
        }
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"Veredito: {VerdictMarkup(result.Overall)} [grey](FPS médio e 1% low; IC por bootstrap com {BenchmarkSession.Resamples:N0} reamostragens)[/]");
        if (state.After.Concat(state.Before).Select(run => run.Stats.Bottleneck).FirstOrDefault(item => item != Bottleneck.Unknown) is var bottleneck and not Bottleneck.Unknown)
            AnsiConsole.MarkupLine($"[grey]Gargalo no jogo: {Markup.Escape(BenchmarkReport.Describe(bottleneck))}.[/]");
    }

    private static string VerdictMarkup(Verdict verdict) => verdict switch
    {
        Verdict.RealGain => "[bold green]Ganho real[/]",
        Verdict.Worse => "[bold red]Piorou[/]",
        _ => "[bold yellow]Dentro do ruído[/]"
    };

    private string SaveReport(ProofState state, SessionResult result)
    {
        var pc = app.Pc;
        var hardware = new List<string>();
        if (pc.Cpu is { } cpu)
            hardware.Add($"CPU: {cpu.Name}");
        hardware.AddRange(pc.Gpus.Where(gpu => !gpu.IsIntegrated).Select(gpu => $"GPU: {gpu.Name} (driver {gpu.DriverVersion ?? "?"})"));
        if (pc.Memory is { } memory)
            hardware.Add($"RAM: {Math.Round(memory.TotalBytes / 1_073_741_824d)} GB");
        hardware.Add($"Windows: {pc.Os.Caption} {pc.Os.DisplayVersion} (build {pc.Os.Build})");

        var now = DateTimeOffset.Now;
        var html = BenchmarkReport.Html(new BenchmarkReportInput(Program.Version, now, state.Game, state.Group, hardware,
            state.AppliedNames ?? [], result, state.Before.Select(run => run.Stats).ToArray(), state.After.Select(run => run.Stats).ToArray(),
            state.Before.LastOrDefault()?.Timeline, state.After.LastOrDefault()?.Timeline));
        var folder = Path.Combine(app.DataDirectory, "reports");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, BenchmarkReport.FileName(now));
        File.WriteAllText(path, html);
        return path;
    }

    /// <summary>Opens through explorer.exe so the browser starts as the normal user, not elevated like this app.</summary>
    public static void OpenReport(string path, IActionLog log)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
            {
                ArgumentList = { path }, UseShellExecute = false
            });
            log.Info($"Relatório aberto: {path}.");
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            AnsiConsole.MarkupLine($"[yellow]Não foi possível abrir o relatório: {Markup.Escape(error.Message)}[/]");
        }
    }

    public static string? LatestReport(string dataDirectory)
    {
        var folder = Path.Combine(dataDirectory, "reports");
        return Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "benchmark-*.html").OrderByDescending(file => file, StringComparer.Ordinal).FirstOrDefault()
            : null;
    }

    private static bool RebootedSince(DateTimeOffset? appliedAt) =>
        appliedAt is null || DateTimeOffset.Now - TimeSpan.FromMilliseconds(Environment.TickCount64) > appliedAt;

    private ProofState? Load()
    {
        if (!File.Exists(StatePath))
            return null;
        try
        {
            return JsonSerializer.Deserialize<ProofState>(File.ReadAllText(StatePath), ReportStore.Json);
        }
        catch (JsonException error)
        {
            app.Log.Warning($"Teste A/B salvo ilegível, descartado: {error.Message}");
            Discard();
            return null;
        }
    }

    private void Save(ProofState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(state, ReportStore.Json));
    }

    private void Discard() => File.Delete(StatePath);

    private static T Spin<T>(string message, Func<T> work)
    {
        T result = default!;
        AnsiConsole.Status().Spinner(Spinner.Known.Dots).SpinnerStyle(new Style(Purple)).Start(message, _ => result = work());
        return result;
    }
}
