using System.Diagnostics;
using Microsoft.Win32;
using NovaisBoost.Core;
using Spectre.Console;

namespace NovaisBoost.App;

/// <summary>Background loop: applies GuardianPolicy while a game runs and restores every priority when it closes.</summary>
internal sealed class GuardianService(GuardianPolicy policy, IActionLog log, bool echo)
{
    private readonly Dictionary<int, (string Name, ProcessPriorityClass Original)> _changed = [];
    private readonly Dictionary<int, TimeSpan> _cpu = [];
    private DateTime _lastSample = DateTime.UtcNow;
    private bool _gameRunning;

    public void Run(CancellationToken cancel)
    {
        using var mutex = new Mutex(true, @"Global\NOVAISboostGuardian", out var owner);
        if (!owner)
        {
            Say("O Process Guardian já está rodando.");
            return;
        }
        Say("Process Guardian ativo. Aguardando um jogo abrir...");
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                Tick();
                cancel.WaitHandle.WaitOne(2000);
            }
        }
        finally
        {
            RestoreAll();
            Say("Process Guardian encerrado. Prioridades restauradas.");
        }
    }

    private void Tick()
    {
        var samples = Sample();
        var gameRunning = samples.Any(sample => policy.IsGame(sample.Name));
        if (!gameRunning)
        {
            if (_gameRunning)
            {
                RestoreAll();
                Say("Jogo fechado. Prioridades restauradas.");
            }
            _gameRunning = false;
            return;
        }
        if (!_gameRunning)
            Say($"Jogo detectado: {samples.First(sample => policy.IsGame(sample.Name)).Name}.");
        _gameRunning = true;

        foreach (var change in policy.Plan(samples))
        {
            try
            {
                using var process = Process.GetProcessById(change.Id);
                _changed.TryAdd(change.Id, (change.Name, process.PriorityClass));
                process.PriorityClass = ToClass(change.To);
                Say($"{change.Name}: {change.From} → {change.To} ({change.Reason}).");
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Process exited or is protected; nothing to do.
            }
        }
    }

    private List<ProcessSample> Sample()
    {
        var now = DateTime.UtcNow;
        var elapsed = Math.Max((now - _lastSample).TotalMilliseconds, 1) * Environment.ProcessorCount;
        _lastSample = now;
        var samples = new List<ProcessSample>();
        var seen = new HashSet<int>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var cpu = process.TotalProcessorTime;
                    var percent = _cpu.TryGetValue(process.Id, out var previous) ? (cpu - previous).TotalMilliseconds / elapsed * 100 : 0;
                    _cpu[process.Id] = cpu;
                    seen.Add(process.Id);
                    samples.Add(new ProcessSample(process.Id, process.ProcessName, ToLevel(process.PriorityClass), percent));
                }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    // Access denied (protected/system process) or already exited.
                }
            }
        }
        foreach (var gone in _cpu.Keys.Where(id => !seen.Contains(id)).ToArray())
            _cpu.Remove(gone);
        return samples;
    }

    private void RestoreAll()
    {
        foreach (var (id, (name, original)) in _changed)
        {
            try
            {
                using var process = Process.GetProcessById(id);
                if (string.Equals(process.ProcessName, name, StringComparison.OrdinalIgnoreCase))
                    process.PriorityClass = original;
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Exited meanwhile.
            }
        }
        _changed.Clear();
    }

    private void Say(string message)
    {
        log.Info($"Guardian: {message}");
        if (echo)
            AnsiConsole.MarkupLine($"[grey]{DateTime.Now:HH:mm:ss}[/] {Markup.Escape(message)}");
    }

    private static PriorityLevel ToLevel(ProcessPriorityClass priority) => priority switch
    {
        ProcessPriorityClass.Idle => PriorityLevel.Idle,
        ProcessPriorityClass.BelowNormal => PriorityLevel.BelowNormal,
        ProcessPriorityClass.AboveNormal => PriorityLevel.AboveNormal,
        ProcessPriorityClass.High or ProcessPriorityClass.RealTime => PriorityLevel.High,
        _ => PriorityLevel.Normal
    };

    private static ProcessPriorityClass ToClass(PriorityLevel level) => level switch
    {
        PriorityLevel.Idle => ProcessPriorityClass.Idle,
        PriorityLevel.BelowNormal => ProcessPriorityClass.BelowNormal,
        PriorityLevel.AboveNormal => ProcessPriorityClass.AboveNormal,
        PriorityLevel.High => ProcessPriorityClass.High,
        _ => ProcessPriorityClass.Normal
    };
}

internal sealed record StartupEntry(string Name, string Command, string Source, RegistryHiveName Hive, string ApprovedKey, bool Enabled);

/// <summary>Programs started with Windows, read the same way Task Manager does (Run keys, Startup folders, StartupApproved).</summary>
internal static class StartupApps
{
    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public static IReadOnlyList<StartupEntry> List()
    {
        var entries = new List<StartupEntry>();
        void FromRun(RegistryKey root, RegistryHiveName hive, string path, string approved)
        {
            using var key = root.OpenSubKey(path);
            if (key is null)
                return;
            foreach (var name in key.GetValueNames().Where(name => name.Length > 0))
                entries.Add(new StartupEntry(name, key.GetValue(name)?.ToString() ?? "", hive == RegistryHiveName.CurrentUser ? "Usuário" : "Todos os usuários",
                    hive, Approved + approved, IsEnabled(root, Approved + approved, name)));
        }

        FromRun(Registry.CurrentUser, RegistryHiveName.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "Run");
        FromRun(Registry.LocalMachine, RegistryHiveName.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "Run");
        FromRun(Registry.LocalMachine, RegistryHiveName.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Run32");

        foreach (var (folder, hive, root) in new[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.Startup), RegistryHiveName.CurrentUser, Registry.CurrentUser),
            (Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), RegistryHiveName.LocalMachine, Registry.LocalMachine)
        })
        {
            if (!Directory.Exists(folder))
                continue;
            foreach (var file in Directory.EnumerateFiles(folder).Where(file => !file.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)))
                entries.Add(new StartupEntry(Path.GetFileName(file), file, "Pasta Inicializar", hive, Approved + "StartupFolder",
                    IsEnabled(root, Approved + "StartupFolder", Path.GetFileName(file))));
        }
        return entries;
    }

    /// <summary>The same "disabled" marker Task Manager writes: first byte 3 in a 12-byte value.</summary>
    public static TweakDefinition DisableTweak(IReadOnlyList<StartupEntry> entries) => new(
        "startup-disable", "Desativar programas na inicialização",
        $"Impede que {string.Join(", ", entries.Select(entry => entry.Name))} abram junto com o Windows (igual ao Gerenciador de Tarefas).",
        "Inicialização", TweakLevel.Safe, "Nenhum. Os programas continuam instalados.", false, new TweakCompatibility([], []),
        entries.Select(entry => (TweakAction)new RegistryAction(entry.Hive, entry.ApprovedKey, entry.Name, RegistryOperation.Set,
            RegistryValue.From(RegistryValueType.Binary, new[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }))).ToArray());

    private static bool IsEnabled(RegistryKey root, string path, string name)
    {
        using var key = root.OpenSubKey(path);
        return key?.GetValue(name) is not byte[] { Length: > 0 } data || (data[0] & 1) == 0;
    }
}
