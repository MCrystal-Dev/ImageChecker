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
        if (args is ["--benchmark"]) { ScanBenchmark.Run(); return; }
        if (args is ["--explorer"]) { ExplorerChecks.Run(); return; }
        if (args is ["--review"] or ["--layout"])
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetDefaultFont(new Font("Segoe UI", 10));
            ImageChecker.Appearance.SetWindowsColorMode();
            using var context = new ApplicationContext();
            Exception? failure = null;
            void TestOnIdle(object? sender, EventArgs e)
            {
                Application.Idle -= TestOnIdle;
                try { if (args[0] == "--layout") LayoutChecks.Run(); else ReviewChecks.Run(); }
                catch (Exception ex) { failure = ex; }
                finally { context.ExitThread(); }
            }
            Application.Idle += TestOnIdle;
            Application.Run(context);
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            return;
        }
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
            CheckSizeLimits(src);
            string copy = Path.Combine(folder.FullName, "second.png");
            File.Copy(src, copy);
            string converted = Path.Combine(folder.FullName, "small.jpg");
            using (var image = new MagickImage(src)) { image.Resize(160, 120); image.Quality = 76; image.Write(converted); }
            using (var blue = new MagickImage(MagickColors.Blue, 320, 240)) blue.Write(Path.Combine(folder.FullName, "blue.png"));
            var review = new DuplicateReview([src]);
            Check(review.Current is { Exact: true } && review.Current.Right.Path == copy && review.Notes.Count == 0, "Exact copies first: " + string.Join("\n", review.Notes));
            review.Advance();
            var similar = review.Current;
            Check(similar is { Exact: false } && similar.Right.Path == converted, "Resized and re-encoded match");
            File.WriteAllText(converted, "changed");
            try { similar!.RecycleSide(true, 0); throw new Exception("Accepted a changed keeper"); } catch (IOException) { }
            try { similar!.RecycleSide(false, 0); throw new Exception("Accepted a changed target"); } catch (IOException) { }
            review.Advance();
            Check(review.Current == null && File.Exists(src) && File.Exists(converted), "Unrelated image or unsafe recycle");
            var settings = new Options(2, "JPG", 80, 80, 5000);
            foreach (var (mode, format) in new[] { (2, "JPG"), (2, "WebP"), (2, "PNG"), (3, "PNG"), (4, "PNG") })
            {
                using var plan = Operations.Preview([src], settings with { Action = mode, Format = format });
                Check(plan.Notes.Count == 0, string.Join("\n", plan.Notes));
                var row = plan.Changes.Single();
                Check(!Directory.Exists(Path.GetDirectoryName(row.Target)), "Preview created export folder");
                Check(Operations.Apply(plan, mode).Done == 1, "Export failed");
                using var result = new MagickImage(row.Target);
                if (mode == 2) Check(result.Format == Operations.FormatOf(row.Target) || format == "PNG" && result.Format.ToString().StartsWith("Png"), "Wrong output format");
                if (mode == 3) Check(result.Width == 80 && result.Height == 60, "Resize bounds");
                if (mode == 4) Check(new FileInfo(row.Target).Length <= settings.ByteLimit, "Size cap");
            }
            using var conflict = Operations.Preview([src], settings);
            Directory.CreateDirectory(Path.GetDirectoryName(conflict.Changes.Single().Target)!);
            File.WriteAllText(conflict.Changes.Single().Target, "occupied");
            var exports = Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => Operations.Apply(conflict, 2)))).GetAwaiter().GetResult();
            Check(exports.All(export => export.Done == 1) && exports.Select(export => export.Completed.Single().Target).Distinct().Count() == 2
                && File.ReadAllText(conflict.Changes.Single().Target) == "occupied", "Fresh folder per export");
            string occupied = Path.ChangeExtension(src, ".jpg");
            File.WriteAllText(occupied, "another file");
            var original = File.ReadAllBytes(src);
            Check(Operations.Apply(conflict, 2, overwrite: true).Done == 0 && original.SequenceEqual(File.ReadAllBytes(src))
                && File.ReadAllText(occupied) == "another file", "Conversion overwrite collision");
            using var resize = Operations.Preview([copy], settings with { Action = 3 });
            var rewritten = Operations.Apply(resize, 3, overwrite: true);
            Check(rewritten.Done == 1 && rewritten.Completed[0].Target == copy, "In-place overwrite: " + string.Join(", ", resize.Notes.Concat(rewritten.Errors)));
            using (var resized = new MagickImage(copy)) Check(resized.Width == 80 && resized.Height == 60, "Overwritten dimensions");
            Check(Operations.Apply(resize, 3, overwrite: true).Done == 0, "Stale overwrite protection");
            string prefix = DateTime.Today.ToString("yyyy_MM_dd", System.Globalization.CultureInfo.InvariantCulture) + "_";
            File.WriteAllText(Path.Combine(folder.FullName, prefix + "001.png"), "occupied");
            using var batch = Operations.Preview([src, copy], settings with { Action = 1 });
            using var stale = Operations.Preview([src], settings with { Action = 1 });
            Check(batch.Changes.Select(c => Path.GetFileName(c.Target)).SequenceEqual([prefix + "002.png", prefix + "003.png"]), "Counter collision");
            Check(Operations.Apply(batch, 1).Done == 2, "Counter apply");
            try { Operations.Apply(stale, 1); throw new InvalidOperationException("Stale counter accepted"); } catch (IOException) { }
            using var next = Operations.Preview([batch.Changes[0].Target], settings with { Action = 1 });
            Check(Path.GetFileName(next.Changes.Single().Target) == prefix + "004.png", "Saved shared counter");
            using var dated = Operations.Preview([batch.Changes[0].Target], settings with { Action = 0 });
            Check(Operations.Apply(dated, 0).Done == 1 && File.Exists(Path.Combine(folder.FullName, Operations.Today + "_" + prefix + "002.png")), "Date rename");
            string[] extensions = [".png", ".JPG", ".jpeg", ".webp", ".PNG"];
            var mixed = extensions.Select((ext, i) => Path.Combine(folder.FullName, $"sample-{i}{ext}")).ToArray();
            using (var img = new MagickImage(MagickColors.Blue, 8, 8))
                foreach (var file in mixed) img.Write(file);
            var originals = mixed.Select(File.ReadAllBytes).ToArray();
            using var numbered = Operations.Preview(mixed, settings with { Action = 1 });
            Check(Operations.Apply(numbered, 1).Done == mixed.Length, "Mixed-format numbered rename");
            for (int i = 0; i < mixed.Length; i++)
            {
                string output = Path.Combine(folder.FullName, $"{prefix}{i + 4:D3}{extensions[i]}");
                Check(File.ReadAllBytes(output).SequenceEqual(originals[i]), "Dated number, extension and file contents");
            }
            Console.WriteLine("Passed conversion, resize, file size limit, duplicate scan, safe preview, collision and counter checks.");
        }
        finally
        {
            RegOverridePredefKey(hkcu, 0);
            Registry.CurrentUser.DeleteSubKeyTree(keyName);
            folder.Delete(true);
        }
    }

    private static void CheckSizeLimits(string source)
    {
        var options = new Options(4, "PNG", 0, 0, 10_000);
        foreach (var format in new[] { "JPG", "WebP", "PNG" })
        {
            var codec = Operations.FormatOf("image." + format);
            long cap = codec == MagickFormat.Png ? 10_000 : 50_000;
            using var plan = Operations.Preview([source], options with { LimitFormat = format, ByteLimit = cap });
            Check(plan.Notes.Count == 0, string.Join("\n", plan.Notes));
            var row = plan.Changes.Single();
            var applied = Operations.Apply(plan, 4).Completed.Single();
            byte[] bytes = File.ReadAllBytes(applied.Target);
            Check(bytes.SequenceEqual(File.ReadAllBytes(row.Staged!)), "Export must match preview");
            using var decoded = new MagickImage(bytes);
            Check(Path.GetExtension(row.Target) == "." + format.ToLowerInvariant() && (codec == MagickFormat.Png
                ? decoded.Format.ToString().StartsWith("Png") : decoded.Format == codec), "Wrong size-limit format");
            Check(bytes.Length < cap && bytes.Length >= cap * 0.94, $"Poor budget use for {format}: {bytes.Length}/{cap}");
            if (codec != MagickFormat.Png) Check(decoded.Width == 320 && decoded.Height == 240, "Unnecessary downscale");
            Console.WriteLine($"{format}: {bytes.Length:N0} bytes under a {cap:N0}-byte limit, {decoded.Width}×{decoded.Height}.");
        }
        using var unchanged = Operations.Preview([source], options with { ByteLimit = 1_000_000 });
        Check(File.ReadAllBytes(unchanged.Changes.Single().Staged!).SequenceEqual(File.ReadAllBytes(source)), "Under-limit original must be preserved");
        long exactLength = new FileInfo(source).Length;
        using var boundary = Operations.Preview([source], options with { ByteLimit = exactLength });
        Check(new FileInfo(boundary.Changes.Single().Staged!).Length < exactLength, "Output must be strictly below the limit");
        using var convertSmall = Operations.Preview([source], options with { LimitFormat = "WebP", ByteLimit = 1_000_000 });
        using (var image = new MagickImage(convertSmall.Changes.Single().Staged!))
            Check(image.Format == MagickFormat.WebP, "Under-limit conversion must encode the requested format");
        using var impossible = Operations.Preview([source], options with { ByteLimit = 1 });
        Check(impossible.Changes.Count == 0 && impossible.Notes.Count == 1, "Impossible byte limit must skip the file");
    }
}
