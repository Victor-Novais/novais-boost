namespace NovaisBoost.Core;

public sealed record SettingChange(string Target, string Current, string Expected);

public sealed record BatchResult(TweakDefinition Tweak, IReadOnlyList<SettingChange> Changes, string? Error);

/// <param name="compatibility">Returns null when the hardware requirements are met, or the reason they are not.</param>
public sealed class TweakEngine(
    IRegistry registry, IServiceController services, IPowerConfig power,
    SnapshotManager snapshots, IActionLog log,
    IScheduledTasks? tasks = null, IPowerPlans? plans = null,
    Func<TweakCompatibility, string?>? compatibility = null,
    INvidiaSettings? nvidia = null, IDnsSettings? dns = null)
{
    private RestorePoint? _batchPoint;
    private bool _inBatch;

    /// <summary>
    /// Applies reviewed tweaks one by one under a single restore point. A failed tweak is rolled back
    /// and reported; the others continue. Stops only when recovery itself fails or protection is off.
    /// </summary>
    public IReadOnlyList<BatchResult> ApplyBatch(IReadOnlyList<TweakDefinition> batch,
        IReadOnlyDictionary<string, IReadOnlyList<SettingChange>> reviewed, Action<TweakDefinition>? progress = null)
    {
        var results = new List<BatchResult>();
        _inBatch = true;
        try
        {
            foreach (var tweak in batch)
            {
                progress?.Invoke(tweak);
                try
                {
                    var changes = Apply(tweak, dryRun: false,
                        reviewed.TryGetValue(tweak.Id, out var preview) ? preview : throw new InvalidOperationException("Ajuste não revisado."));
                    results.Add(new BatchResult(tweak, changes, null));
                }
                catch (Exception error) when (error is not (AggregateException or SystemProtectionDisabledException or OutOfMemoryException))
                {
                    log.Warning($"Lote: {tweak.Id} não aplicado: {error.Message}");
                    results.Add(new BatchResult(tweak, [], error.Message));
                }
            }
        }
        finally
        {
            _inBatch = false;
            _batchPoint = null;
        }
        return results;
    }

    /// <summary>Null when the tweak can run on this PC; otherwise the reason it cannot.</summary>
    /// <remarks>"compat.antiCheat" does not block: it becomes a warning at review (<see cref="Compatibility.AntiCheatWarning"/>).</remarks>
    public string? CheckCompatibility(TweakDefinition tweak)
    {
        if (tweak.Compat.Hardware.Length == 0)
            return null;
        return compatibility is null ? "a verificação de hardware não está disponível" : compatibility(tweak.Compat);
    }
    public IReadOnlyList<SettingChange> Detect(TweakDefinition tweak)
    {
        var changes = Describe(Plan(tweak));
        log.Info($"Detecção de {tweak.Id}: {changes.Length} alteração(ões).");
        return changes;
    }

    public IReadOnlyList<SettingChange> Apply(TweakDefinition tweak, bool dryRun = true,
        IReadOnlyList<SettingChange>? reviewedChanges = null)
    {
        if (dryRun)
        {
            log.Info($"Simulação de aplicação: {tweak.Id}.");
            return Detect(tweak);
        }

        using var lease = snapshots.Acquire();
        var active = snapshots.ReadActive();
        if (active?.State.Pending == true)
            throw new InvalidOperationException("Há um ajuste incompleto. Use Reverter tudo antes de aplicar outro ajuste.");
        if (CheckCompatibility(tweak) is { } reason)
            throw new InvalidOperationException($"Ajuste incompatível com este PC: {reason}.");

        var plan = Plan(tweak);
        var changes = Describe(plan);
        EnsureReviewed(changes, reviewedChanges);
        if (plan.Length == 0)
        {
            log.Info($"O ajuste {tweak.Id} já está aplicado.");
            return [];
        }

        var point = _batchPoint ?? snapshots.EnsureRestorePoint();
        if (_inBatch)
            _batchPoint = point;
        plan = Plan(tweak);
        changes = Describe(plan);
        EnsureReviewed(changes, reviewedChanges);
        if (plan.Length == 0)
            return [];

        var backup = new TweakBackup(tweak.Id, plan.Select(change => new SettingBackup(change.Action, change.Current)).ToArray());
        var file = snapshots.Begin(active, backup, point);
        try
        {
            foreach (var change in plan)
                Write(change.Action, change.Expected);
            Verify(plan.Select(change => new SettingBackup(change.Action, change.Expected)));
        }
        catch (Exception error)
        {
            log.Error($"Falha ao aplicar {tweak.Id}: {error.Message}");
            try
            {
                Restore(backup.Settings.Reverse());
                snapshots.Save(active ?? (file with { State = file.State with { Pending = false, Closed = true } }));
                log.Warning($"O estado anterior a {tweak.Id} foi restaurado.");
            }
            catch (Exception rollbackError)
            {
                throw new AggregateException("Falha de aplicação e de recuperação. O backup ativo foi preservado para Reverter tudo.",
                    error, rollbackError);
            }
            throw;
        }

        snapshots.Save(file with { State = file.State with { Pending = false } });
        log.Info($"Ajuste aplicado: {tweak.Id}.");
        return changes;
    }

    public IReadOnlyList<SettingChange> Revert(string tweakId, bool dryRun = true) =>
        RevertInternal([tweakId], dryRun, null);

    /// <summary>
    /// Reverts a group of tweaks (e.g. the group of an A/B test) that are the most recent entries of the active backup.
    /// Ids not in the backup are ignored (they had nothing to change). Fails if a tweak applied later sits on top of them.
    /// </summary>
    public IReadOnlyList<SettingChange> RevertLatest(IReadOnlyCollection<string> tweakIds, bool dryRun = true,
        IReadOnlyList<SettingChange>? reviewedChanges = null) =>
        RevertInternal(tweakIds, dryRun, reviewedChanges);

    public IReadOnlyList<SettingChange> RevertAll(bool dryRun = true, IReadOnlyList<SettingChange>? reviewedChanges = null) =>
        RevertInternal(null, dryRun, reviewedChanges);

    private IReadOnlyList<SettingChange> RevertInternal(IReadOnlyCollection<string>? tweakIds, bool dryRun, IReadOnlyList<SettingChange>? reviewedChanges)
    {
        using var lease = dryRun ? null : snapshots.Acquire();
        var file = snapshots.ReadActive();
        if (file is null)
        {
            EnsureReviewed([], reviewedChanges);
            return [];
        }

        var tweaks = file.State.Tweaks;
        var count = tweaks.Length;
        if (tweakIds is not null)
        {
            var ids = tweakIds.ToHashSet(StringComparer.Ordinal);
            if (tweaks.All(tweak => !ids.Contains(tweak.Id)))
            {
                EnsureReviewed([], reviewedChanges);
                return [];
            }
            // The consecutive most recent entries that belong to the group.
            count = 0;
            while (count < tweaks.Length && ids.Contains(tweaks[^(count + 1)].Id))
                count++;
            if (count == 0)
                throw new InvalidOperationException(ids.Count == 1
                    ? "Reverta primeiro o ajuste mais recente ou escolha Reverter tudo."
                    : "Outros ajustes foram aplicados depois deste grupo. Reverta-os primeiro ou escolha Reverter tudo.");
        }

        var selected = tweaks[^count..];
        // The first capture of a target contains its state before this batch.
        var original = selected.SelectMany(tweak => tweak.Settings)
            .DistinctBy(entry => entry.Action.Identity, StringComparer.OrdinalIgnoreCase).ToArray();
        // A key first seen absent must end absent, keeping how many ancestors were created with it.
        var absentKeys = original.Where(entry => entry.Action is RegistryAction)
            .GroupBy(entry => ((RegistryAction)entry.Action).Key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.First().Before is RegistryState { KeyExists: false })
            .ToDictionary(group => group.Key, group => (RegistryState)group.First().Before, StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < original.Length; index++)
        {
            var entry = original[index];
            if (entry.Action is RegistryAction registryEntry && absentKeys.TryGetValue(registryEntry.Key, out var absent) &&
                entry.Before is RegistryState { Value: null })
                original[index] = entry with { Before = absent };
        }
        var (current, changes) = PreviewRevert(original);
        EnsureReviewed(changes, reviewedChanges);
        if (dryRun)
        {
            log.Info($"Simulação de reversão: {Label(tweakIds)}.");
            return changes;
        }

        if (changes.Length > 0)
        {
            var point = snapshots.EnsureRestorePoint();
            (current, changes) = PreviewRevert(original);
            EnsureReviewed(changes, reviewedChanges);
            if (changes.Length > 0)
            {
                snapshots.SavePreRevert(file, current, point);
                snapshots.Save(file with { State = file.State with { Pending = true } });
                Restore(original.Reverse());
            }
        }

        var state = file.State with { Closed = selected.Length == tweaks.Length, Pending = false };
        if (!state.Closed)
            state = state with { Tweaks = tweaks[..^count] };
        snapshots.Save(file with { State = state });
        log.Info($"Reversão concluída: {Label(tweakIds)}.");
        return changes;
    }

    private static string Label(IReadOnlyCollection<string>? tweakIds) => tweakIds is null ? "tudo" : string.Join(", ", tweakIds);

    private (SettingBackup[] Current, SettingChange[] Changes) PreviewRevert(SettingBackup[] original)
    {
        var current = original.Select(entry => new SettingBackup(entry.Action, Read(entry.Action))).ToArray();
        var plan = original.Zip(current, (before, now) => new PlannedChange(before.Action, now.Before, before.Before))
            .Where(change => !change.Current.Matches(change.Expected)).ToArray();
        return (current, Describe(plan));
    }

    private PlannedChange[] Plan(TweakDefinition tweak) => tweak.Actions.Select(action =>
    {
        var before = Read(action);
        SettingState expected = action switch
        {
            RegistryAction { Operation: RegistryOperation.Set, Accept: { } accept } when
                before is RegistryState { Value: { } current } && accept.Any(value => value.Matches(current)) => before,
            RegistryAction { Operation: RegistryOperation.Set, Expected: not null } set => new RegistryState(true, set.Expected),
            RegistryAction { Operation: RegistryOperation.Delete, Expected: null } when before is RegistryState state =>
                state with { Value = null, Native = null },
            // Not installed / not present on this Windows build: nothing to change.
            ServiceAction when before is ServiceState { Startup: null } => before,
            ServiceAction service => new ServiceState(service.Expected),
            PowerAction scheme => new PowerState(scheme.Expected),
            ScheduledTaskAction when before is TaskState { Exists: false } => before,
            ScheduledTaskAction task => new TaskState(true, task.Expected == TaskSetting.Enabled),
            PowerPlanAction powerPlan => new PowerPlanState(true, powerPlan.Settings),
            NvidiaSettingAction setting => new NvidiaSettingState(true, setting.Expected),
            DnsAction servers => new DnsState(servers.Servers),
            _ => throw new InvalidDataException("Operação de ajuste inválida.")
        };
        return new PlannedChange(action, before, expected);
    }).Where(change => !change.Current.Matches(change.Expected)).ToArray();

    private SettingState Read(TweakAction action) => action switch
    {
        RegistryAction entry => registry.Read(entry),
        ServiceAction service => new ServiceState(services.GetStartup(service.Name)),
        PowerAction => new PowerState(power.GetActiveScheme()),
        ScheduledTaskAction task => Require(tasks).Get(task.Path),
        PowerPlanAction powerPlan => Require(plans).Read(powerPlan),
        NvidiaSettingAction setting => Require(nvidia).Read(setting.SettingId),
        DnsAction servers => Require(dns).Read(servers),
        _ => throw new InvalidDataException("Tipo de ajuste desconhecido.")
    };

    private static T Require<T>(T? dependency) where T : class =>
        dependency ?? throw new InvalidOperationException($"{typeof(T).Name} não foi configurado.");

    private void Write(TweakAction action, SettingState state)
    {
        log.Info($"Alteração em {action.Target} → {state.Display}.");
        switch (action, state)
        {
            case (RegistryAction entry, RegistryState value):
                registry.Write(entry, value);
                break;
            case (ServiceAction service, ServiceState { Startup: { } startup }):
                services.SetStartup(service.Name, startup);
                break;
            case (PowerAction, PowerState scheme):
                power.SetActiveScheme(scheme.Scheme);
                break;
            case (ScheduledTaskAction task, TaskState { Exists: true } taskState):
                Require(tasks).SetEnabled(task.Path, taskState.Enabled);
                break;
            case (PowerPlanAction powerPlan, PowerPlanState planState):
                Require(plans).Write(powerPlan, planState);
                break;
            case (NvidiaSettingAction setting, NvidiaSettingState settingState):
                Require(nvidia).Write(setting.SettingId, settingState);
                break;
            case (DnsAction servers, DnsState dnsState):
                Require(dns).Write(servers, dnsState);
                break;
            default:
                throw new InvalidDataException("O estado do backup não corresponde à operação.");
        }
    }

    private void Restore(IEnumerable<SettingBackup> settings)
    {
        foreach (var entry in settings)
        {
            if (!Read(entry.Action).Matches(entry.Before))
                Write(entry.Action, entry.Before);
        }
        Verify(settings);
    }

    private void Verify(IEnumerable<SettingBackup> settings)
    {
        foreach (var entry in settings)
        {
            if (!Read(entry.Action).Matches(entry.Before))
                throw new InvalidOperationException($"O Windows não confirmou o estado esperado em {entry.Action.Target}.");
        }
    }

    private static SettingChange[] Describe(PlannedChange[] plan) => plan.Select(change =>
        new SettingChange(change.Action.Target, change.Current.Display, change.Expected.Display)).ToArray();

    private static void EnsureReviewed(SettingChange[] changes, IReadOnlyList<SettingChange>? reviewed)
    {
        if (reviewed is not null && !changes.SequenceEqual(reviewed))
            throw new InvalidOperationException("O estado mudou após a simulação. Revise a nova simulação antes de confirmar.");
    }

    private sealed record PlannedChange(TweakAction Action, SettingState Current, SettingState Expected);
}
