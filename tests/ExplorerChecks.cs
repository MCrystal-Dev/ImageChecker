using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ImageMagick;
using ImageChecker;

internal static class ExplorerChecks
{
    // Check the installed application's dropdown through each Explorer entry point.
    internal static void Run()
    {
        var dir = Directory.CreateTempSubdirectory("ImageChecker-explorer-");
        var paths = new[] { Path.Combine(dir.FullName, "one image.png"), Path.Combine(dir.FullName, "two images.jpg") };
        try
        {
            using var image = new MagickImage(MagickColors.Blue, 12, 18);
            foreach (string path in paths) image.Write(path);
            var first = dir.CreateSubdirectory("First folder");
            var second = dir.CreateSubdirectory("Second folder");
            image.Write(Path.Combine(first.FullName, "direct.WEBP"));
            image.Write(Path.Combine(second.FullName, "another.jpeg"));
            image.Write(Path.Combine(first.CreateSubdirectory("Nested").FullName, "nested.png"));
            File.WriteAllText(Path.Combine(first.FullName, "notes.txt"), "skip");
            foreach (string action in Program.Actions) CheckAction(paths, 2, action);
            CheckAction([first.FullName], 1, Program.Actions[1]);
            CheckAction([first.FullName, second.FullName], 2, Program.Actions[2]);
        }
        finally { dir.Delete(true); }
    }

    private static void CheckAction(string[] selection, int expectedImages, string action)
    {
        var pidls = new List<nint>();
        nint menu = 0;
        try
        {
            foreach (string path in selection)
            {
                SHParseDisplayName(path, 0, out nint id, 0, out _);
                pidls.Add(id);
            }
            Guid folderId = typeof(IShellFolder).GUID, contextId = typeof(IContextMenu).GUID;
            SHBindToParent(pidls[0], ref folderId, out IShellFolder folder, out _);
            folder.GetUIObjectOf(0, (uint)pidls.Count, pidls.Select(ILFindLastID).ToArray(), ref contextId, 0, out IContextMenu context);
            menu = CreatePopupMenu();
            Marshal.ThrowExceptionForHR(context.QueryContextMenu(menu, 0, 1, 30000, 0));
            nint sub = GetSubMenu(menu, Find(menu, "ImageChecker"));
            if (sub == 0) throw new Exception("Missing ImageChecker submenu");
            if (context is IContextMenu2 lazy) lazy.HandleMenuMsg(0x0117, sub, 0);
            int verb = Find(sub, action);
            if (GetMenuItemCount(sub) != 6) throw new Exception("Expected six ImageChecker verbs.");
            var old = Process.GetProcessesByName("ImageChecker").Select(p => p.Id).ToHashSet();
            var info = new InvokeInfo { Size = Marshal.SizeOf<InvokeInfo>(), Verb = (nint)(GetMenuItemID(sub, verb) - 1), Show = 1 };
            context.InvokeCommand(ref info);
            for (int i = 0; i < 100; i++)
            {
                foreach (var process in Process.GetProcessesByName("ImageChecker").Where(p => !old.Contains(p.Id)))
                {
                    if (process.MainWindowTitle != $"ImageChecker — {expectedImages} selected") continue;
                    bool currentLabels = HasActionDropdown(process.MainWindowHandle, action);
                    process.CloseMainWindow();
                    if (!process.WaitForExit(5000)) { process.CloseMainWindow(); process.WaitForExit(5000); }
                    if (!currentLabels) throw new Exception($"Incorrect dropdown labels or active action after opening {action}.");
                    Console.WriteLine($"Verified dropdown via {action} on {expectedImages} image(s).");
                    return;
                }
                Thread.Sleep(150);
            }
            throw new Exception($"Timed out waiting for the {expectedImages}-image selection.");
        }
        finally
        {
            if (menu != 0) DestroyMenu(menu);
            foreach (var ptr in pidls) Marshal.FreeCoTaskMem(ptr);
        }
    }
    private static bool HasActionDropdown(nint window, string expectedAction)
    {
        bool matched = false;
        EnumChildWindows(window, (child, _) =>
        {
            var className = new StringBuilder(256);
            GetClassName(child, className, className.Capacity);
            if (!className.ToString().Contains("COMBOBOX", StringComparison.OrdinalIgnoreCase)) return true;
            if ((int)SendMessage(child, 0x146, 0, 0) != Program.Actions.Length) return true; // CB_GETCOUNT
            for (int i = 0; i < Program.Actions.Length; i++)
            {
                var label = new StringBuilder(256);
                SendMessage(child, 0x148, i, label); // CB_GETLBTEXT
                if (label.ToString() != Program.Actions[i]) return true;
            }
            matched = (int)SendMessage(child, 0x147, 0, 0) == Array.IndexOf(Program.Actions, expectedAction);
            return !matched;
        }, 0);
        return matched;
    }
    private delegate bool EnumChildProc(nint child, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumChildProc callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder value, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessage(nint window, uint message, nint index, nint value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessage(nint window, uint message, nint index, StringBuilder value);
    private static int Find(nint menu, string text)
    {
        var names = new List<string>();
        for (int i = 0; i < GetMenuItemCount(menu); i++)
        {
            var label = new StringBuilder(300);
            GetMenuString(menu, (uint)i, label, 300, 0x400);
            names.Add(label.ToString());
            if (label.ToString().Replace("&", "").StartsWith(text, StringComparison.OrdinalIgnoreCase)) return i;
        }
        throw new Exception($"Missing {text} in {string.Join(", ", names)}");
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct InvokeInfo { public int Size, Mask; public nint Owner, Verb, Args, Directory; public int Show, HotKey; public nint Icon; }
    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        void ParseDisplayName(); void EnumObjects(); void BindToObject(); void BindToStorage(); void CompareIDs();
        void CreateViewObject(); void GetAttributesOf();
        void GetUIObjectOf(nint owner, uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] nint[] ids, ref Guid iid, nint reserved, out IContextMenu result);
    }
    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(nint menu, uint index, uint first, uint last, uint flags);
        void InvokeCommand(ref InvokeInfo info);
    }
    [ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        void QueryContextMenu(); void InvokeCommand(); void GetCommandString();
        [PreserveSig] int HandleMenuMsg(uint msg, nint wparam, nint lparam);
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)] private static extern void SHParseDisplayName(string name, nint bind, out nint pidl, uint attributes, out uint result);
    [DllImport("shell32.dll", PreserveSig = false)] private static extern void SHBindToParent(nint pidl, ref Guid iid, out IShellFolder parent, out nint child);
    [DllImport("shell32.dll")] private static extern nint ILFindLastID(nint pidl);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll")] private static extern nint GetSubMenu(nint menu, int index);
    [DllImport("user32.dll")] private static extern uint GetMenuItemID(nint menu, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMenuString(nint menu, uint index, StringBuilder label, int size, uint flags);
}
