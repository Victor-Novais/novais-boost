using NovaisBoost.Core;
using Spectre.Console;

namespace NovaisBoost.App;

/// <summary>20.9 terms of use and 20.3 update offer, shown before the menu.</summary>
internal static class FirstRun
{
    public static bool EnsureTermsAccepted(AppServices app)
    {
        var settings = app.Settings;
        if (Terms.Accepted(settings))
            return true;
        if (Console.IsInputRedirected)
        {
            AnsiConsole.MarkupLine("[yellow]Abra o NOVAIS boost uma vez de forma interativa para aceitar o termo de uso.[/]");
            return false;
        }
        var grid = new Grid().AddColumn();
        foreach (var line in Terms.Text)
            grid.AddRow($"• {Markup.Escape(line)}");
        AnsiConsole.Write(new Panel(grid).Header("[bold] Termo de uso [/]").Border(BoxBorder.Rounded).BorderColor(new Color(168, 85, 247)));
        if (!ConsoleUi.Confirm("Você leu e aceita?"))
        {
            AnsiConsole.MarkupLine("[grey]Sem o aceite, só os modos somente leitura funcionam: --diagnostico, --simular e --reparar.[/]");
            return false;
        }
        (settings with { TermsAccepted = Terms.Version }).Save(app.DataDirectory);
        app.Log.Info($"Termo de uso {Terms.Version} aceito.");
        return true;
    }

    public static void OfferUpdate(AppServices app)
    {
        if (!app.Settings.UpdatesOrDefault.Check || Console.IsInputRedirected || Updater.Check(app.Log) is not { } manifest)
            return;
        AnsiConsole.MarkupLine($"[bold #C084FC]Nova versão disponível: {Markup.Escape(manifest.Version)}[/] [grey](você usa {Program.Version})[/]");
        if (manifest.Notes is { Length: > 0 } notes)
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(notes)}[/]");
        if (!ConsoleUi.Confirm("Baixar e instalar agora? (confere SHA256 e assinatura digital antes de trocar)"))
            return;
        try
        {
            Updater.Install(manifest, app.Log);
            AnsiConsole.MarkupLine("[green]Atualizado. Feche e abra o NOVAIS boost de novo. Para voltar: --voltar-versao.[/]");
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException
                                          or System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            app.Log.Warning($"Atualização recusada: {error.Message}");
            AnsiConsole.MarkupLine($"[yellow]Atualização não instalada: {Markup.Escape(error.Message)}[/]");
        }
    }
}

/// <summary>20.6: non-interactive modes for automated VM tests. They still create the restore point and the backup.</summary>
internal static class CommandLine
{
    public static int? TryRun(string[] args, AppServices app)
    {
        bool Has(string flag) => args.Contains(flag, StringComparer.OrdinalIgnoreCase);
        var apply = Array.FindIndex(args, arg => string.Equals(arg, "--aplicar", StringComparison.OrdinalIgnoreCase));
        if (apply < 0 && !Has("--reverter-tudo"))
            return null;
        if (!Has("--confirmar"))
        {
            AnsiConsole.MarkupLine("[yellow]Use --confirmar para alterar sem perguntas, ou --simular para só ver o que mudaria.[/]");
            return 2;
        }
        if (Has("--reverter-tudo"))
        {
            var changes = app.Engine.RevertAll(dryRun: false);
            app.Log.Info($"--reverter-tudo: {changes.Count} valor(es) restaurado(s).");
            AnsiConsole.MarkupLine($"[green]{changes.Count} valor(es) restaurado(s).[/]");
            return 0;
        }
        if (apply + 1 >= args.Length || !int.TryParse(args[apply + 1], out var number) || number is < 1 or > 3)
        {
            AnsiConsole.MarkupLine("[yellow]--aplicar precisa do nível: 1, 2 ou 3.[/]");
            return 2;
        }
        var level = (TweakLevel)(number - 1);
        var avoided = app.AvoidedTweaks;
        var reviewed = new Dictionary<string, IReadOnlyList<SettingChange>>(StringComparer.Ordinal);
        var batch = new List<TweakDefinition>();
        foreach (var tweak in TweakLevels.Select(app.AllTweaks, level, app.LocalEvidence)
                     .Where(tweak => !avoided.Contains(tweak.Id) && app.Engine.CheckCompatibility(tweak) is null))
        {
            var changes = app.Engine.Detect(tweak);
            if (changes.Count == 0)
                continue;
            reviewed[tweak.Id] = changes;
            batch.Add(tweak);
        }
        var results = app.Engine.ApplyBatch(batch, reviewed);
        var failed = results.Where(result => result.Error is not null).ToArray();
        foreach (var failure in failed)
            AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(failure.Tweak.Id)}: {Markup.Escape(failure.Error!)}[/]");
        AnsiConsole.MarkupLine($"[green]{results.Count - failed.Length} ajuste(s) aplicados[/], {failed.Length} falharam.");
        return failed.Length == 0 ? 0 : 3;
    }
}

