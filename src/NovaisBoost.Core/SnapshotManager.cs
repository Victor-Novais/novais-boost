using System.Globalization;
using System.Text.Json;

namespace NovaisBoost.Core;

public sealed record SnapshotInfo(string Path, int TweakCount, long RestorePointNumber, bool Pending);

internal sealed record SettingBackup(TweakAction Action, SettingState Before);
internal sealed record TweakBackup(string Id, SettingBackup[] Settings);
internal sealed record SnapshotState(
    int Version, string Owner, long RestorePointNumber, bool Pending, bool Closed, TweakBackup[] Tweaks);
internal sealed record SnapshotFile(string Path, SnapshotState State);

public sealed class SnapshotManager(
    string dataDirectory, string owner, IRestorePointProvider restorePoints, IActionLog log)
{
    private const int Version = 1;
    private readonly string _backupDirectory = Path.Combine(dataDirectory, "backups");

    public SnapshotInfo? GetActive()
    {
        var file = ReadActive();
        return file is null ? null : new SnapshotInfo(
            file.Path, file.State.Tweaks.Length, file.State.RestorePointNumber, file.State.Pending);
    }

    internal IDisposable Acquire()
    {
        Directory.CreateDirectory(dataDirectory);
        try
        {
            return new FileStream(Path.Combine(dataDirectory, "changes.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException error)
        {
            throw new IOException("Não foi possível bloquear o backup para escrita. Verifique se outra instância está alterando o sistema.", error);
        }
    }

    internal SnapshotFile? ReadActive()
    {
        if (!Directory.Exists(_backupDirectory))
            return null;

        SnapshotFile? active = null;
        foreach (var directory in Directory.EnumerateDirectories(_backupDirectory, $"*-{owner}-*"))
        {
            var path = Path.Combine(directory, "state.json");
            if (!File.Exists(path))
                continue;

            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var state = JsonSerializer.Deserialize<SnapshotState>(file, TweakCatalog.JsonOptions);
            if (state is null || state.Version != Version || state.Owner != owner ||
                state.RestorePointNumber <= 0 || state.Tweaks is not { Length: > 0 } ||
                state.Tweaks.Any(tweak => string.IsNullOrWhiteSpace(tweak.Id) || tweak.Settings is not { Length: > 0 }))
                throw new InvalidDataException($"Backup inválido em {path}.");

            if (state.Closed)
                continue;
            if (active is not null)
                throw new InvalidDataException("Há mais de um backup ativo para esta conta.");
            active = new SnapshotFile(path, state);
        }
        return active;
    }

    internal RestorePoint EnsureRestorePoint()
    {
        var point = restorePoints.Ensure();
        if (point.SequenceNumber <= 0)
            throw new InvalidOperationException("O Windows não confirmou um ponto de restauração.");
        log.Info(point.Reused ? $"Ponto de restauração existente verificado: {point.SequenceNumber}."
            : $"Ponto de restauração criado: {point.SequenceNumber}.");
        return point;
    }

    internal SnapshotFile Begin(SnapshotFile? active, TweakBackup tweak, RestorePoint point)
    {
        var path = active?.Path ?? Path.Combine(_backupDirectory,
            $"{DateTimeOffset.Now.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture)}-{owner}-{Guid.NewGuid():N}", "state.json");
        var tweaks = active is null ? [tweak] : active.State.Tweaks.Append(tweak).ToArray();
        var file = new SnapshotFile(path, new SnapshotState(Version, owner, point.SequenceNumber, true, false, tweaks));
        Save(file);
        return file;
    }

    internal void Save(SnapshotFile file)
    {
        WriteAtomic(file.Path, file.State);
        log.Info($"Backup gravado em {file.Path}.");
    }

    internal void SavePreRevert(SnapshotFile active, SettingBackup[] settings, RestorePoint point)
    {
        var path = Path.Combine(Path.GetDirectoryName(active.Path)!, $"pre-revert-{Guid.NewGuid():N}.json");
        WriteAtomic(path, new SnapshotState(Version, owner, point.SequenceNumber, false, true,
            [new TweakBackup("pre-revert", settings)]));
        log.Info($"Estado anterior à reversão gravado em {path}.");
    }

    private static void WriteAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, TweakCatalog.JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
