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
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(Strings.T("terms.interactive"))}[/]");
            return false;
        }
        var grid = new Grid().AddColumn();
        foreach (var line in Terms.Text)
            grid.AddRow($"• {Markup.Escape(line)}");
        AnsiConsole.Write(new Panel(grid).Header($"[bold] {Markup.Escape(Strings.T("terms.title"))} [/]").Border(BoxBorder.Rounded).BorderColor(new Color(168, 85, 247)));
        if (!ConsoleUi.Confirm(Strings.T("terms.ask")))
        {
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(Strings.T("terms.declined"))}[/]");
            return false;
        }
        (settings with { TermsAccepted = Terms.Version }).Save(app.DataDirectory);
        app.Log.Info($"Termo de uso {Terms.Version} aceito.");
        return true;
    }

    /// <summary>F21: asked once, off by default, with the exact payload shown before any decision.</summary>
    public static void OfferCommunity(AppServices app)
    {
        var settings = app.Settings;
        if (settings.CommunityOrDefault.Prompted || Console.IsInputRedirected || CommunityClient.Configured(settings) is null)
            return;
        AnsiConsole.MarkupLine("[bold #C084FC]Base anônima de resultados (opcional)[/]");
        AnsiConsole.MarkupLine("Quando você usa \"Provar ajustes\", o resultado pode ser enviado de forma anônima. Em troca, cada ajuste mostra " +
            "quanto ganhou em PCs parecidos com o seu. Vai: modelo de CPU/GPU, RAM (GB e MT/s), build do Windows, ajustes testados e o resultado " +
            "relativo. [bold]Nunca[/]: nome, e-mail, número de série, IP, nome do PC ou lista de programas. Detalhes em docs/PRIVACIDADE.md.");
        if (ConsoleUi.Confirm("Ver exatamente o que seria enviado deste PC?"))
        {
            var sample = Community.Build(app.Pc, [new LocalEvidenceEntry("power-plan", "Energia", "Counter-Strike 2", Verdict.RealGain, 3.2, 4.1, 1.5, 6.8, DateTimeOffset.Now)], Program.Version);
            AnsiConsole.Write(new Panel(Markup.Escape(Community.ToJson(sample))).Header("[bold] exemplo do envio [/]").Border(BoxBorder.Rounded));
        }
        var enabled = ConsoleUi.Confirm("Participar da base anônima? (dá para mudar depois em Configurações)");
        (settings with { Community = settings.CommunityOrDefault with { Enabled = enabled, Prompted = true } }).Save(app.DataDirectory);
        app.Log.Info($"Base anônima: {(enabled ? "ativada" : "recusada")} pelo usuário.");
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
                (Strings.T("settings.gamePriority"), guardian.GamePriority == PriorityLevel.High ? Strings.T("settings.high") : Strings.T("settings.aboveNormal"),
                    s => s with { Guardian = guardian with { GamePriority = guardian.GamePriority == PriorityLevel.High ? PriorityLevel.AboveNormal : PriorityLevel.High } }),
                (Strings.T("settings.touchProtected"), OnOff(guardian.TouchProtectedGames), s => s with { Guardian = guardian with { TouchProtectedGames = !guardian.TouchProtectedGames } }),
                (Strings.T("settings.efficiency"), OnOff(guardian.EfficiencyMode), s => s with { Guardian = guardian with { EfficiencyMode = !guardian.EfficiencyMode } }),
                (Strings.T("settings.backgroundCpuSets"), OnOff(guardian.BackgroundCpuSets), s => s with { Guardian = guardian with { BackgroundCpuSets = !guardian.BackgroundCpuSets } }),
                (Strings.T("settings.gameCpuSets"), OnOff(guardian.GameCpuSets), s => s with { Guardian = guardian with { GameCpuSets = !guardian.GameCpuSets } }),
                (Strings.T("settings.obsCpuSets"), OnOff(guardian.ObsCpuSets), s => s with { Guardian = guardian with { ObsCpuSets = !guardian.ObsCpuSets } }),
                (Strings.T("settings.powerPlan"), OnOff(guardian.PowerPlanDuringGame), s => s with { Guardian = guardian with { PowerPlanDuringGame = !guardian.PowerPlanDuringGame } }),
                (Strings.T("settings.standby"), OnOff(guardian.StandbyPurge), s => s with { Guardian = guardian with { StandbyPurge = !guardian.StandbyPurge } }),
                (Strings.T("settings.wifi"), OnOff(guardian.WifiScanOff), s => s with { Guardian = guardian with { WifiScanOff = !guardian.WifiScanOff } }),
                (Strings.T("settings.browsers"), OnOff(cleanup.Browsers), s => s with { Cleanup = cleanup with { Browsers = !cleanup.Browsers } }),
                (Strings.T("settings.launchers"), OnOff(cleanup.Launchers), s => s with { Cleanup = cleanup with { Launchers = !cleanup.Launchers } }),
                (Strings.T("settings.updates"), OnOff(settings.UpdatesOrDefault.Check), s => s with { Updates = new UpdateSettings(!settings.UpdatesOrDefault.Check) }),
                (Strings.T("settings.community"), OnOff(settings.CommunityOrDefault.Enabled),
                    s => s with { Community = settings.CommunityOrDefault with { Enabled = !settings.CommunityOrDefault.Enabled, Prompted = true } }),
                (Strings.T("settings.errors"), OnOff(settings.ErrorReportsOrDefault.Enabled),
                    s => s with { ErrorReports = settings.ErrorReportsOrDefault with { Enabled = !settings.ErrorReportsOrDefault.Enabled } }),
                (Strings.T("settings.psu"), settings.PcOrDefault.PsuWatts is { } watts ? $"{watts} W {settings.PcOrDefault.PsuRating}" : Strings.T("settings.psuUnknown"),
                    s => s with { Pc = AskPsu() }),
                (Strings.T("settings.language"), settings.Language ?? "auto",
                    s => s with { Language = settings.Language switch { null or "auto" => Strings.Portuguese, Strings.Portuguese => Strings.English, _ => "auto" } })
            };

            var table = new Table().Border(TableBorder.Rounded).BorderColor(new Color(168, 85, 247)).AddColumn("#").AddColumn(Strings.T("settings.option")).AddColumn(Strings.T("settings.value"));
            for (var index = 0; index < options.Length; index++)
                table.AddRow((index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), Markup.Escape(options[index].Label), Markup.Escape(options[index].Value));
            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(Strings.T("settings.hint", Path.Combine(app.DataDirectory, AppSettings.FileName)))}[/]");
            var choice = ConsoleUi.ReadChoice();
            if (!int.TryParse(choice, out var number) || number < 1 || number > options.Length)
                return;
            try
            {
                var updated = options[number - 1].Toggle(settings);
                updated.Save(app.DataDirectory);
                Strings.Use(updated.Language);
                app.Log.Info($"Configuração alterada: {options[number - 1].Label}.");
                AnsiConsole.MarkupLine($"[green]{Markup.Escape(Strings.T("settings.saved"))}[/] [grey]{Markup.Escape(Strings.T("settings.savedHint"))}[/]");
            }
            catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(Strings.T("settings.saveFailed", error.Message))}[/]");
            }
        }
    }

    private static PcSettings AskPsu()
    {
        var watts = AnsiConsole.Prompt(new TextPrompt<int>(Markup.Escape(Strings.T("settings.psuWatts")))
            .Validate(value => value == 0 || value is >= 200 and <= 3000 ? ValidationResult.Success() : ValidationResult.Error(Strings.T("settings.psuRange"))));
        if (watts == 0)
            return new PcSettings();
        return new PcSettings(watts, AnsiConsole.Prompt(new SelectionPrompt<PsuRating>().Title(Markup.Escape(Strings.T("settings.psuRating"))).AddChoices(Enum.GetValues<PsuRating>())));
    }

    private static string OnOff(bool value) => value ? Strings.T("settings.on") : Strings.T("settings.off");
}
