using System.Runtime.InteropServices;

namespace ImageChecker;

internal static class Recycle
{
    internal static void FileToBin(string path, nint owner)
    {
        Guid shellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
        SHCreateItemFromParsingName(path, 0, ref shellItem, out object item);
        var op = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new("3AD05575-8857-4850-9277-11B85BDB8E09"))!)!;
        try
        {
            op.SetOwnerWindow(owner);
            op.SetOperationFlags(0x80000 | 0x4000 | 0x40); // Recycle, warn on permanent deletion, allow undo.
            op.DeleteItem(item, 0);
            op.PerformOperations();
            op.GetAnyOperationsAborted(out bool cancelled);
            if (cancelled || File.Exists(path)) throw new IOException("The file was not moved to the Recycle Bin.");
        }
        finally { Marshal.ReleaseComObject(item); Marshal.ReleaseComObject(op); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, nint bind, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object item);

    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise(nint sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog(nint dialog);
        void SetProperties(nint properties);
        void SetOwnerWindow(nint hwnd);
        void ApplyPropertiesToItem(nint item);
        void ApplyPropertiesToItems(nint items);
        void RenameItem(nint item, nint name, nint sink);
        void RenameItems(nint items, nint name);
        void MoveItem(nint item, nint folder, nint name, nint sink);
        void MoveItems(nint items, nint folder);
        void CopyItem(nint item, nint folder, nint name, nint sink);
        void CopyItems(nint items, nint folder);
        void DeleteItem([MarshalAs(UnmanagedType.Interface)] object item, nint sink);
        void DeleteItems(nint items);
        void NewItem(nint folder, uint attributes, nint name, nint template, nint sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
}
