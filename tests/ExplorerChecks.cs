using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ImageMagick;

internal static class ExplorerChecks
{
    // Exercise Explorer's actual context menu for a mixed selection and invoke its Convert verb.
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
            CheckConvert(paths, 2);
            CheckConvert([first.FullName], 1);
            CheckConvert([first.FullName, second.FullName], 2);
        }
        finally { dir.Delete(true); }
    }

    private static void CheckConvert(string[] selection, int expectedImages)
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
            int convert = Find(sub, "Change file format");
            if (GetMenuItemCount(sub) != 6) throw new Exception("Expected six ImageChecker verbs.");
            var old = Process.GetProcessesByName("ImageChecker").Select(p => p.Id).ToHashSet();
            var info = new InvokeInfo { Size = Marshal.SizeOf<InvokeInfo>(), Verb = (nint)(GetMenuItemID(sub, convert) - 1), Show = 1 };
            context.InvokeCommand(ref info);
            for (int i = 0; i < 100; i++)
            {
                foreach (var process in Process.GetProcessesByName("ImageChecker").Where(p => !old.Contains(p.Id)))
                {
                    if (process.MainWindowTitle != $"ImageChecker — {expectedImages} selected") continue;
                    process.CloseMainWindow();
                    if (!process.WaitForExit(5000)) { process.CloseMainWindow(); process.WaitForExit(5000); }
                    Console.WriteLine($"Explorer opened {expectedImages} image(s) from {string.Join(", ", selection.Select(Path.GetFileName))}.");
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
