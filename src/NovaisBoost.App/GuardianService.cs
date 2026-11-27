using System.Diagnostics;
using Microsoft.Win32;
using NovaisBoost.Core;
using Spectre.Console;

namespace NovaisBoost.App;

/// <summary>
/// Background loop (F17): waits for process start events (no polling while idle), applies GuardianPolicy while a game runs
/// and restores everything it changed when the game closes or the Guardian stops.
/// </summary>
internal sealed class GuardianService(GuardianPolicy policy, IActionLog log, bool echo, GuardianEnvironment? environment = null)
{
    private static readonly TimeSpan ActiveTick = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan IdleFallback = TimeSpan.FromSeconds(15);

    private readonly GuardianEnvironment _environment = environment ?? new GuardianEnvironment();
    private readonly Dictionary<int, (string Name, ProcessPriorityClass Original)> _changed = [];
    private readonly Dictionary<int, string> _efficiency = [];
    private readonly Dictionary<int, string> _cpuSets = [];
    private readonly Dictionary<int, TimeSpan> _cpu = [];
    private readonly LeakTracker _leaks = new();
    private DateTime _lastSample = DateTime.UtcNow;
    private DateTimeOffset? _lastPurge;
    private DateTimeOffset _lastAlert = DateTimeOffset.MinValue;
    private Guid? _previousPlan;
    private bool _gameRunning;

