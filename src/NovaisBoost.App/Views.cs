using System.Diagnostics;
using System.Globalization;
using NovaisBoost.Core;
using Spectre.Console;

namespace NovaisBoost.App;

/// <summary>Menu screens 3 to 9. Changes always go through ConsoleUi.ReviewAndApply (preview, confirmation, backup).</summary>
internal sealed class Views(AppServices app, ConsoleUi ui)
{
    private static readonly Color Purple = new(168, 85, 247);

    // ------------------------------------------------------------------ [3] Perfil do meu Hardware
    public void HardwareProfile()
    {
        var profiles = Spin("Identificando o hardware...", () => app.Profiles);
        if (profiles.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]Nenhum perfil específico para este hardware. A otimização universal já cobre este PC.[/]");
            return;
        }

        foreach (var group in profiles.GroupBy(profile => profile.Component))
        {
            var grid = new Grid().AddColumn();
            foreach (var profile in group)
            {
                grid.AddRow($"[bold #C084FC]{Markup.Escape(profile.Name)}[/]");
                foreach (var advice in profile.Recommendations)
                    grid.AddRow($"  • [bold]{Markup.Escape(advice.Title)}[/] [grey]— {Markup.Escape(advice.Detail)}[/]");
            }
            AnsiConsole.Write(new Panel(grid).Header($"[bold] {Markup.Escape(group.Key)} [/]").Border(BoxBorder.Rounded).BorderColor(Purple));
        }

        var bios = profiles.SelectMany(profile => profile.Bios).Distinct().ToArray();
        if (bios.Length > 0)
        {
            AnsiConsole.MarkupLine("[bold]Checklist de BIOS[/] [grey](o NOVAIS boost não altera a BIOS; confira manualmente)[/]");
            foreach (var item in bios)
                AnsiConsole.MarkupLine($"  [#C084FC]☐[/] {Markup.Escape(item)}");
        }
        foreach (var avoid in app.AvoidedTweaks)
            AnsiConsole.MarkupLine($"[yellow]O ajuste '{Markup.Escape(avoid)}' é bloqueado neste PC pelo perfil de hardware.[/]");

