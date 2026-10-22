using System.Xml.Linq;
using WinVelo.Core.Interfaces;
using WinVelo.Core.Safety;

namespace WinVelo.Platform;

public sealed class WindowsTaskSchedulerService : ITaskSchedulerService
{
    private readonly IShellService _shell;
    public WindowsTaskSchedulerService() { EnsureNotTestMode(); _shell = new WindowsShellService(); }
    private static void EnsureNotTestMode()
    {
        if (TestModeManager.IsTestMode) throw new InvalidOperationException("Real platform class operation blocked in TEST MODE.");
    }
    private static string FullName(string path, string name) => "\\" + string.Join('\\', new[] { path.Trim('\\').Replace('/', '\\'), name }.Where(s => s.Length > 0));
    private string Checked(params string[] args)
    {
        EnsureNotTestMode();
        return WindowsCommand.RequireSuccess(WindowsCommand.Run(_shell, "schtasks.exe", 30, args), "schtasks " + string.Join(' ', args));
    }
    public bool TaskExists(string path, string taskName)
    {
        // COM distinguishes missing tasks from access denied without parsing translated console messages.
        EnsureNotTestMode();
        Type type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException("Task Scheduler is unavailable.");
        dynamic service = Activator.CreateInstance(type)!;
        object? folder = null;
        try
        {
            service.Connect();
            folder = service.GetFolder("\\");
            try { object task = ((dynamic)folder).GetTask(FullName(path, taskName)); System.Runtime.InteropServices.Marshal.FinalReleaseComObject(task); return true; }
            catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x80070002) || ex.HResult == unchecked((int)0x80070003)) { return false; }
        }
        finally
        {
            if (folder != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(folder);
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(service);
        }
    }
    public string? ExportTaskDefinition(string path, string taskName) => Checked("/query", "/tn", FullName(path, taskName), "/xml");
    public bool IsTaskEnabled(string path, string taskName)
    {
        var doc = XDocument.Parse(ExportTaskDefinition(path, taskName)!);
        XNamespace ns = doc.Root!.Name.Namespace;
        return !string.Equals(doc.Root.Element(ns + "Settings")?.Element(ns + "Enabled")?.Value, "false", StringComparison.OrdinalIgnoreCase);
    }
    public void EnableTask(string path, string taskName) => SetEnabled(path, taskName, true);
    public void DisableTask(string path, string taskName) => SetEnabled(path, taskName, false);
    private void SetEnabled(string path, string name, bool enabled)
    {
        Checked("/change", "/tn", FullName(path, name), enabled ? "/enable" : "/disable");
        if (IsTaskEnabled(path, name) != enabled) throw new InvalidOperationException("Task state was not changed: " + name);
    }
    public void DeleteTask(string path, string taskName) => Checked("/delete", "/tn", FullName(path, taskName), "/f");
    public void RegisterTaskDefinition(string path, string taskName, string xml)
    {
        EnsureNotTestMode();
        string file = Path.GetTempFileName();
        try { File.WriteAllText(file, xml, System.Text.Encoding.Unicode); Checked("/create", "/tn", FullName(path, taskName), "/xml", file, "/f"); }
        finally { File.Delete(file); }
    }
}
