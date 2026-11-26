namespace NovaisBoost.Core;

public sealed record SettingChange(string Target, string Current, string Expected);

public sealed class TweakEngine(
    IRegistry registry, IServiceController services, IPowerConfig power,
    SnapshotManager snapshots, IActionLog log)
{
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
        if (tweak.Compat.Hardware.Length > 0 || tweak.Compat.AntiCheat.Length > 0)
            throw new InvalidOperationException("Este ajuste exige uma verificação de compatibilidade ainda indisponível.");

        var plan = Plan(tweak);
        var changes = Describe(plan);
        EnsureReviewed(changes, reviewedChanges);
        if (plan.Length == 0)
        {
            log.Info($"O ajuste {tweak.Id} já está aplicado.");
            return [];
        }

        var point = snapshots.EnsureRestorePoint();
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
        RevertInternal(tweakId, dryRun, null);

    public IReadOnlyList<SettingChange> RevertAll(bool dryRun = true, IReadOnlyList<SettingChange>? reviewedChanges = null) =>
        RevertInternal(null, dryRun, reviewedChanges);

    private IReadOnlyList<SettingChange> RevertInternal(string? tweakId, bool dryRun, IReadOnlyList<SettingChange>? reviewedChanges)
    {
        using var lease = dryRun ? null : snapshots.Acquire();
        var file = snapshots.ReadActive();
        if (file is null)
        {
            EnsureReviewed([], reviewedChanges);
            return [];
        }

        var tweaks = file.State.Tweaks;
        if (tweakId is not null && tweaks.All(tweak => tweak.Id != tweakId))
            return [];
        if (tweakId is not null && tweaks[^1].Id != tweakId)
            throw new InvalidOperationException("Reverta primeiro o ajuste mais recente ou escolha Reverter tudo.");

        var selected = tweakId is null ? tweaks : [tweaks[^1]];
        // The first capture of a target contains its state before this batch.
        var original = selected.SelectMany(tweak => tweak.Settings)
            .DistinctBy(entry => entry.Action.Identity, StringComparer.OrdinalIgnoreCase).ToArray();
        var absentKeys = original.Where(entry => entry.Action is RegistryAction)
            .GroupBy(entry => ((RegistryAction)entry.Action).Key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.First().Before is RegistryState { KeyExists: false })
            .Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < original.Length; index++)
        {
            var entry = original[index];
            if (entry.Action is RegistryAction registryEntry && absentKeys.Contains(registryEntry.Key) &&
                entry.Before is RegistryState { Value: null })
                original[index] = entry with { Before = new RegistryState(false, null) };
        }
        var (current, changes) = PreviewRevert(original);
        EnsureReviewed(changes, reviewedChanges);
        if (dryRun)
        {
            log.Info($"Simulação de reversão: {tweakId ?? "tudo"}.");
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
            state = state with { Tweaks = tweaks[..^1] };
        snapshots.Save(file with { State = state });
        log.Info($"Reversão concluída: {tweakId ?? "tudo"}.");
        return changes;
    }

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
            RegistryAction { Operation: RegistryOperation.Set, Expected: not null } set => new RegistryState(true, set.Expected),
            RegistryAction { Operation: RegistryOperation.Delete, Expected: null } when before is RegistryState state =>
                new RegistryState(state.KeyExists, null),
            ServiceAction service => new ServiceState(service.Expected),
            PowerAction scheme => new PowerState(scheme.Expected),
            _ => throw new InvalidDataException("Operação de ajuste inválida.")
        };
        return new PlannedChange(action, before, expected);
    }).Where(change => !change.Current.Matches(change.Expected)).ToArray();

    private SettingState Read(TweakAction action) => action switch
    {
        RegistryAction entry => registry.Read(entry),
        ServiceAction service => new ServiceState(services.GetStartup(service.Name)),
        PowerAction => new PowerState(power.GetActiveScheme()),
        _ => throw new InvalidDataException("Tipo de ajuste desconhecido.")
    };

    private void Write(TweakAction action, SettingState state)
    {
        log.Info($"Alteração em {action.Target} → {state.Display}.");
        switch (action, state)
        {
            case (RegistryAction entry, RegistryState value):
                registry.Write(entry, value);
                break;
            case (ServiceAction service, ServiceState startup):
                services.SetStartup(service.Name, startup.Startup);
                break;
            case (PowerAction, PowerState scheme):
                power.SetActiveScheme(scheme.Scheme);
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
