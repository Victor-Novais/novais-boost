using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using NovaisBoost.Core;

namespace NovaisBoost.App;

internal sealed class WindowsScheduledTasks(IActionLog log) : IScheduledTasks
{
    private static readonly XNamespace Task = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    public TaskState Get(string path)
    {
        string xml;
        try
        {
            xml = WindowsCommand.Run("schtasks.exe", "/query", "/tn", path, "/xml");
        }
        catch (Win32Exception error)
        {
            // schtasks reports a missing task with a nonzero exit code; such tasks are skipped.
            log.Info($"Tarefa {path} não encontrada ({error.Message}).");
            return new TaskState(false, false);
        }

        var start = xml.IndexOf('<');
        var document = XDocument.Parse(start > 0 ? xml[start..] : xml);
        var enabled = document.Root?.Element(Task + "Settings")?.Element(Task + "Enabled")?.Value;
        return new TaskState(true, enabled is null || bool.Parse(enabled));
    }

    public void SetEnabled(string path, bool enabled) =>
        WindowsCommand.Run("schtasks.exe", "/change", "/tn", path, enabled ? "/enable" : "/disable");
}

internal sealed class WindowsPowerPlans : IPowerPlans
{
    private const uint NotFound = 2;
    private static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    public PowerPlanState Read(PowerPlanAction plan)
    {
        if (!Exists(plan.Id))
            return new PowerPlanState(false, null);

        var scheme = plan.Id;
        var settings = plan.Settings.Select(setting =>
        {
            var (subgroup, id) = (setting.Subgroup, setting.Setting);
            Check(PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref id, out var ac), "ler (AC)");
            Check(PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref id, out var dc), "ler (DC)");
            return setting with { Ac = ac, Dc = dc };
        }).ToArray();
        return new PowerPlanState(true, settings);
    }

    public void Write(PowerPlanAction plan, PowerPlanState state)
    {
        var id = plan.Id.ToString("D");
        if (!state.Exists)
        {
            if (!Exists(plan.Id))
                return;
            if (new WindowsPowerConfig().GetActiveScheme() == plan.Id)
                throw new InvalidOperationException($"O plano {plan.Name} está ativo; ative outro plano antes de removê-lo.");
            WindowsCommand.Run("powercfg.exe", "/delete", id);
            return;
        }

        if (!Exists(plan.Id))
        {
            try
            {
                WindowsCommand.Run("powercfg.exe", "/duplicatescheme", plan.Template.ToString("D"), id);
            }
            catch (Win32Exception)
            {
                // Some editions hide the Ultimate Performance template; High Performance is always present.
                WindowsCommand.Run("powercfg.exe", "/duplicatescheme", HighPerformance.ToString("D"), id);
            }
            WindowsCommand.Run("powercfg.exe", "/changename", id, plan.Name, "Criado pelo NOVAIS boost. Removido ao reverter.");
        }

        foreach (var setting in state.Settings ?? [])
        {
            string[] target = [id, setting.Subgroup.ToString("D"), setting.Setting.ToString("D")];
            WindowsCommand.Run("powercfg.exe", ["/setacvalueindex", .. target, setting.Ac.ToString()]);
            WindowsCommand.Run("powercfg.exe", ["/setdcvalueindex", .. target, setting.Dc.ToString()]);
        }

        // Values written to the active plan only take effect when it is re-applied.
        if (new WindowsPowerConfig().GetActiveScheme() == plan.Id)
            WindowsCommand.Run("powercfg.exe", "/setactive", id);
    }

    private static bool Exists(Guid scheme)
    {
        uint size = 0;
        var result = PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
        if (result == NotFound)
            return false;
        Check(result, "localizar");
        return true;
    }

    private static void Check(uint result, string operation)
    {
        if (result != 0)
            throw new Win32Exception((int)result, $"Falha ao {operation} configuração de energia (código {result}).");
    }

    [DllImport("powrprof.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr subgroup, IntPtr setting, IntPtr buffer, ref uint size);

    [DllImport("powrprof.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);

    [DllImport("powrprof.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);
}
