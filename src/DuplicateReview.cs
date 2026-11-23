using ImageMagick;

namespace ImageChecker;

internal sealed record ReviewImage(string Path, string Hash, long Length, uint Width, uint Height, byte[]? Sample)
{
    internal string Details => (Width == 0 ? "Preview unavailable" : $"{Width} × {Height} px") + $" · {Length:N0} bytes";
}

internal sealed record ImagePair(ReviewImage Left, ReviewImage Right)
{
    internal bool Exact => Left.Hash == Right.Hash;

    internal void RecycleSide(bool left, nint owner)
    {
        var (remove, keep) = left ? (Left, Right) : (Right, Left);
        using var keeper = File.OpenRead(keep.Path);
        using var target = new FileStream(remove.Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (Operations.Hash(keeper) != keep.Hash || Operations.Hash(target) != remove.Hash)
            throw new IOException("An image changed since the scan. Refresh to compare the current files.");
        Recycle.FileToBin(remove.Path, owner);
    }
}

internal sealed class DuplicateReview
{
    private readonly HashSet<string> removed = new(Operations.Names);
    private readonly IEnumerator<ImagePair> pairs;
    private CancellationToken searchCancellation;
    internal ImagePair? Current { get; private set; }
    internal List<string> Notes { get; } = [];
    internal int Reviewed { get; private set; }

    internal DuplicateReview(string[] sources, CancellationToken cancellation = default)
    {
        var selected = sources.ToHashSet(Operations.Names);
        var images = new List<ReviewImage>();
        var samples = new Dictionary<(string Hash, MagickFormat Format), ReviewImage>();
        foreach (var folder in sources.Select(Path.GetDirectoryName).Distinct(Operations.Names))
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                foreach (string path in Directory.EnumerateFiles(folder!).Where(Program.Supported).Order(Operations.Names))
                {
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                        if (!File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) images.Add(ReadImage(path, samples));
                    }
                    catch (Exception ex) { Notes.Add($"{path} — {ex.Message}"); }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { Notes.Add($"{folder} — {ex.Message}"); }
        }
        pairs = FindPairs(images, selected).GetEnumerator();
        Advance(cancellation: cancellation);
    }

    private ReviewImage ReadImage(string path, Dictionary<(string Hash, MagickFormat Format), ReviewImage> samples)
    {
        using var file = File.OpenRead(path);
        var key = (Hash: Operations.Hash(file), Format: Operations.FormatOf(path));
        if (samples.TryGetValue(key, out var known)) return known with { Path = path };
        var result = new ReviewImage(path, key.Hash, file.Length, 0, 0, null);
        try
        {
            file.Position = 0;
            var settings = new MagickReadSettings { Format = key.Format, FrameCount = 2 };
            using var frames = new MagickImageCollection();
            frames.Ping(file, settings);
            if (frames.Count != 1) return samples[key] = result with { Width = frames[0].Width, Height = frames[0].Height };
            file.Position = 0;
            settings.FrameCount = 1;
            using var image = new MagickImage(file, settings);
            image.AutoOrient();
            result = result with { Width = image.Width, Height = image.Height };
            image.ColorSpace = ColorSpace.sRGB;
            image.Resize(new MagickGeometry(24, 24) { IgnoreAspectRatio = true });
            image.Depth = 8;
            byte[] rgba = image.ToByteArray(MagickFormat.Rgba);
            for (int i = 0; i < rgba.Length; i += 4)
                for (int channel = i; channel < i + 3; channel++)
                    rgba[channel] = (byte)(rgba[channel] * rgba[i + 3] / 255);
            return samples[key] = result with { Sample = rgba };
        }
        catch (MagickException ex) { Notes.Add($"{path} — {ex.Message}"); return result; }
    }

    private IEnumerable<ImagePair> FindPairs(List<ReviewImage> images, HashSet<string> selected)
    {
        var anchors = images.Where(i => selected.Contains(i.Path)).ToArray();
        var byHash = images.ToLookup(i => i.Hash);
        // Show exact copies first. Generate pairs on demand, even for a folder of identical images.
        foreach (bool exact in new[] { true, false })
            foreach (var left in anchors)
            {
                if (removed.Contains(left.Path)) continue;
                IEnumerable<ReviewImage> candidates = exact ? byHash[left.Hash] : images;
                foreach (var right in candidates)
                {
                    searchCancellation.ThrowIfCancellationRequested();
                    if (removed.Contains(left.Path)) break;
                    if (removed.Contains(right.Path) || selected.Contains(right.Path) && Operations.Names.Compare(left.Path, right.Path) >= 0) continue;
                    if (exact || left.Hash != right.Hash && Similar(left, right)) yield return new(left, right);
                }
            }
    }

    private static bool Similar(ReviewImage left, ReviewImage right)
    {
        if (left.Sample is not {} a || right.Sample is not {} b) return false;
        double ratio = (double)left.Width * right.Height / ((double)right.Width * left.Height);
        if (ratio < 1 / 1.03 || ratio > 1.03) return false;
        // Allow small encoding and resampling differences in corresponding color and alpha values.
        int budget = a.Length * 10 * 10;
        for (int i = 0; i < a.Length; i++)
        {
            int delta = a[i] - b[i];
            budget -= delta * delta;
            if (budget < 0) return false;
        }
        return true;
    }

    internal void Advance(string? recycledPath = null, CancellationToken cancellation = default)
    {
        if (Current != null) Reviewed++;
        if (recycledPath != null) removed.Add(recycledPath);
        searchCancellation = cancellation;
        try { Current = pairs.MoveNext() ? pairs.Current : null; }
        finally { searchCancellation = default; }
    }
}
