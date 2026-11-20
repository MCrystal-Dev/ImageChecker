using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Win32;
using ComData = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace ImageChecker;

internal static class ShellMenu
{
    private const string MenuKey = @"Software\Classes\*\shell\MCrystal.ImageChecker";
    private static Guid ClassId(int index) => new($"F9F9A2F7-DABC-4DFB-B271-53150000000{index}");

    internal static void Register(bool install)
    {
        string executable = Environment.ProcessPath!;
        if (install)
        {
            using var menu = Registry.CurrentUser.CreateSubKey(MenuKey);
            menu.SetValue("MUIVerb", "ImageChecker");
            menu.SetValue("Icon", $"\"{executable}\",0");
            menu.SetValue("SubCommands", "");
            menu.SetValue("MultiSelectModel", "Player");
            menu.SetValue("AppliesTo", string.Join(" OR ", Program.Extensions.Select(e => $"System.FileExtension:=\"{e}\"")));
        }
        else Registry.CurrentUser.DeleteSubKeyTree(MenuKey, false);
        for (int i = 0; i < Program.Actions.Length; i++)
        {
            string id = ClassId(i).ToString("B");
            string cls = @"Software\Classes\CLSID\" + id;
            if (!install)
            {
                Registry.CurrentUser.DeleteSubKeyTree(cls, false);
                continue;
            }
            using var server = Registry.CurrentUser.CreateSubKey(cls + @"\LocalServer32");
            server.SetValue("", $"\"{executable}\" --shell {i}");
            using var verb = Registry.CurrentUser.CreateSubKey(MenuKey + @"\shell\action" + i);
            verb.SetValue("MUIVerb", Program.Actions[i] + "…");
            verb.SetValue("MultiSelectModel", "Player");
            using var target = verb.CreateSubKey("DropTarget");
            target.SetValue("CLSID", id);
        }
        SHChangeNotify(0x08000000, 0, 0, 0);
    }

    internal static void Listen(int action)
    {
        if ((uint)action >= Program.Actions.Length) throw new ArgumentOutOfRangeException(nameof(action));
        Application.OleRequired();
        using var context = new ApplicationContext();
        string[]? selection = null;
        var factory = new DropFactory(paths => selection = paths);
        Guid clsid = ClassId(action);
        Marshal.ThrowExceptionForHR(CoRegisterClassObject(ref clsid, factory, 4, 0, out int cookie));
        try
        {
            var started = DateTime.UtcNow;
            using var timer = new System.Windows.Forms.Timer { Interval = 100 };
            timer.Tick += (_, _) =>
            {
                if (selection == null && DateTime.UtcNow - started < TimeSpan.FromMinutes(2)) return;
                timer.Stop();
                if (selection is not { Length: > 0 }) { context.ExitThread(); return; }
                context.MainForm = new Workspace(selection, action);
                context.MainForm.Show();
                context.MainForm.Activate();
            };
            timer.Start();
            Application.Run(context);
        }
        finally { CoRevokeClassObject(cookie); GC.KeepAlive(factory); }
    }

    [DllImport("shell32.dll")] private static extern void SHChangeNotify(int e, uint flags, nint a, nint b);
    [DllImport("ole32.dll")] private static extern int CoRegisterClassObject(ref Guid id, [MarshalAs(UnmanagedType.IUnknown)] object factory, uint context, uint flags, out int cookie);
    [DllImport("ole32.dll")] private static extern int CoRevokeClassObject(int cookie);
}

[ComVisible(true), Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IClassFactory
{
    [PreserveSig] int CreateInstance(nint outer, ref Guid iid, out nint result);
    [PreserveSig] int LockServer(int locked);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class DropFactory(Action<string[]> receive) : IClassFactory
{
    public int CreateInstance(nint outer, ref Guid iid, out nint result)
    {
        result = 0;
        if (outer != 0) return unchecked((int)0x80040110);
        var ptr = Marshal.GetIUnknownForObject(new ImageDrop(receive));
        try { return Marshal.QueryInterface(ptr, in iid, out result); }
        finally { Marshal.Release(ptr); }
    }
    public int LockServer(int locked) => 0;
}

[ComVisible(true), Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellDropTarget
{
    void DragEnter(ComData data, uint keys, long point, ref uint effect);
    void DragOver(uint keys, long point, ref uint effect);
    void DragLeave();
    void Drop(ComData data, uint keys, long point, ref uint effect);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class ImageDrop(Action<string[]> receive) : IShellDropTarget
{
    public void DragEnter(ComData data, uint keys, long point, ref uint effect) => effect &= 1;
    public void DragOver(uint keys, long point, ref uint effect) => effect &= 1;
    public void DragLeave() { }
    public void Drop(ComData data, uint keys, long point, ref uint effect)
    {
        var format = new FORMATETC { cfFormat = 15, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL };
        data.GetData(ref format, out var medium);
        try
        {
            uint count = DragQueryFile(medium.unionmember, uint.MaxValue, null, 0);
            var paths = new List<string>();
            for (uint i = 0; i < count; i++)
            {
                uint length = DragQueryFile(medium.unionmember, i, null, 0);
                var name = new System.Text.StringBuilder((int)length + 1);
                DragQueryFile(medium.unionmember, i, name, length + 1);
                if (Program.Supported(name.ToString())) paths.Add(name.ToString());
            }
            receive(paths.ToArray());
            effect &= 1;
        }
        finally { ReleaseStgMedium(ref medium); }
    }
    [DllImport("shell32.dll", EntryPoint = "DragQueryFileW", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(nint drop, uint index, System.Text.StringBuilder? name, uint length);
    [DllImport("ole32.dll")] private static extern void ReleaseStgMedium(ref STGMEDIUM medium);
}