    public void Run(CancellationToken cancel)
    {
        using var mutex = new Mutex(true, @"Global\NOVAISboostGuardian", out var owner);
        if (!owner)
        {
            Say("O Process Guardian já está rodando.");
            return;
        }
        using var starts = ProcessStartEvents.TryCreate(log);
        Say(starts is null ? "Process Guardian ativo (verificação a cada 15 s). Aguardando um jogo abrir..."
            : "Process Guardian ativo. Aguardando um jogo abrir...");
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                Tick();
                if (_gameRunning)
                    cancel.WaitHandle.WaitOne(ActiveTick);
                else
                    // Idle: sleep until a process starts (event), with a slow fallback when the WMI event is unavailable.
                    WaitHandle.WaitAny([cancel.WaitHandle, starts?.Signal ?? new ManualResetEvent(false)], IdleFallback);
                starts?.Signal.Reset();
            }
        }
        finally
        {
            RestoreAll();
            Say("Process Guardian encerrado. Tudo restaurado.");
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
                Say("Jogo fechado. Prioridades, modo eficiência, núcleos e plano de energia restaurados.");
            }
            _gameRunning = false;
            return;
        }
        if (!_gameRunning)
        {
            var game = samples.First(sample => policy.IsGame(sample.Name)).Name;
            Say(policy.MayTouchGame(game) ? $"Jogo detectado: {game}."
                : $"Jogo detectado: {game} (anti-cheat protegido: o processo do jogo não é alterado).");
            SwitchPowerPlan(gameStarted: true);
            if (policy.Settings.WifiScanOff && WifiScan.Pause(log) > 0)
                Say("Varredura de redes Wi-Fi em segundo plano pausada durante o jogo.");
        }
        _gameRunning = true;

        foreach (var change in policy.Plan(samples))
            WithProcess(change.Id, process =>
            {
                _changed.TryAdd(change.Id, (change.Name, process.PriorityClass));
                process.PriorityClass = ToClass(change.To);
                Say($"{change.Name}: {change.From} → {change.To} ({change.Reason}).");
            });

        var names = samples.ToDictionary(sample => sample.Id, sample => sample.Name);
        foreach (var id in policy.EfficiencyTargets(samples).Where(id => !_efficiency.ContainsKey(id)))
            if (ProcessControl.SetEfficiencyMode(id, true))
                _efficiency[id] = names[id];

        var background = _environment.Topology?.BackgroundSets() ?? [];
        if (background.Count > 0)
        {
            foreach (var id in policy.CpuSetTargets(samples).Where(id => !_cpuSets.ContainsKey(id)))
                if (ProcessControl.SetCpuSets(id, background))
                    _cpuSets[id] = names[id];
            foreach (var id in policy.StreamingCpuSetTargets(samples).Where(id => !_cpuSets.ContainsKey(id)))
                if (ProcessControl.SetCpuSets(id, background))
                {
                    _cpuSets[id] = names[id];
                    Say($"{names[id]}: transmissão nos núcleos de segundo plano, separada do jogo.");
                }
        }
        var gameSets = _environment.Topology?.GameSets() ?? [];
        if (gameSets.Count > 0)
            foreach (var id in policy.GameCpuSetTargets(samples).Where(id => !_cpuSets.ContainsKey(id)))
                if (ProcessControl.SetCpuSets(id, gameSets))
                {
                    _cpuSets[id] = names[id];
                    Say($"{names[id]}: fixado nos núcleos do jogo (experimental).");
                }

        Memory();
    }

    private void Memory()
    {
        var now = DateTimeOffset.Now;
        var memory = _environment.ReadMemory();
        if (memory is { } stats && GuardianRules.ShouldPurgeStandby(policy.Settings, stats.FreeBytes, stats.StandbyBytes, _lastPurge, now))
        {
            _lastPurge = now;
            Say(ProcessControl.PurgeStandbyList()
                ? $"Lista standby limpa (livre {Core.Diagnostics.Gb(stats.FreeBytes)}, standby {Core.Diagnostics.Gb(stats.StandbyBytes)})."
                : "Não foi possível limpar a lista standby.");
        }
        if (now - _lastAlert < TimeSpan.FromMinutes(5))
            return;
        var alerts = GuardianRules.Alerts(memory?.UsedPercent, _environment.ReadVramPercent()).ToList();
        alerts.AddRange(_leaks.Record(now, Sample(includeCpu: false))
            .Select(name => $"{name} está aumentando o uso de memória há 30 minutos sem parar (possível vazamento). Reinicie o programa."));
        foreach (var alert in alerts)
        {
            _lastAlert = now;
            log.Warning($"Guardian: alerta: {alert}");
            Notifier.Show("NOVAIS boost · Guardian", alert, log);
            if (echo)
                AnsiConsole.MarkupLine($"[bold yellow]⚠ {Markup.Escape(alert)}[/]");
        }
    }

    private void SwitchPowerPlan(bool gameStarted)
    {
        if (gameStarted)
        {
            if (!GuardianRules.UseGamePowerPlan(policy.Settings, true, _environment.DualCcdX3d, PowerPlans.NovaisPlanInstalled()))
                return;
            _previousPlan = PowerPlans.Active();
            if (_previousPlan != PowerPlans.Novais && PowerPlans.Activate(PowerPlans.Novais))
                Say("Plano de energia NOVAIS boost ativado durante o jogo.");
            else
                _previousPlan = null;
        }
        else if (_previousPlan is { } previous)
        {
            PowerPlans.Activate(previous);
            _previousPlan = null;
            Say("Plano de energia anterior restaurado.");
        }
    }

    private List<ProcessSample> Sample(bool includeCpu = true)
    {
        var now = DateTime.UtcNow;
        var elapsed = Math.Max((now - _lastSample).TotalMilliseconds, 1) * Environment.ProcessorCount;
        if (includeCpu)
            _lastSample = now;
        var samples = new List<ProcessSample>();
        var seen = new HashSet<int>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var percent = 0d;
                    if (includeCpu)
                    {
                        var cpu = process.TotalProcessorTime;
                        percent = _cpu.TryGetValue(process.Id, out var previous) ? (cpu - previous).TotalMilliseconds / elapsed * 100 : 0;
                        _cpu[process.Id] = cpu;
                        seen.Add(process.Id);
                    }
                    samples.Add(new ProcessSample(process.Id, process.ProcessName, ToLevel(process.PriorityClass), percent, process.WorkingSet64));
                }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    // Access denied (protected/system process) or already exited.
                }
            }
        }
        if (includeCpu)
            foreach (var gone in _cpu.Keys.Where(id => !seen.Contains(id)).ToArray())
                _cpu.Remove(gone);
        return samples;
    }

    private void RestoreAll()
    {
        foreach (var (id, (name, original)) in _changed)
            WithProcess(id, process =>
            {
                if (string.Equals(process.ProcessName, name, StringComparison.OrdinalIgnoreCase))
                    process.PriorityClass = original;
            });
        foreach (var id in _efficiency.Keys)
            ProcessControl.SetEfficiencyMode(id, false);
        foreach (var id in _cpuSets.Keys)
            ProcessControl.SetCpuSets(id, []);
        _changed.Clear();
        _efficiency.Clear();
        _cpuSets.Clear();
        SwitchPowerPlan(gameStarted: false);
        WifiScan.Resume(log);
    }

    private static void WithProcess(int id, Action<Process> action)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            action(process);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Exited meanwhile or protected; nothing to do.
        }
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
        "Inicialização", TweakLevel.Safe, TweakEvidence.Documented, "Nenhum. Os programas continuam instalados.", false, new TweakCompatibility([], []),
        entries.Select(entry => (TweakAction)new RegistryAction(entry.Hive, entry.ApprovedKey, entry.Name, RegistryOperation.Set,
            RegistryValue.From(RegistryValueType.Binary, new[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }))).ToArray());

    private static bool IsEnabled(RegistryKey root, string path, string name)
    {
        using var key = root.OpenSubKey(path);
        return key?.GetValue(name) is not byte[] { Length: > 0 } data || (data[0] & 1) == 0;
    }
}
