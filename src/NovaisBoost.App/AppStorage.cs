using System.Globalization;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using NovaisBoost.Core;

namespace NovaisBoost.App;

internal static class AppStorage
{
    public static string Open()
    {
        var commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(commonData))
            throw new InvalidOperationException("O Windows não informou o diretório ProgramData.");

        var root = new DirectoryInfo(Path.Combine(commonData, "NOVAIS boost"));
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var security = new DirectorySecurity();
        security.SetOwner(administrators);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        foreach (var account in new[] { administrators, system })
            security.AddAccessRule(new FileSystemAccessRule(account, FileSystemRights.FullControl,
                inheritance, PropagationFlags.None, AccessControlType.Allow));
        if (!root.Exists)
            root.Create(security);

        root.Refresh();
        if ((root.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException($"A pasta de dados {root.FullName} é um redirecionamento.");

        var acl = root.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
        var owner = acl.GetOwner(typeof(SecurityIdentifier));
        var allowed = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).OfType<FileSystemAccessRule>()
            .Where(rule => rule.AccessControlType == AccessControlType.Allow).ToArray();
        if ((!administrators.Equals(owner) && !system.Equals(owner)) || !acl.AreAccessRulesProtected ||
            allowed.Any(rule => !administrators.Equals(rule.IdentityReference) && !system.Equals(rule.IdentityReference)))
            throw new UnauthorizedAccessException($"A pasta de dados {root.FullName} precisa ter acesso restrito a Administradores e SYSTEM.");
        return root.FullName;
    }
}

internal sealed class DailyLog(string dataDirectory) : IActionLog
{
    private readonly string _directory = Path.Combine(dataDirectory, "logs");

    public void Info(string message) => Write("INFO", message);
    public void Warning(string message) => Write("AVISO", message);
    public void Error(string message) => Write("ERRO", message);

    private void Write(string level, string message)
    {
        Directory.CreateDirectory(_directory);
        var now = DateTimeOffset.Now;
        var path = Path.Combine(_directory, now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log");
        var line = $"{now:yyyy-MM-dd HH:mm:ss zzz} [{level}] {message.ReplaceLineEndings(" ")}{Environment.NewLine}";
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.AppendAllText(path, line, Encoding.UTF8);
                return;
            }
            catch (IOException error) when ((error.HResult & 0xFFFF) == 32 && attempt < 8)
            {
                Thread.Sleep(25);
            }
        }
    }
}
