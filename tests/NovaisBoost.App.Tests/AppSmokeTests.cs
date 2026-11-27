using NovaisBoost.App;
using NovaisBoost.Core;
using Spectre.Console;
using Spectre.Console.Testing;

namespace NovaisBoost.App.Tests;

/// <summary>Skipped outside Windows: these tests touch Windows APIs (identity, registry, WMI).</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Só no Windows (roda na CI windows-latest).";
    }
}

[Collection("Console")]
public sealed class AppSmokeTests
{
    private static string Render(Action draw)
    {
        var console = new TestConsole().Width(140);
        var previous = AnsiConsole.Console;
        AnsiConsole.Console = console;
        try
        {
            draw();
            return console.Output;
        }
        finally
        {
            AnsiConsole.Console = previous;
        }
    }

    [Fact]
    public void PreviewTableShowsEveryChange()
    {
        var output = Render(() => ConsoleUi.DrawPreview([new SettingChange(@"HKCU\Software\X [Valor]", "não definido", "Dword 1")]));
        Assert.Contains("Valor", output);
        Assert.Contains("não definido", output);
        Assert.Contains("Dword 1", output);
    }

    [Fact]
    public void MenuItemsAreEscaped()
    {
        Assert.Equal("[bold #C084FC][[11]][/] Configurações", ConsoleUi.Item(11, "Configurações"));
        Assert.Contains("[[x]]", ConsoleUi.Item("A", "[x]"));
    }

    [Fact]
    public void BannerRendersTheBrand()
    {
        var output = Render(ConsoleUi.DrawBanner);
        Assert.Contains("███", output);
        Assert.Contains("v", output);
    }

    [WindowsFact]
    public void BootGuardTaskRunsAsSystemAtStartup()
    {
        var xml = TaskSchedulerService.BuildXml(Schedules.BootGuard).ToString();
        Assert.Contains("BootTrigger", xml);
        Assert.Contains("S-1-5-18", xml);
        Assert.Contains("--tarefa protecao-boot", xml);
        var logon = TaskSchedulerService.BuildXml(Schedules.BootConfirm).ToString();
        Assert.Contains("LogonTrigger", logon);
        Assert.Contains("PT2H", logon);
    }

    [WindowsFact]
    public void GuardianTaskNeverTimesOut()
    {
        var xml = TaskSchedulerService.BuildXml(Schedules.Find("guardian")).ToString();
        Assert.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>", xml);
        Assert.Contains("conhost.exe", xml);
    }
}