/// <summary>20.10: [11] Configurações — the settings.json options, validated and saved atomically.</summary>
internal sealed class SettingsView(AppServices app)
{
    public void Run()
    {
        while (true)
        {
            var settings = app.Settings;
            var guardian = settings.GuardianOrDefault;
            var cleanup = settings.CleanupOrDefault;
            var options = new (string Label, string Value, Func<AppSettings, AppSettings> Toggle)[]
            {
                ("Prioridade do jogo no Guardian", guardian.GamePriority == PriorityLevel.High ? "Alta" : "Acima do normal",
                    s => s with { Guardian = guardian with { GamePriority = guardian.GamePriority == PriorityLevel.High ? PriorityLevel.AboveNormal : PriorityLevel.High } }),
                ("Mexer em jogos com anti-cheat de kernel", OnOff(guardian.TouchProtectedGames), s => s with { Guardian = guardian with { TouchProtectedGames = !guardian.TouchProtectedGames } }),
                ("Modo de eficiência no segundo plano", OnOff(guardian.EfficiencyMode), s => s with { Guardian = guardian with { EfficiencyMode = !guardian.EfficiencyMode } }),
                ("Segundo plano nos E-cores / CCD sem V-Cache", OnOff(guardian.BackgroundCpuSets), s => s with { Guardian = guardian with { BackgroundCpuSets = !guardian.BackgroundCpuSets } }),
                ("Jogo fixo nos P-cores / V-Cache (experimental)", OnOff(guardian.GameCpuSets), s => s with { Guardian = guardian with { GameCpuSets = !guardian.GameCpuSets } }),
                ("OBS nos núcleos de segundo plano", OnOff(guardian.ObsCpuSets), s => s with { Guardian = guardian with { ObsCpuSets = !guardian.ObsCpuSets } }),
                ("Plano de energia só durante o jogo", OnOff(guardian.PowerPlanDuringGame), s => s with { Guardian = guardian with { PowerPlanDuringGame = !guardian.PowerPlanDuringGame } }),
                ("Limpar a lista standby com pouca RAM", OnOff(guardian.StandbyPurge), s => s with { Guardian = guardian with { StandbyPurge = !guardian.StandbyPurge } }),
                ("Pausar a varredura Wi-Fi durante o jogo", OnOff(guardian.WifiScanOff), s => s with { Guardian = guardian with { WifiScanOff = !guardian.WifiScanOff } }),
                ("Limpar cache dos navegadores", OnOff(cleanup.Browsers), s => s with { Cleanup = cleanup with { Browsers = !cleanup.Browsers } }),
                ("Limpar cache dos launchers", OnOff(cleanup.Launchers), s => s with { Cleanup = cleanup with { Launchers = !cleanup.Launchers } }),
                ("Procurar atualizações ao abrir", OnOff(settings.UpdatesOrDefault.Check), s => s with { Updates = new UpdateSettings(!settings.UpdatesOrDefault.Check) }),
                ("Relatório de erros (só exceção e versão)", OnOff(settings.ErrorReportsOrDefault.Enabled),
                    s => s with { ErrorReports = settings.ErrorReportsOrDefault with { Enabled = !settings.ErrorReportsOrDefault.Enabled } }),
                ("Fonte (PSU)", settings.PcOrDefault.PsuWatts is { } watts ? $"{watts} W {settings.PcOrDefault.PsuRating}" : "não informada",
                    s => s with { Pc = AskPsu() })
            };

            var table = new Table().Border(TableBorder.Rounded).BorderColor(new Color(168, 85, 247)).AddColumn("#").AddColumn("Opção").AddColumn("Valor");
            for (var index = 0; index < options.Length; index++)
                table.AddRow((index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), Markup.Escape(options[index].Label), Markup.Escape(options[index].Value));
            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"[grey]Número para mudar · {ConsoleUi.Item(0, "Voltar")} · arquivo: {Markup.Escape(Path.Combine(app.DataDirectory, AppSettings.FileName))}[/]");
            var choice = ConsoleUi.ReadChoice();
            if (!int.TryParse(choice, out var number) || number < 1 || number > options.Length)
                return;
            try
            {
                options[number - 1].Toggle(settings).Save(app.DataDirectory);
                app.Log.Info($"Configuração alterada: {options[number - 1].Label}.");
                AnsiConsole.MarkupLine("[green]Salvo.[/] [grey]O Guardian usa os valores novos na próxima vez que iniciar.[/]");
            }
            catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                AnsiConsole.MarkupLine($"[red]Não foi possível salvar: {Markup.Escape(error.Message)}[/]");
            }
        }
    }

    private static PcSettings AskPsu()
    {
        var watts = AnsiConsole.Prompt(new TextPrompt<int>("Potência da fonte em watts (0 para apagar):")
            .Validate(value => value == 0 || value is >= 200 and <= 3000 ? ValidationResult.Success() : ValidationResult.Error("Entre 200 e 3000 W.")));
        if (watts == 0)
            return new PcSettings();
        return new PcSettings(watts, AnsiConsole.Prompt(new SelectionPrompt<PsuRating>().Title("Certificação 80 Plus:").AddChoices(Enum.GetValues<PsuRating>())));
    }

    private static string OnOff(bool value) => value ? "ligado" : "desligado";
}
