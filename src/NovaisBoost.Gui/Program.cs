using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using NovaisBoost.Core;

namespace NovaisBoost.Gui;

/// <summary>
/// F22: graphical interface over the same Core and Windows adapters as the terminal. The terminal app stays for enthusiasts,
/// scheduled tasks and the Guardian. Without administrator it diagnoses and simulates; applying asks for elevation.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Strings.Use(args.SkipWhile(arg => arg != "--idioma").Skip(1).FirstOrDefault());
        return AppBuilder.Configure<GuiApp>().UseWin32().UseSkia().StartWithClassicDesktopLifetime(args);
    }
}

internal sealed class GuiApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
