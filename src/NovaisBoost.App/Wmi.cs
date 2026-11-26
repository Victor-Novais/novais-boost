using System.Management;
using Microsoft.Win32;
using NovaisBoost.Core;

namespace NovaisBoost.App;

internal static class Wmi
{
    public static T[] Query<T>(string query, Func<ManagementBaseObject, T> select, string scope = @"root\cimv2")
    {
        using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(query), new System.Management.EnumerationOptions
        {
            Timeout = TimeSpan.FromSeconds(5), ReturnImmediately = true, Rewindable = false
        });
        using var results = searcher.Get();
        var values = new List<T>();
        foreach (ManagementBaseObject item in results)
        {
            using (item)
                values.Add(select(item));
        }
        return values.ToArray();
    }
}

internal sealed class WindowsRestorePoints : IRestorePointProvider
{
    private const string Scope = @"root\default";
    private const string RestorePointDescription = "NOVAIS boost";
    private const int ModifySettings = 12;
    private const int BeginSystemChange = 100;
    private const int ServiceDisabled = 1058;

    public RestorePoint Ensure()
    {
        var frequency = GetFrequency();
        var before = GetPoints();
        using var systemRestore = new ManagementClass(Scope, "SystemRestore", null);
        using var parameters = systemRestore.GetMethodParameters("CreateRestorePoint");
        parameters["Description"] = RestorePointDescription;
        parameters["RestorePointType"] = (uint)ModifySettings;
        parameters["EventType"] = (uint)BeginSystemChange;
        using var result = systemRestore.InvokeMethod("CreateRestorePoint", parameters, null);
        var code = Convert.ToUInt32(result?["ReturnValue"] ?? uint.MaxValue);
        if (code == ServiceDisabled || code == 0x80070422)
            throw new SystemProtectionDisabledException();
        if (code != 0)
            throw new InvalidOperationException($"Falha ao solicitar o ponto de restauração (código 0x{code:X8}).");

        var after = GetPoints().OrderByDescending(point => point.Created).ToArray();
        var created = Array.Find(after, point => point.Description == RestorePointDescription &&
            before.All(old => old.SequenceNumber != point.SequenceNumber));
        if (created is not null)
            return new RestorePoint(created.SequenceNumber, false);

        var now = DateTime.Now;
        var recent = Array.Find(after, point => point.Created <= now && now - point.Created < frequency);
        if (recent is not null)
            return new RestorePoint(recent.SequenceNumber, true);
        if (after.Length == 0)
            throw new SystemProtectionDisabledException();
        throw new InvalidOperationException("O Windows não confirmou um ponto de restauração utilizável. Verifique a Proteção do Sistema.");
    }

    private static TimeSpan GetFrequency()
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore");
        return TimeSpan.FromMinutes(key?.GetValue("SystemRestorePointCreationFrequency") is int value
            ? unchecked((uint)value) : 1_440u);
    }

    private static WindowsPoint[] GetPoints() => Wmi.Query(
        "SELECT SequenceNumber, CreationTime, Description FROM SystemRestore",
        item => new WindowsPoint(Convert.ToInt64(item["SequenceNumber"]),
            ManagementDateTimeConverter.ToDateTime((string)item["CreationTime"]),
            (string)item["Description"]), Scope);

    private sealed record WindowsPoint(long SequenceNumber, DateTime Created, string Description);
}