        var recommended = profiles.SelectMany(profile => profile.Tweaks).Distinct().Except(app.AvoidedTweaks).ToHashSet();
        var tweaks = app.AllTweaks.Where(tweak => recommended.Contains(tweak.Id)).ToArray();
        if (tweaks.Length > 0 && ConsoleUi.Confirm("Revisar os ajustes recomendados para este hardware?"))
            ui.ReviewAndApply(tweaks);
    }

    // ------------------------------------------------------------------ [4] Jogos & Streaming
    public void Games()
    {
        var games = Spin("Procurando jogos instalados...", () => app.Games);
        if (games.Count == 0)
            AnsiConsole.MarkupLine("[grey]Nenhum jogo conhecido encontrado. Abra o jogo e volte a esta tela para detectá-lo pelo processo.[/]");
        else
        {
            var table = new Table().Border(TableBorder.Rounded).BorderColor(Purple)
                .AddColumn("Jogo").AddColumn("Anti-cheat").AddColumn("Executável");
            foreach (var game in games)
                table.AddRow(Markup.Escape(game.Game.Name), Markup.Escape(game.Game.AntiCheat), $"[grey]{Markup.Escape(game.ExecutablePath)}[/]");
            AnsiConsole.Write(table);
        }

        foreach (var game in games.Select(item => item.Game).DistinctBy(game => game.Id))
        {
            AnsiConsole.MarkupLine($"[bold #C084FC]{Markup.Escape(game.Name)}[/]");
            if (game.LaunchOptions is { } launch)
                AnsiConsole.MarkupLine($"  Opções de inicialização: [bold]{Markup.Escape(launch)}[/]");
            foreach (var tip in game.Tips)
                AnsiConsole.MarkupLine($"  • {Markup.Escape(tip)}");
        }

        GamePresetsOffer(games);

        var gpu = app.Pc.Gpus.FirstOrDefault(item => !item.IsIntegrated) ?? app.Pc.Gpus.FirstOrDefault();
        if (gpu is not null)
            AnsiConsole.MarkupLine($"[bold]Streaming com {Markup.Escape(gpu.Name)}:[/] {Markup.Escape(EncoderAdvice(gpu))}");

        var tweaks = app.DynamicTweaks.Where(tweak => tweak.Id is "games-gpu-preference" or "games-qos").ToArray();
        if (tweaks.Length > 0 && ConsoleUi.Confirm("Revisar os ajustes para os jogos detectados (GPU dedicada e prioridade de rede)?"))
            ui.ReviewAndApply(tweaks);
    }

    /// <summary>15.1–15.3: competitive presets written into the game's own configuration file (game closed, full-file backup).</summary>
    private void GamePresetsOffer(IReadOnlyList<InstalledGame> games)
    {
        var found = GamePresetFiles.Find(games);
        if (found.Count == 0)
            return;
        AnsiConsole.MarkupLine($"[bold]Preset competitivo nos arquivos do jogo:[/] {Markup.Escape(string.Join(", ", found.Select(item => item.Preset.Name)))} " +
            "[grey](o jogo precisa estar fechado; o arquivo inteiro vai para o backup)[/]");
        if (!ConsoleUi.Confirm("Revisar os presets dos jogos?"))
            return;

        int? hz = app.Pc.Displays.Count > 0 ? app.Pc.Displays.Max(display => display.MaxHz) : null;
        bool? vrr = null;
        var tweaks = new List<TweakDefinition>();
        foreach (var (preset, files) in found)
        {
            int? cap = null;
            if (hz is > 0 && preset.Files.Any(file => file.Settings.Any(setting => setting.Value == GamePresets.FrameCapToken)))
            {
                vrr ??= ConsoleUi.Confirm($"Seu monitor ({hz} Hz) usa G-Sync ou FreeSync ligado?");
                cap = FrameCap.For(hz.Value, vrr.Value, preset.Reflex);
                AnsiConsole.MarkupLine(cap is { } value
                    ? $"[grey]{Markup.Escape(preset.Name)}: limite de FPS calculado em {value} (VRR{(preset.Reflex ? " + Reflex" : "")}, {hz} Hz).[/]"
                    : $"[grey]{Markup.Escape(preset.Name)}: sem VRR o limite de FPS do jogo não é alterado.[/]");
            }
            tweaks.AddRange(GamePresets.Build(preset, files, cap));
            if (preset.TestedVersion is null)
                AnsiConsole.MarkupLine($"[grey]{Markup.Escape(preset.Name)}: chaves conferidas em fontes públicas ({preset.LastVerified}), ainda não testadas numa instalação.[/]");
        }
        ui.ReviewAndApply(tweaks);
    }

    private static string EncoderAdvice(GpuInfo gpu) => gpu.Vendor switch
    {
        GpuVendor.Nvidia when System.Text.RegularExpressions.Regex.IsMatch(gpu.Name, @"RTX\s?(40|50)") =>
            "NVENC AV1 (YouTube) ou NVENC H.264 (Twitch), preset P5/P6, sem impacto perceptível no FPS.",
        GpuVendor.Nvidia => "NVENC H.264, preset P5. Evite x264 na CPU durante o jogo.",
        GpuVendor.Amd => "AMF AV1/HEVC nas RX 7000/9000; AMF H.264 nas mais antigas.",
        GpuVendor.Intel => "QuickSync AV1 (Arc) ou H.264.",
        _ => "Use o codificador da GPU, nunca x264, enquanto joga."
    };

    // ------------------------------------------------------------------ [5] Process Guardian
    public void Guardian()
    {
        var guardian = Core.Schedules.Find("guardian");
        var active = TaskSchedulerService.IsRegistered(guardian);
        AnsiConsole.MarkupLine($"Início automático com o Windows: {(active ? "[green]ligado[/]" : "[grey]desligado[/]")}");
        var priority = app.Settings.GuardianOrDefault.GamePriority;
        AnsiConsole.MarkupLine($"Prioridade do jogo: [bold]{(priority == PriorityLevel.High ? "Alta" : "Acima do normal")}[/] " +
            $"[grey](para mudar: \"guardian\": {{ \"gamePriority\": \"high\" }} em {Markup.Escape(Path.Combine(app.DataDirectory, AppSettings.FileName))})[/]");
        AnsiConsole.MarkupLine(ConsoleUi.Item(1, "Ver processos que gastam recursos à toa"));
        AnsiConsole.MarkupLine(ConsoleUi.Item(2, "Programas que abrem com o Windows"));
        AnsiConsole.MarkupLine(ConsoleUi.Item(3, "Rodar o Guardian agora nesta janela (Ctrl+C para parar)"));
        AnsiConsole.MarkupLine(ConsoleUi.Item(4, active ? "Desligar o início automático" : "Ligar o início automático"));
        AnsiConsole.MarkupLine(ConsoleUi.Item(0, "Voltar"));
        switch (ConsoleUi.ReadChoice())
        {
            case "1":
                Processes();
                break;
            case "2":
                Startup();
                break;
            case "3":
                using (var cancel = new CancellationTokenSource())
                {
                    ConsoleCancelEventHandler stop = (_, args) => { args.Cancel = true; cancel.Cancel(); };
                    Console.CancelKeyPress += stop;
                    try
                    {
                        new GuardianService(app.GuardianPolicy(), app.Log, echo: true).Run(cancel.Token);
                    }
                    finally
                    {
                        Console.CancelKeyPress -= stop;
                    }
                }
                break;
            case "4":
                if (active)
                    TaskSchedulerService.Remove(guardian);
                else
                    TaskSchedulerService.Register(guardian);
                AnsiConsole.MarkupLine("[green]Feito.[/]");
                break;
        }
    }

    private void Processes()
    {
        var catalog = new ProcessCatalog(DataCatalog.Processes());
        var rows = Process.GetProcesses().Select(process =>
            {
                using (process)
                {
                    try
                    {
                        return (process.ProcessName, Memory: process.WorkingSet64, Entry: catalog.Find(process.ProcessName));
                    }
                    catch (InvalidOperationException)
                    {
                        return (process.ProcessName, Memory: 0L, Entry: (ProcessEntry?)null);
                    }
                }
            })
            .Where(row => row.Entry is { Category: ProcessCategory.Bloat or ProcessCategory.Optional or ProcessCategory.Launcher or ProcessCategory.VirtualMachine })
            .GroupBy(row => row.Entry!.Label)
            .Select(group => (Label: group.Key, Entry: group.First().Entry!, Names: group.Select(row => row.ProcessName).Distinct().ToArray(),
                Count: group.Count(), Memory: group.Sum(row => row.Memory)))
            .OrderByDescending(row => row.Memory).ToArray();

        if (rows.Length == 0)
        {
            AnsiConsole.MarkupLine("[green]Nenhum processo desnecessário conhecido está rodando.[/]");
            return;
        }
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Purple)
            .AddColumn("#").AddColumn("Programa").AddColumn("Tipo").AddColumn(new TableColumn("RAM").RightAligned()).AddColumn("Observação");
        for (var index = 0; index < rows.Length; index++)
            table.AddRow((index + 1).ToString(CultureInfo.InvariantCulture), Markup.Escape(rows[index].Label), Category(rows[index].Entry.Category),
                Core.Diagnostics.Gb(rows[index].Memory), $"[grey]{Markup.Escape(rows[index].Entry.Description)}[/]");
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine("[grey]Durante o jogo, o Guardian baixa a prioridade destes programas automaticamente.[/]");

        AnsiConsole.Write("Números para FECHAR agora (Enter para nenhum): ");
        var close = ParseNumbers(Console.ReadLine(), rows.Length);
        if (close.Count == 0 || !ConsoleUi.Confirm($"Fechar {close.Count} programa(s)? Trabalho não salvo neles será perdido."))
            return;
        foreach (var name in close.SelectMany(index => rows[index].Names))
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    app.Log.Warning($"Não foi possível fechar {name}: {error.Message}");
                }
            }
        }
        app.Log.Info($"Processos fechados pelo usuário: {string.Join(", ", close.Select(index => rows[index].Label))}.");
        AnsiConsole.MarkupLine("[green]Programas fechados.[/]");
    }

    private void Startup()
    {
        var catalog = new ProcessCatalog(DataCatalog.Processes());
        var entries = StartupApps.List().Where(entry => entry.Enabled).ToArray();
        if (entries.Length == 0)
        {
            AnsiConsole.MarkupLine("[green]Nenhum programa ativo na inicialização.[/]");
            return;
        }
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Purple).AddColumn("#").AddColumn("Programa").AddColumn("Origem").AddColumn("Avaliação");
        for (var index = 0; index < entries.Length; index++)
        {
            var exe = System.Text.RegularExpressions.Regex.Match(entries[index].Command, @"([^\\/""]+)\.exe", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var entry = exe.Success ? catalog.Find(exe.Groups[1].Value) : null;
            table.AddRow((index + 1).ToString(CultureInfo.InvariantCulture), Markup.Escape(entries[index].Name), Markup.Escape(entries[index].Source),
                entry is null ? "[grey]desconhecido[/]" : $"{Category(entry.Category)} [grey]{Markup.Escape(entry.Label)}[/]");
        }
        AnsiConsole.Write(table);
        AnsiConsole.Write("Números para DESATIVAR na inicialização (Enter para nenhum): ");
        var chosen = ParseNumbers(Console.ReadLine(), entries.Length);
        if (chosen.Count > 0)
            ui.ReviewAndApply([StartupApps.DisableTweak(chosen.Select(index => entries[index]).ToArray())]);
    }

    private static string Category(ProcessCategory category) => category switch
    {
        ProcessCategory.Bloat => "[red]desnecessário[/]",
        ProcessCategory.Launcher => "[yellow]launcher[/]",
        ProcessCategory.VirtualMachine => "[yellow]máquina virtual[/]",
        ProcessCategory.Optional => "[blue]opcional[/]",
        ProcessCategory.Communication => "[green]comunicação[/]",
        ProcessCategory.Streaming => "[green]streaming[/]",
        _ => "[grey]sistema[/]"
    };

    // ------------------------------------------------------------------ [6] Cronogramas
    public void Schedules()
    {
        var registered = Core.Schedules.All.ToDictionary(schedule => schedule.Id, TaskSchedulerService.IsRegistered);
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Purple).AddColumn("#").AddColumn("Tarefa").AddColumn("Quando").AddColumn("Status");
        for (var index = 0; index < Core.Schedules.All.Count; index++)
        {
            var schedule = Core.Schedules.All[index];
            table.AddRow((index + 1).ToString(CultureInfo.InvariantCulture),
                $"[bold]{Markup.Escape(schedule.Name)}[/]\n[grey]{Markup.Escape(schedule.Description)}[/]", When(schedule),
                registered[schedule.Id] ? "[green]ativa[/]" : "[grey]desligada[/]");
        }
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"{ConsoleUi.Item("A", "Ativar todas")}   {ConsoleUi.Item("R", "Remover todas")}   {ConsoleUi.Item("L", "Limpar agora")}   [grey]ou o número para ligar/desligar[/]   {ConsoleUi.Item(0, "Voltar")}");
        var choice = ConsoleUi.ReadChoice()?.ToUpperInvariant();
        switch (choice)
        {
            case "A":
                foreach (var schedule in Core.Schedules.All.Where(schedule => !registered[schedule.Id]))
                    TaskSchedulerService.Register(schedule);
                AnsiConsole.MarkupLine("[green]Todas as tarefas foram ativadas.[/]");
                break;
            case "R":
                foreach (var schedule in Core.Schedules.All.Where(schedule => registered[schedule.Id]))
                    TaskSchedulerService.Remove(schedule);
                AnsiConsole.MarkupLine("[green]Tarefas removidas.[/]");
                break;
            case "L":
                CleanNow();
                break;
            default:
                if (int.TryParse(choice, out var number) && number >= 1 && number <= Core.Schedules.All.Count)
                {
                    var schedule = Core.Schedules.All[number - 1];
                    if (registered[schedule.Id])
                        TaskSchedulerService.Remove(schedule);
                    else
                        TaskSchedulerService.Register(schedule);
                    AnsiConsole.MarkupLine("[green]Feito.[/]");
                }
                break;
        }
    }

    private static string When(ScheduleDefinition schedule) => schedule.Trigger switch
    {
        ScheduleTrigger.Logon => "ao entrar no Windows",
        ScheduleTrigger.Daily => $"todo dia às {schedule.Time:HH\\:mm}",
        ScheduleTrigger.Weekly => $"{CultureInfo.GetCultureInfo("pt-BR").DateTimeFormat.GetDayName(schedule.Day ?? DayOfWeek.Friday)} às {schedule.Time:HH\\:mm}",
        _ => $"dia {schedule.DayOfMonth} às {schedule.Time:HH\\:mm}"
    };

    private void CleanNow()
    {
        var targets = CleanupTargets.All.Where(target => target.Kind == CleanupKind.Regular).ToArray();
        var preview = Spin("Calculando...", () => Cleaner.Run(targets, dryRun: true, app.Log));
        var table = new Table().Border(TableBorder.Simple).AddColumn("Local").AddColumn(new TableColumn("Arquivos").RightAligned()).AddColumn(new TableColumn("Tamanho").RightAligned());
        foreach (var result in preview.Where(result => result.Files > 0))
            table.AddRow(Markup.Escape(result.Target.Name), result.Files.ToString(CultureInfo.InvariantCulture), Core.Diagnostics.Gb(result.Bytes));
        AnsiConsole.Write(table);
        var total = preview.Sum(result => result.Bytes);
        if (total == 0 || !ConsoleUi.Confirm($"Apagar {Core.Diagnostics.Gb(total)} de arquivos temporários?"))
            return;
        var done = Spin("Limpando...", () => Cleaner.Run(targets, dryRun: false, app.Log));
        AnsiConsole.MarkupLine($"[green]{Core.Diagnostics.Gb(done.Sum(result => result.Bytes))} liberados.[/] [grey]{done.Sum(result => result.Skipped)} arquivo(s) em uso foram mantidos.[/]");
    }

    // ------------------------------------------------------------------ [7] Benchmark
    public void Benchmark()
    {
        AnsiConsole.MarkupLine(ConsoleUi.Item(1, "Provar ajustes: teste A/B com estatística (antes × depois de um grupo)"));
        AnsiConsole.MarkupLine(ConsoleUi.Item(2, "Latência de DPC/ISR por driver (µs, ETW) — 20 segundos"));
        AnsiConsole.MarkupLine(ConsoleUi.Item(3, "Carga de DPC/ISR (%) e resolução do timer — 20 segundos"));
        AnsiConsole.MarkupLine(ConsoleUi.Item(4, "Capturar FPS de um jogo (uma rodada)"));
        AnsiConsole.MarkupLine(ConsoleUi.Item(5, "Comparar capturas avulsas antes × depois"));
        AnsiConsole.MarkupLine(ConsoleUi.Item(6, "Abrir o último relatório"));
        AnsiConsole.MarkupLine(ConsoleUi.Item(0, "Voltar"));
        switch (ConsoleUi.ReadChoice())
        {
            case "1":
                new ProofView(app, ui).Run();
                break;
            case "2":
                KernelLatency();
                break;
            case "3":
                var sample = Spin("Medindo a carga de DPC/ISR por 20 segundos (deixe o PC parado)...", () => LatencyProbe.Measure(TimeSpan.FromSeconds(20)));
                DrawLatency(sample);
                break;
            case "4":
                CaptureFps();
                break;
            case "5":
                Compare();
                break;
            case "6":
                if (ProofView.LatestReport(app.DataDirectory) is { } report)
                    ProofView.OpenReport(report, app.Log);
                else
                    AnsiConsole.MarkupLine("[grey]Nenhum relatório ainda. Rode \"Provar ajustes\" para gerar um.[/]");
                break;
        }
    }

    private void KernelLatency()
    {
        AnsiConsole.MarkupLine("Grava as chamadas de DPC/ISR de todos os drivers por 20 segundos (somente leitura). " +
            "Para achar o culpado de travadas, deixe tocando um vídeo ou o jogo aberto durante a gravação.");
        if (!ConsoleUi.Confirm("Começar agora?"))
            return;
        var accumulator = new KernelLatencyAccumulator();
        var duration = TimeSpan.FromSeconds(20);
        Spin("Gravando DPC/ISR por driver (20 s)...", () =>
        {
            new EtwKernelTrace(app.Log).Record(duration, accumulator.Add);
            return 0;
        });
        DrawKernelLatency(accumulator.Build(duration));
    }

    private static void DrawKernelLatency(KernelLatencyReport report)
    {
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Purple).Title("[bold]Drivers com a DPC/ISR mais longa[/]")
            .AddColumn("Driver").AddColumn(new TableColumn("Máx. DPC (µs)").RightAligned()).AddColumn(new TableColumn("Máx. ISR (µs)").RightAligned())
            .AddColumn(new TableColumn("Total (ms)").RightAligned()).AddColumn(new TableColumn("Chamadas").RightAligned());
        foreach (var driver in report.ByMax)
        {
            var color = KernelLatencyAnalysis.Classify(driver.MaxMicroseconds) switch { Severity.Critical => "red", Severity.Warning => "yellow", _ => "green" };
            table.AddRow($"[{color}]{Markup.Escape(driver.Module)}[/]", driver.MaxDpcMicroseconds.ToString("0", CultureInfo.CurrentCulture),
                driver.MaxIsrMicroseconds.ToString("0", CultureInfo.CurrentCulture), (driver.TotalMicroseconds / 1000).ToString("0.0", CultureInfo.CurrentCulture),
                (driver.DpcCount + driver.IsrCount).ToString("N0", CultureInfo.CurrentCulture));
        }
        AnsiConsole.Write(table);
        var heaviest = report.ByTotal.Take(3).Select(driver => $"{driver.Module} ({driver.TotalMicroseconds / 1000:0.0} ms)");
        AnsiConsole.MarkupLine($"[grey]{report.Events:N0} chamadas em {report.Duration.TotalSeconds:0} s. Mais tempo no total: {Markup.Escape(string.Join(", ", heaviest))}. " +
            $"Alerta acima de {KernelLatencyAnalysis.WarningMicroseconds:0} µs, crítico acima de {KernelLatencyAnalysis.CriticalMicroseconds:0} µs.[/]");
        if (report.Findings.Count == 0)
            AnsiConsole.MarkupLine("[green]Nenhum driver passou de 500 µs: latência de DPC/ISR saudável.[/]");
        foreach (var finding in report.Findings)
            AnsiConsole.MarkupLine($"[{(finding.Severity == Severity.Critical ? "red" : "yellow")}]{Markup.Escape(finding.Title)}[/] — {Markup.Escape(finding.Recommendation)}");
    }

    private static void DrawLatency(LatencySample sample)
    {
        var grid = new Grid().AddColumn().AddColumn();
        grid.AddRow("DPC, % da CPU (média / pico)", $"{sample.DpcPercent:0.00}% / {sample.DpcPeakPercent:0.00}%");
        grid.AddRow("Interrupções (ISR), % da CPU (média / pico)", $"{sample.InterruptPercent:0.00}% / {sample.InterruptPeakPercent:0.00}%");
        grid.AddRow("DPCs por segundo", sample.DpcPerSecond.ToString("N0", CultureInfo.GetCultureInfo("pt-BR")));
        grid.AddRow("Interrupções por segundo", sample.InterruptsPerSecond.ToString("N0", CultureInfo.GetCultureInfo("pt-BR")));
        grid.AddRow("Resolução do timer", $"{sample.TimerResolutionMs:0.###} ms");
        AnsiConsole.Write(new Panel(grid).Header("[bold] Carga de DPC/ISR (%) [/]").BorderColor(Purple));
        AnsiConsole.MarkupLine("[grey]Mostra a parte da CPU gasta em DPC/ISR. O tempo de cada driver em µs está na opção 2.[/]");
        var findings = LatencyRules.Evaluate(sample);
        if (findings.Count == 0)
            AnsiConsole.MarkupLine("[green]Carga de DPC/ISR saudável.[/]");
        foreach (var finding in findings)
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(finding.Title)}[/] — {Markup.Escape(finding.Recommendation)}");
    }

    internal static void PresentMonMissing(AppServices app) =>
        AnsiConsole.MarkupLine($"[yellow]PresentMon não encontrado.[/] O pacote do NOVAIS boost traz o PresentMon {PresentMonTool.Version} na pasta " +
            $"[bold]tools[/] ao lado do exe; reinstale o pacote completo. Também vale copiar o PresentMon (console) para " +
            $"[bold]{Markup.Escape(Path.Combine(app.DataDirectory, "tools"))}[/].");

    /// <summary>Game from the detected list, or any executable name typed by the user.</summary>
    internal static (string Exe, string Name)? PickGame(AppServices app)
    {
        var games = app.Games.Where(game => game.Game.Id != "obs").ToArray();
        for (var index = 0; index < games.Length; index++)
            AnsiConsole.MarkupLine(ConsoleUi.Item(index + 1, games[index].Game.Name));
        AnsiConsole.Write("Número do jogo ou nome do executável (ex.: cs2.exe): ");
        var input = Console.ReadLine()?.Trim() ?? "";
        var (exe, name) = int.TryParse(input, out var number) && number >= 1 && number <= games.Length
            ? (Path.GetFileName(games[number - 1].ExecutablePath), games[number - 1].Game.Name)
            : (input, input);
        return exe.Length == 0 ? null : (exe, name);
    }

    private void CaptureFps()
    {
        var presentMon = NovaisBoost.App.Benchmark.FindPresentMon(app.DataDirectory, app.Log);
        if (presentMon is null)
        {
            PresentMonMissing(app);
            return;
        }
        if (PickGame(app) is not var (exe, name))
            return;
        AnsiConsole.Write("Rótulo [[antes/depois]] (padrão: antes): ");
        var label = Console.ReadLine()?.Trim().ToLowerInvariant() is { Length: > 0 } text ? text : "antes";
        AnsiConsole.MarkupLine("Abra o jogo e entre numa partida/cena repetível. A captura dura 60 segundos.");
        if (!ConsoleUi.Confirm("Começar a captura agora?"))
            return;
        var run = Spin("Capturando quadros...", () => NovaisBoost.App.Benchmark.Capture(presentMon, exe, name, label, 60, app));
        DrawRun(run);
    }

    internal static void DrawRun(BenchmarkRun run)
    {
        var stats = run.Stats;
        AnsiConsole.MarkupLine($"[bold #C084FC]{Markup.Escape(run.Game)}[/] ({Markup.Escape(run.Label)}): [bold]{stats.AverageFps:0} FPS[/] · " +
            $"1% low {stats.OnePercentLowFps:0} · 0,1% low {stats.PointOnePercentLowFps:0} · frametime p99 {stats.P99FrameTimeMs:0.0} ms · {stats.Stutters} travada(s)");
        if (run.Timeline is { Length: > 1 } timeline)
            AnsiConsole.MarkupLine($"  [#C084FC]{FrameAnalysis.Sparkline(timeline, Math.Clamp(AnsiConsole.Profile.Width - 4, 20, 100))}[/]");
        var details = new List<string>();
        if (stats.Bottleneck != Bottleneck.Unknown)
            details.Add($"gargalo: {BenchmarkReport.Describe(stats.Bottleneck)}");
        if (stats.GpuBusyPercent is { } gpu)
            details.Add($"GPU ocupada {gpu:0}% do quadro");
        if (stats.AveragePcLatencyMs is { } pcLatency)
            details.Add($"latência do PC {pcLatency:0.0} ms");
        if (stats.AverageClickToPhotonMs is { } click)
            details.Add($"clique até a tela {click:0.0} ms");
        if (details.Count > 0)
            AnsiConsole.MarkupLine($"  [grey]{Markup.Escape(string.Join(" · ", details))}[/]");
    }

    private void Compare()
    {
        var history = NovaisBoost.App.Benchmark.History(app.DataDirectory);
        var pairs = history.GroupBy(run => run.Game).Select(group => (
                Before: group.LastOrDefault(run => run.Label == "antes"), After: group.LastOrDefault(run => run.Label == "depois")))
            .Where(pair => pair.Before is not null && pair.After is not null).ToArray();
        if (pairs.Length == 0)
        {
            AnsiConsole.MarkupLine("[grey]Faça uma captura 'antes' e outra 'depois' do mesmo jogo para comparar.[/]");
            return;
        }
        foreach (var (before, after) in pairs)
        {
            var table = new Table().Title($"[bold]{Markup.Escape(before!.Game)}[/]").Border(TableBorder.Rounded).BorderColor(Purple)
                .AddColumn("Métrica").AddColumn(new TableColumn("Antes").RightAligned()).AddColumn(new TableColumn("Depois").RightAligned()).AddColumn(new TableColumn("Diferença").RightAligned());
            void Row(string metric, double a, double b, bool higherIsBetter, string format)
            {
                var change = FrameAnalysis.ChangePercent(a, b);
                var good = higherIsBetter ? change >= 0 : change <= 0;
                table.AddRow(metric, a.ToString(format, CultureInfo.InvariantCulture), b.ToString(format, CultureInfo.InvariantCulture),
                    $"[{(good ? "green" : "red")}]{change:+0.0;-0.0}%[/]");
            }
            Row("FPS médio", before.Stats.AverageFps, after!.Stats.AverageFps, true, "0");
            Row("1% low", before.Stats.OnePercentLowFps, after.Stats.OnePercentLowFps, true, "0");
            Row("0,1% low", before.Stats.PointOnePercentLowFps, after.Stats.PointOnePercentLowFps, true, "0");
            Row("Frametime p99 (ms)", before.Stats.P99FrameTimeMs, after.Stats.P99FrameTimeMs, false, "0.0");
            Row("Travadas", before.Stats.Stutters, after.Stats.Stutters, false, "0");
            AnsiConsole.Write(table);
        }
    }

    // ------------------------------------------------------------------ [9] Rede
    public void Network()
    {
        AnsiConsole.MarkupLine(ConsoleUi.Item(1, "Teste de ping, jitter, perda e bufferbloat (~30 s, usa a internet)"));
        AnsiConsole.MarkupLine(ConsoleUi.Item(2, "Achar o DNS mais rápido"));
        AnsiConsole.MarkupLine(ConsoleUi.Item(3, "Ajustes da placa de rede e QoS"));
        AnsiConsole.MarkupLine(ConsoleUi.Item(0, "Voltar"));
        switch (ConsoleUi.ReadChoice())
        {
            case "1":
                PingTest();
                break;
            case "2":
                Dns();
                break;
            case "3":
                var tweaks = app.DynamicTweaks.Where(tweak => tweak.Id is "nic-power" or "nic-interrupt-moderation" or "games-qos").ToArray();
                if (tweaks.Length == 0)
                    AnsiConsole.MarkupLine("[green]A placa de rede já está ajustada.[/]");
                else
                    ui.ReviewAndApply(tweaks);
                break;
        }
    }

    private void PingTest()
    {
        var adapter = NetworkTools.ActiveAdapter();
        var gateway = adapter is null ? null : NetworkTools.Gateway(adapter);
        var router = gateway is null ? null : Spin("Medindo o roteador...", () => NetworkTools.Ping(gateway.ToString(), 20, 100));
        var (idle, loaded, mbps) = Spin("Medindo internet parada e sob carga...", NetworkTools.Bufferbloat);
        var grade = NetworkAnalysis.BufferbloatGrade(idle.AverageMs, loaded.AverageMs);

        var table = new Table().Border(TableBorder.Rounded).BorderColor(Purple).AddColumn("Destino").AddColumn(new TableColumn("Ping").RightAligned())
            .AddColumn(new TableColumn("Jitter").RightAligned()).AddColumn(new TableColumn("Perda").RightAligned());
        if (router is not null)
            table.AddRow($"Roteador ({gateway})", $"{router.AverageMs:0.0} ms", $"{router.JitterMs:0.0} ms", $"{router.LossPercent:0.#}%");
        table.AddRow("Internet (1.1.1.1)", $"{idle.AverageMs:0.0} ms", $"{idle.JitterMs:0.0} ms", $"{idle.LossPercent:0.#}%");
        table.AddRow("Internet sob download", $"{loaded.AverageMs:0.0} ms", $"{loaded.JitterMs:0.0} ms", $"{loaded.LossPercent:0.#}%");
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"Bufferbloat: [bold]{grade}[/] (+{Math.Max(0, loaded.AverageMs - idle.AverageMs):0} ms sob carga) · download medido: {mbps:0} Mbps");
        if (adapter?.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211)
            AnsiConsole.MarkupLine("[yellow]Você está no Wi-Fi. Para jogar competitivo, use cabo.[/]");
        foreach (var finding in NetworkAnalysis.Evaluate(idle, grade).Concat(router is null ? [] : NetworkAnalysis.Evaluate(router, null)))
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(finding.Title)}[/] — {Markup.Escape(finding.Recommendation)}");
    }

    private void Dns()
    {
        var adapter = NetworkTools.ActiveAdapter();
        var results = Spin("Testando servidores DNS...", () => NetworkTools.BenchmarkDns(adapter));
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Purple).AddColumn("Servidor").AddColumn("Endereço")
            .AddColumn(new TableColumn("Tempo (mediana)").RightAligned()).AddColumn(new TableColumn("Falhas").RightAligned());
        foreach (var result in results)
            table.AddRow(Markup.Escape(result.Name), result.Server, double.IsInfinity(result.MedianMs) ? "-" : $"{result.MedianMs:0.0} ms", result.Failures.ToString(CultureInfo.InvariantCulture));
        AnsiConsole.Write(table);

        var best = results.FirstOrDefault();
        var current = results.FirstOrDefault(result => result.Name == "Atual");
        var resolver = NetworkTools.PublicResolvers.FirstOrDefault(item => item.Primary == best?.Server);
        if (adapter is null || best is null || resolver.Primary is null || best.Failures > 0 ||
            current is not null && current.MedianMs - best.MedianMs < 5)
        {
            AnsiConsole.MarkupLine("[green]Seu DNS atual já está entre os mais rápidos.[/]");
            return;
        }
        if (!Guid.TryParse(adapter.Id, out var id))
            return;
        ui.ReviewAndApply([new TweakDefinition("dns-fastest", $"Usar o DNS {resolver.Name}",
            $"Troca o DNS de {adapter.Name} para {resolver.Primary} e {resolver.Secondary}, o mais rápido no teste.",
            "Rede", TweakLevel.Safe, TweakEvidence.Documented, "Nenhum. Reverter volta ao DNS anterior (automático ou manual).", false, new TweakCompatibility([], []),
            [new DnsAction(id, adapter.Name, [resolver.Primary, resolver.Secondary])])]);
    }

    // ------------------------------------------------------------------ helpers
    private static T Spin<T>(string message, Func<T> work)
    {
        T result = default!;
        AnsiConsole.Status().Spinner(Spinner.Known.Dots).SpinnerStyle(new Style(Purple)).Start(message, _ => result = work());
        return result;
    }

    private static IReadOnlyList<int> ParseNumbers(string? text, int count) =>
        (text ?? "").Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(item => int.TryParse(item, out var number) ? number - 1 : -1)
            .Where(index => index >= 0 && index < count).Distinct().ToArray();
}
