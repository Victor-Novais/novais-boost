using System.Diagnostics;
using System.Security.Principal;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NovaisBoost.App;
using NovaisBoost.Core;
using static NovaisBoost.Core.Strings;

namespace NovaisBoost.Gui;

/// <summary>One-click flow (F22): Diagnose → Recommended for your PC → Review → Apply → Prove.</summary>
internal sealed class MainWindow : Window
{
    private static readonly IBrush Purple = new SolidColorBrush(Color.Parse("#A855F7"));
    private static readonly IBrush Lilac = new SolidColorBrush(Color.Parse("#C084FC"));
    private static readonly IBrush Grey = new SolidColorBrush(Color.Parse("#9CA3AF"));
    private static readonly IBrush Night = new SolidColorBrush(Color.Parse("#0F0A1A"));

    private readonly bool _isAdmin;
    private readonly AppServices _app;
    private readonly StackPanel _content = new() { Spacing = 8, Margin = new Thickness(24, 8, 24, 24) };
    private readonly TextBlock _status = new() { Foreground = Grey, Margin = new Thickness(24, 0, 24, 12), TextWrapping = TextWrapping.Wrap };
    private readonly List<(CheckBox Box, TweakDefinition Tweak, IReadOnlyList<SettingChange> Changes)> _recommended = [];
    private DiagnosticReport? _report;

