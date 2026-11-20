using ImageChecker;
using ImageMagick;
using Microsoft.Win32;
using System.Runtime.InteropServices;

internal static class Checks
{
    [DllImport("advapi32.dll")] private static extern int RegOverridePredefKey(nint key, nint replacement);
    private static void Check(bool condition, string detail)
    {
        if (!condition) throw new InvalidOperationException(detail);
    }

    [STAThread]
    private static void Main(string[] args)
    {
        if (args is ["--explorer"]) { ExplorerChecks.Run(); return; }
        if (args is ["--review"]) { ReviewChecks.Run(); return; }
        var folder = Directory.CreateTempSubdirectory("ImageChecker-checks-");
        string keyName = @"Software\ImageChecker-checks-" + Guid.NewGuid().ToString("N");
        using var sandbox = Registry.CurrentUser.CreateSubKey(keyName);
        var hkcu = new nint(unchecked((int)0x80000001));
        Check(RegOverridePredefKey(hkcu, sandbox.Handle.DangerousGetHandle()) == 0, "Counter registry isolation failed.");
        try
        {
            string src = Path.Combine(folder.FullName, "photo.png");
            var pixels = new byte[320 * 240 * 3];
            new Random(47).NextBytes(pixels);
            using (var img = new MagickImage(pixels, new PixelReadSettings(320, 240, StorageType.Char, PixelMapping.RGB))) img.Write(src);
            string copy = Path.Combine(folder.FullName, "second.png");
            File.Copy(src, copy);
            var notes = new List<string>();
            Check(Operations.Duplicates([src], notes).Single().Value.Count == 2 && notes.Count == 0, "Folder duplicate scan");
            var settings = new Options(2, false, "JPG", 80, 80, 5000);
            foreach (var (mode, format) in new[] { (2, "JPG"), (2, "WebP"), (2, "PNG"), (3, "PNG"), (4, "PNG") })
            {
                using var plan = Operations.Preview([src], settings with { Action = mode, Format = format });
                Check(plan.Notes.Count == 0, string.Join("\n", plan.Notes));
                var row = plan.Changes.Single();
                Check(!File.Exists(row.Target), "Preview changed disk");
                Check(Operations.Apply(plan, mode).Done == 1, "Export failed");
                using var result = new MagickImage(row.Target);
                if (mode == 2) Check(result.Format == Operations.FormatOf(row.Target) || format == "PNG" && result.Format.ToString().StartsWith("Png"), "Wrong output format");
                if (mode == 3) Check(result.Width == 80 && result.Height == 60, "Resize bounds");
                if (mode == 4) Check(new FileInfo(row.Target).Length <= settings.ByteLimit, "Size cap");
            }
            using var conflict = Operations.Preview([src], settings);
            File.WriteAllText(conflict.Changes.Single().Target, "occupied");
            Check(Operations.Apply(conflict, 2).Done == 0 && File.ReadAllText(conflict.Changes.Single().Target) == "occupied", "Overwrite protection");
            File.WriteAllText(Path.Combine(folder.FullName, "001.png"), "occupied");
            using var batch = Operations.Preview([src, copy], settings with { Action = 1 });
            using var stale = Operations.Preview([src], settings with { Action = 1 });
            Check(batch.Changes.Select(c => Path.GetFileName(c.Target)).SequenceEqual(["002.png", "003.png"]), "Counter collision");
            Check(Operations.Apply(batch, 1).Done == 2, "Counter apply");
            try { Operations.Apply(stale, 1); throw new InvalidOperationException("Stale counter accepted"); } catch (IOException) { }
            using var next = Operations.Preview([batch.Changes[0].Target], settings with { Action = 1, WithDate = true });
            Check(Path.GetFileName(next.Changes.Single().Target) == Operations.Today + "_004.png", "Saved shared counter");
            using var dated = Operations.Preview([batch.Changes[0].Target], settings with { Action = 0 });
            Check(Operations.Apply(dated, 0).Done == 1 && File.Exists(Path.Combine(folder.FullName, Operations.Today + "_002.png")), "Date rename");
            Console.WriteLine("Passed conversion, resize, file size limit, duplicate scan, safe preview, collision and counter checks.");
        }
        finally
        {
            RegOverridePredefKey(hkcu, 0);
            Registry.CurrentUser.DeleteSubKeyTree(keyName);
            folder.Delete(true);
        }
    }
}
