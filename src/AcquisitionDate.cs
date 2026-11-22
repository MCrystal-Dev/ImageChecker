using System.Runtime.InteropServices;

namespace ImageChecker;

internal static class AcquisitionDate
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { internal Guid Format; internal uint Id; }

    // The x64 PROPVARIANT contains a two-pointer union after its eight-byte header.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] internal ushort Type;
        [FieldOffset(8)] internal long FileTime;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetPropertyStoreFromParsingName(string path, nint context, uint flags, ref Guid id, out IPropertyStore store);
    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    internal static DateTime? Read(string path)
    {
        Guid id = typeof(IPropertyStore).GUID;
        if (SHGetPropertyStoreFromParsingName(path, 0, 0, ref id, out var store) < 0) return null;
        try
        {
            var key = new PropertyKey { Format = new("2CBAA8F5-D81F-47CA-B17A-F8D822300131"), Id = 100 };
            int result = store.GetValue(ref key, out var value);
            try { return result >= 0 && value.Type == 64 && value.FileTime > 0 ? DateTime.FromFileTime(value.FileTime) : null; }
            catch (ArgumentOutOfRangeException) { return null; }
            finally { PropVariantClear(ref value); }
        }
        finally { Marshal.ReleaseComObject(store); }
    }
}
