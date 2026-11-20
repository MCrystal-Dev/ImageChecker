using ImageMagick;
using Microsoft.Win32;
using System.Globalization;
using System.Security.Cryptography;

namespace ImageChecker;

internal record Options(int Action, bool WithDate, string Format, int Width, int Height, long ByteLimit);
internal record Stamp(long Length, DateTime Modified)
{
    internal static Stamp Read(string path)
    {
        var file = new FileInfo(path);
        return new(file.Length, file.LastWriteTimeUtc);
    }
}
internal sealed record Change(string Source, string Target, Stamp Original, string Details, string? Staged = null, int Number = 0);
internal sealed class Plan : IDisposable
{
    internal readonly List<Change> Changes = [];
    internal readonly List<string> Notes = [];
    internal readonly string Day = Operations.Today;
    internal int CounterStart;
    internal readonly DirectoryInfo Temp = Directory.CreateTempSubdirectory("ImageChecker-");
    public void Dispose() { try { Temp.Delete(true); } catch (IOException) { } }
}

internal static class Operations
{
    internal static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
    internal static string Today => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private const string CounterKey = @"Software\MCrystal-Dev\ImageChecker\Counters";

    private static T Locked<T>(Func<RegistryKey, T> work)
    {
        using var gate = new Mutex(false, @"Local\MCrystal.ImageChecker.Renames");
        try { if (!gate.WaitOne(30000)) throw new IOException("Another rename is still running. Try again."); }
        catch (AbandonedMutexException) { }
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(CounterKey);
            // Finish an interrupted counter update only if its file move completed.
            if (key.GetValue("Pending") is string[] p && p.Length == 4)
            {
                if (!File.Exists(p[1]) && File.Exists(p[2])) key.SetValue(p[0], int.Parse(p[3], CultureInfo.InvariantCulture));
                key.DeleteValue("Pending");
            }
            return work(key);
        }
        finally { gate.ReleaseMutex(); }
    }

    internal static Plan Preview(IEnumerable<string> paths, Options options)
    {
        var plan = new Plan();
        try
        {
            int number = options.Action == 1 ? Locked(k => (int)(k.GetValue(plan.Day) ?? 0)) : 0;
            plan.CounterStart = number;
            var used = new HashSet<string>(Names);
            foreach (var src in paths.Distinct(Names))
            {
                try
                {
                    var stamp = Stamp.Read(src);
                    string directory = Path.GetDirectoryName(src)!;
                    string ext = Path.GetExtension(src);
                    string stem = Path.GetFileNameWithoutExtension(src);
                    string target, details;
                    string? staged = null;
                    bool collision = false;
                    if (options.Action == 1)
                    {
                        do
                        {
                            number = checked(number + 1);
                            target = Path.Combine(directory, $"{(options.WithDate ? plan.Day + "_" : "")}{number:D3}{ext}");
                            if (!Taken(target, used)) break;
                            collision = true;
                        } while (true);
                        details = "Rename";
                    }
                    else
                    {
                        string suffix = options.Action switch { 0 => "", 2 => "-converted", 3 => "-resized", 4 => "-limited", _ => throw new ArgumentException("Unknown action.") };
                        string outputStem = options.Action == 0 ? $"{plan.Day}_{stem}" : stem + suffix;
                        if (options.Action == 2) ext = "." + options.Format.ToLowerInvariant();
                        target = Path.Combine(directory, outputStem + ext);
                        for (int i = 2; Taken(target, used); i++)
                        {
                            collision = true;
                            target = Path.Combine(directory, $"{outputStem} ({i}){ext}");
                        }
                        details = "Rename";
                        if (options.Action >= 2)
                        {
                            staged = Path.Combine(plan.Temp.FullName, Guid.NewGuid().ToString("N") + ext);
                            details = Encode(src, staged, options);
                        }
                    }
                    used.Add(target);
                    plan.Changes.Add(new(src, target, stamp, details + (collision ? " · name in use, next free name shown" : ""), staged, options.Action == 1 ? number : 0));
                }
                catch (Exception ex) { plan.Notes.Add($"{src} — {ex.Message}"); }
            }
            return plan;
        }
        catch { plan.Dispose(); throw; }
    }
    private static bool Taken(string path, HashSet<string> used) => used.Contains(path) || Path.Exists(path);

    internal static MagickFormat FormatOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => MagickFormat.Jpeg, ".png" => MagickFormat.Png, ".webp" => MagickFormat.WebP,
        _ => throw new InvalidDataException("Choose a PNG, JPG, or WebP image.")
    };

    private static string Encode(string source, string dest, Options options)
    {
        var settings = new MagickReadSettings { Format = FormatOf(source) };
        using var frames = new MagickImageCollection();
        frames.Ping(source, settings);
        if (frames.Count != 1) throw new InvalidDataException("Animated or multi-frame image skipped.");
        using var image = new MagickImage(source, settings);
        image.AutoOrient();
        var format = FormatOf(dest);
        if (format == MagickFormat.Jpeg && image.HasAlpha)
        {
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaAction.Remove);
        }
        if (options.Action == 3)
        {
            var area = new MagickGeometry((uint)options.Width, (uint)options.Height) { Greater = true };
            image.Resize(area);
        }
        if (options.Action == 4 && new FileInfo(source).Length <= options.ByteLimit)
            File.Copy(source, dest);
        else
        {
            image.Quality = 88;
            while (true)
            {
                image.Write(dest, format);
                long length = new FileInfo(dest).Length;
                if (options.Action != 4 || length <= options.ByteLimit) break;
                image.Strip();
                if (image.Quality > 60 && format != MagickFormat.Png) { image.Quality -= 14; continue; }
                if (image.Width == 1 && image.Height == 1) throw new IOException("Size limit is too small for this format.");
                double scale = Math.Min(0.88, 0.96 * Math.Sqrt((double)options.ByteLimit / length));
                image.Resize(Math.Max(1, (uint)(image.Width * scale)), Math.Max(1, (uint)(image.Height * scale)));
            }
        }
        return $"{image.Width} × {image.Height} px · {new FileInfo(dest).Length:N0} bytes";
    }

    internal static (int Done, List<string> Errors) Apply(Plan plan, int action)
    {
        (int, List<string>) Run(RegistryKey? key)
        {
            if (action <= 1 && plan.Day != Today) throw new IOException("The date changed. Preview again.");
            if (key != null && (int)(key.GetValue(plan.Day) ?? 0) != plan.CounterStart)
                throw new IOException("The daily counter changed. Preview again for current names.");
            var errors = new List<string>();
            int done = 0;
            foreach (var row in plan.Changes)
            {
                try
                {
                    if (row.Original != Stamp.Read(row.Source)) throw new IOException("Source changed since preview.");
                    if (row.Staged == null)
                    {
                        key?.SetValue("Pending", new[] { plan.Day, row.Source, row.Target, row.Number.ToString(CultureInfo.InvariantCulture) });
                        File.Move(row.Source, row.Target);
                        key?.SetValue(plan.Day, row.Number);
                        key?.DeleteValue("Pending");
                    }
                    else
                    {
                        string temp = Path.Combine(Path.GetDirectoryName(row.Target)!, $".imagechecker-{Guid.NewGuid():N}.tmp");
                        try { File.Copy(row.Staged, temp); File.Move(temp, row.Target); }
                        finally { File.Delete(temp); }
                    }
                    done++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{row.Source} — {ex.Message}");
                    if (key != null) break;
                }
            }
            return (done, errors);
        }
        return action == 1 ? Locked(k => Run(k)) : Run(null);
    }

    internal static string Hash(Stream input) => Convert.ToHexString(SHA256.HashData(input));
    internal static Dictionary<string, List<string>> Duplicates(string[] selected, List<string> notes)
    {
        var selectedSet = selected.ToHashSet(Names);
        var sizes = new HashSet<long>();
        foreach (var path in selectedSet)
            try { sizes.Add(new FileInfo(path).Length); } catch (Exception ex) { notes.Add($"{path} — {ex.Message}"); }
        var groups = new Dictionary<string, List<string>>();
        foreach (var dir in selected.Select(p => Path.GetDirectoryName(p)!).Distinct(Names))
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(dir).Where(Program.Supported))
                {
                    try
                    {
                        var entry = new FileInfo(path);
                        if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint) || !sizes.Contains(entry.Length)) continue;
                        using var stream = File.OpenRead(path);
                        var hash = Hash(stream);
                        if (!groups.TryGetValue(hash, out var group)) groups[hash] = group = [];
                        group.Add(path);
                    }
                    catch (Exception ex) { notes.Add($"{path} — {ex.Message}"); }
                }
            }
            catch (Exception ex) { notes.Add($"{dir} — {ex.Message}"); }
        }
        return groups.Where(g => g.Value.Count > 1 && g.Value.Any(selectedSet.Contains)).ToDictionary();
    }
}
