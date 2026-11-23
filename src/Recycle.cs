using System.Runtime.InteropServices;

namespace ImageChecker;

internal static class Recycle
{
    private const uint Silent = 0x0004, NoConfirmation = 0x0010, NoErrorUi = 0x0400;
    private const uint RecycleOnDelete = 0x00080000, EarlyFailure = 0x00100000, AddUndoRecord = 0x20000000;

    internal static void FileToBin(string path, nint owner)
    {
        var op = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new("3AD05575-8857-4850-9277-11B85BDB8E09"))!)!;
        try
        {
            Guid shellItem = typeof(IShellItem).GUID;
            SHCreateItemFromParsingName(path, 0, ref shellItem, out IShellItem item);
            try
            {
                var sink = new RecycleOnlySink();
                op.SetOwnerWindow(owner);
                op.SetOperationFlags(Silent | NoConfirmation | NoErrorUi | RecycleOnDelete | EarlyFailure | AddUndoRecord);
                op.DeleteItem(item, sink);
                op.PerformOperations();
                op.GetAnyOperationsAborted(out bool cancelled);
                if (cancelled || !sink.Recycled)
                    throw new IOException("Windows could not recycle this file. Check that the drive supports the Recycle Bin and that you have permission to remove the file.");
            }
            finally { Marshal.ReleaseComObject(item); }
        }
        catch (COMException ex)
        {
            throw new IOException("Windows could not recycle this file with your current permissions or Recycle Bin settings.", ex);
        }
        finally { Marshal.ReleaseComObject(op); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, nint bind, ref Guid iid, out IShellItem item);

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem { }

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
        void DeleteItem(IShellItem item, IFileOperationProgressSink sink);
        void DeleteItems(nint items);
        void NewItem(nint folder, uint attributes, nint name, nint template, nint sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }

    [ComImport, Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags, nint item, nint name);
        [PreserveSig] int PostRenameItem(uint flags, nint item, nint name, int result, nint created);
        [PreserveSig] int PreMoveItem(uint flags, nint item, nint destination, nint name);
        [PreserveSig] int PostMoveItem(uint flags, nint item, nint destination, nint name, int result, nint created);
        [PreserveSig] int PreCopyItem(uint flags, nint item, nint destination, nint name);
        [PreserveSig] int PostCopyItem(uint flags, nint item, nint destination, nint name, int result, nint created);
        [PreserveSig] int PreDeleteItem(uint flags, nint item);
        [PreserveSig] int PostDeleteItem(uint flags, nint item, int result, nint created);
        [PreserveSig] int PreNewItem(uint flags, nint destination, nint name);
        [PreserveSig] int PostNewItem(uint flags, nint destination, nint name, nint template, uint attributes, int result, nint created);
        [PreserveSig] int UpdateProgress(uint total, uint completed);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class RecycleOnlySink : IFileOperationProgressSink
    {
        private const uint DeleteRecycleIfPossible = 0x80;
        private const int Abort = unchecked((int)0x80004004);

        internal bool Recycled { get; private set; }

        // The Shell can otherwise downgrade a recycle request to permanent deletion.
        public int PreDeleteItem(uint flags, nint item) => (flags & DeleteRecycleIfPossible) != 0 ? 0 : Abort;

        public int PostDeleteItem(uint flags, nint item, int result, nint created)
        {
            Recycled = result >= 0 && created != 0;
            return 0;
        }

        public int StartOperations() => 0;
        public int FinishOperations(int result) => 0;
        public int PreRenameItem(uint flags, nint item, nint name) => 0;
        public int PostRenameItem(uint flags, nint item, nint name, int result, nint created) => 0;
        public int PreMoveItem(uint flags, nint item, nint destination, nint name) => 0;
        public int PostMoveItem(uint flags, nint item, nint destination, nint name, int result, nint created) => 0;
        public int PreCopyItem(uint flags, nint item, nint destination, nint name) => 0;
        public int PostCopyItem(uint flags, nint item, nint destination, nint name, int result, nint created) => 0;
        public int PreNewItem(uint flags, nint destination, nint name) => 0;
        public int PostNewItem(uint flags, nint destination, nint name, nint template, uint attributes, int result, nint created) => 0;
        public int UpdateProgress(uint total, uint completed) => 0;
        public int ResetTimer() => 0;
        public int PauseTimer() => 0;
        public int ResumeTimer() => 0;
    }
}
