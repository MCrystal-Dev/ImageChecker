using System.Diagnostics;
using ImageChecker;
using ImageMagick;

internal static class ScanBenchmark
{
    internal static void Run()
    {
        var folder = Directory.CreateTempSubdirectory("ImageChecker-benchmark-");
        try
        {
            var random = new Random(42);
            var pixels = new byte[1280 * 800 * 3];
            for (int i = 0; i < 6; i++)
            {
                random.NextBytes(pixels);
                using var image = new MagickImage(pixels, new PixelReadSettings(1280, 800, StorageType.Char, PixelMapping.RGB));
                string source = Path.Combine(folder.FullName, $"{i}-0.jpg");
                image.Write(source);
                for (int j = 1; j < 8; j++) File.Copy(source, Path.Combine(folder.FullName, $"{i}-{j}.jpg"));
            }
            string[] paths = Directory.GetFiles(folder.FullName);
            var times = new List<long>();
            for (int run = 0; run < 4; run++)
            {
                var watch = Stopwatch.StartNew();
                var scan = new DuplicateReview(paths);
                watch.Stop();
                if (scan.Current?.Exact != true || scan.Notes.Count != 0) throw new Exception("Invalid benchmark result.");
                if (run > 0) times.Add(watch.ElapsedMilliseconds);
            }
            Console.WriteLine($"48 JPEGs, six distinct images, median time to first pair: {times.Order().ElementAt(1)} ms ({string.Join(", ", times)} ms).");
        }
        finally { folder.Delete(true); }
    }
}