    public MainWindow()
    {
        Title = T("gui.title");
        Width = 1040;
        Height = 760;
        MinWidth = 760;
        Background = Night;

        using var identity = WindowsIdentity.GetCurrent();
        _isAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        var owner = identity.User?.Value ?? throw new InvalidOperationException(identity.Name);
        var directory = _isAdmin ? AppStorage.Open() : Path.Combine(Path.GetTempPath(), "NOVAIS boost preview");
        IActionLog log = _isAdmin ? new DailyLog(directory) : new NullLog();
        _app = AppServices.Create(directory, log, owner);

        var steps = new WrapPanel { Margin = new Thickness(24, 0, 24, 8) };
        steps.Children.Add(Step(T("gui.diagnose"), DiagnoseAsync));
        steps.Children.Add(Step(T("gui.recommended"), RecommendAsync));
        steps.Children.Add(Step(T("gui.review"), () => { Review(); return Task.CompletedTask; }));
        steps.Children.Add(Step(T("gui.apply"), ApplyAsync));
        steps.Children.Add(Step(T("gui.prove"), () => { Prove(); return Task.CompletedTask; }));
        steps.Children.Add(Step(T("gui.revertAll"), RevertAllAsync));

        var layout = new DockPanel();
        var header = Header();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(steps, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        layout.Children.Add(header);
        layout.Children.Add(steps);
        layout.Children.Add(_status);
        layout.Children.Add(new ScrollViewer { Content = _content });
        Content = layout;
    }

    private static Control Header()
    {
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        title.Children.Add(new TextBlock { Text = "NOVAIS", FontSize = 40, FontWeight = FontWeight.Black, Foreground = Purple });
        title.Children.Add(new TextBlock { Text = "boost", FontSize = 40, FontWeight = FontWeight.Black, Foreground = Brushes.White });
        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 12) };
        panel.Children.Add(title);
        panel.Children.Add(new TextBlock { Text = T("gui.subtitle"), Foreground = Grey });
        return panel;
    }

    private Button Step(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 8), BorderBrush = Purple };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                await action();
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                _app.Log.Error(error.ToString());
                _status.Text = T("gui.error", error.Message);
            }
            finally
            {
                button.IsEnabled = true;
            }
        };
        return button;
    }

    private async Task DiagnoseAsync()
    {
        _status.Text = T("gui.analyzing");
        _app.Refresh();
        _report = await Task.Run(() => Core.Diagnostics.Evaluate(_app.Pc, DateOnly.FromDateTime(DateTime.Now), _app.Settings.PcOrDefault));
        _content.Children.Clear();
        _content.Children.Add(new TextBlock { Text = T("gui.score", _report.Score), FontSize = 28, FontWeight = FontWeight.Bold, Foreground = ScoreBrush(_report.Score) });
        _content.Children.Add(new TextBlock { Text = T("gui.findings", _report.Findings.Count), Foreground = Grey });
        if (_report.Findings.Count == 0)
            _content.Children.Add(new TextBlock { Text = T("gui.noFindings") });
        foreach (var finding in _report.Findings)
            _content.Children.Add(Card(finding.Title, $"{finding.Detail}\n→ {finding.Recommendation}", SeverityBrush(finding.Severity)));
        _status.Text = "";
    }

    private async Task RecommendAsync()
    {
        _status.Text = T("gui.loadingTweaks");
        var found = await Task.Run(() =>
        {
            var avoided = _app.AvoidedTweaks;
            return TweakLevels.Select(_app.AllTweaks, TweakLevel.Competitive, _app.LocalEvidence, _app.CommunityEvidence)
                .Where(tweak => !avoided.Contains(tweak.Id) && _app.Engine.CheckCompatibility(tweak) is null)
                .Select(tweak => (Tweak: tweak, Changes: Safe(() => _app.Engine.Detect(tweak))))
                .Where(item => item.Changes.Count > 0).ToArray();
        });
        _recommended.Clear();
        _content.Children.Clear();
        if (found.Length == 0)
            _content.Children.Add(new TextBlock { Text = T("gui.nothingToDo") });
        foreach (var (tweak, changes) in found.OrderBy(item => item.Tweak.Level).ThenBy(item => item.Tweak.Category, StringComparer.CurrentCulture))
        {
            var box = new CheckBox { IsChecked = true, Content = Card(tweak.Name, $"{tweak.Category} · {T("gui.level", (int)tweak.Level + 1)}\n{tweak.Description}" +
                (_app.CommunityEvidence.GetValueOrDefault(tweak.Id) is { } shared ? $"\n{Community.Describe(shared)}" : ""), Lilac) };
            box.IsCheckedChanged += (_, _) => UpdateSelection();
            _recommended.Add((box, tweak, changes));
            _content.Children.Add(box);
        }
        UpdateSelection();
    }

    private void UpdateSelection() => _status.Text = T("gui.selected", _recommended.Count(item => item.Box.IsChecked == true));

    private void Review()
    {
        if (_recommended.Count == 0)
        {
            _status.Text = T("gui.diagnoseFirst");
            return;
        }
        _content.Children.Clear();
        _content.Children.Add(new TextBlock { Text = T("gui.changes"), FontWeight = FontWeight.Bold });
        foreach (var (_, tweak, changes) in _recommended.Where(item => item.Box.IsChecked == true))
            _content.Children.Add(Card(tweak.Name, string.Join("\n", changes.Select(change => $"{change.Target}: {change.Current} → {change.Expected}")), Purple));
    }

    private async Task ApplyAsync()
    {
        var selected = _recommended.Where(item => item.Box.IsChecked == true).ToArray();
        if (selected.Length == 0)
        {
            _status.Text = T("gui.diagnoseFirst");
            return;
        }
        if (!_isAdmin)
        {
            if (await Ask(T("gui.needsAdmin")))
                Relaunch();
            return;
        }
        if (!await Ask(T("gui.confirmApply")))
            return;
        var reviewed = selected.ToDictionary(item => item.Tweak.Id, item => item.Changes);
        var results = await Task.Run(() => _app.Engine.ApplyBatch(selected.Select(item => item.Tweak).ToArray(), reviewed));
        var failed = results.Count(result => result.Error is not null);
        _status.Text = T("gui.applied", results.Count - failed, failed);
        _content.Children.Clear();
        foreach (var result in results)
            _content.Children.Add(Card(result.Tweak.Name, result.Error ?? string.Join("\n", result.Changes.Select(change => $"{change.Target} → {change.Expected}")),
                result.Error is null ? Brushes.LimeGreen : Brushes.OrangeRed));
        if (results.Any(result => result.Error is null && result.Tweak.RequiresReboot))
        {
            BootProtection.Arm(_app, _app.Owner);
            _content.Children.Insert(0, new TextBlock { Text = T("gui.reboot"), Foreground = Brushes.Gold });
        }
        _app.Refresh();
        _recommended.Clear();
    }

    private void Prove()
    {
        _content.Children.Clear();
        _content.Children.Add(new TextBlock { Text = T("gui.proveHint"), TextWrapping = TextWrapping.Wrap });
        var open = new Button { Content = T("gui.openTerminal"), Margin = new Thickness(0, 8) };
        open.Click += (_, _) => OpenTerminal();
        _content.Children.Add(open);
    }

    private async Task RevertAllAsync()
    {
        if (!_isAdmin)
        {
            if (await Ask(T("gui.needsAdmin")))
                Relaunch();
            return;
        }
        var changes = _app.Engine.RevertAll();
        _content.Children.Clear();
        foreach (var change in changes)
            _content.Children.Add(new TextBlock { Text = $"{change.Target}: {change.Current} → {change.Expected}" });
        if (changes.Count == 0 || !await Ask(T("gui.revertAll") + "?"))
            return;
        await Task.Run(() => _app.Engine.RevertAll(dryRun: false, reviewedChanges: changes));
        _app.Refresh();
        _status.Text = T("gui.reverted");
    }

    private static IReadOnlyList<SettingChange> Safe(Func<IReadOnlyList<SettingChange>> detect)
    {
        try
        {
            return detect();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return [];
        }
    }

    private static void Relaunch()
    {
        if (Environment.ProcessPath is not { } exe)
            return;
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            Environment.Exit(0);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // UAC cancelled: stay read-only.
        }
    }

    /// <summary>"Prove tweaks" lives in the terminal app next to this exe.</summary>
    private static void OpenTerminal()
    {
        var folder = AppContext.BaseDirectory;
        var terminal = new[] { "NOVAIS boost.exe", "NovaisBoost.App.exe" }.Select(name => Path.Combine(folder, name)).FirstOrDefault(File.Exists);
        if (terminal is not null)
            Process.Start(new ProcessStartInfo(terminal) { UseShellExecute = true });
    }

    private async Task<bool> Ask(string question)
    {
        var answer = false;
        var dialog = new Window { Width = 460, SizeToContent = SizeToContent.Height, Title = T("gui.title"), Background = Night,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        var yes = new Button { Content = T("gui.yes"), Margin = new Thickness(0, 0, 8, 0) };
        var no = new Button { Content = T("gui.no") };
        yes.Click += (_, _) => { answer = true; dialog.Close(); };
        no.Click += (_, _) => dialog.Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(yes);
        buttons.Children.Add(no);
        var body = new StackPanel { Margin = new Thickness(20) };
        body.Children.Add(new TextBlock { Text = question, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(buttons);
        dialog.Content = body;
        await dialog.ShowDialog(this);
        return answer;
    }

    private static Border Card(string title, string detail, IBrush accent)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, Foreground = accent, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = detail, Foreground = Grey, TextWrapping = TextWrapping.Wrap });
        return new Border { Child = panel, Padding = new Thickness(12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            BorderBrush = accent, Background = new SolidColorBrush(Color.Parse("#1A1229")) };
    }

    private static IBrush SeverityBrush(Severity severity) => severity switch
    {
        Severity.Critical => Brushes.OrangeRed,
        Severity.Warning => Brushes.Gold,
        _ => Lilac
    };

    private static IBrush ScoreBrush(int score) => score >= 80 ? Brushes.LimeGreen : score >= 60 ? Brushes.Gold : Brushes.OrangeRed;
}
