using ImageMagick;
using Microsoft.Win32;
using System.Globalization;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ImageChecker;

internal record Options(int Action, string Format, int Width, int Height, long ByteLimit, string? LimitFormat = null);
internal record Stamp(long Length, DateTime Modified)
{
    internal static Stamp Read(string path)
    {
        var file = new FileInfo(path);
        return new(file.Length, file.LastWriteTimeUtc);
    }
}
internal sealed record Change(string Source, string Target, Stamp Original, string Details, string? Staged = null, int Number = 0);
internal sealed record ApplyResult(List<Change> Completed, List<string> Errors)
{
    internal int Done => Completed.Count;
}
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
            string? exportFolder = null;
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
                        plan.Changes.Add(NumberedRename(src, stamp, plan.Day, ref number));
                        continue;
                    }
                    else
                    {
                        details = "Rename";
                        string outputStem;
                        if (options.Action == Program.PrefixImageDate)
                        {
                            var date = ImageMetadata.Read(src, includeFields: false).Date;
                            if (stem.StartsWith(date.Prefix + "_", StringComparison.Ordinal))
                            {
                                plan.Notes.Add($"{src} — Already starts with the detected date.");
                                continue;
                            }
                            outputStem = $"{date.Prefix}_{stem}";
                            details = date.Source;
                        }
                        else outputStem = options.Action switch
                        {
                            0 => $"{plan.Day}_{stem}", 2 => stem + "-converted", 3 => stem + "-resized",
                            4 => stem + "-limited", _ => throw new ArgumentException("Unknown action.")
                        };
                        if (options.Action == 2) ext = "." + options.Format.ToLowerInvariant();
                        if (options.Action == 4 && options.LimitFormat != null) ext = "." + options.LimitFormat.ToLowerInvariant();
                        bool export = options.Action is 2 or 3 or 4;
                        if (export) directory = exportFolder ??= ExportFolder(directory, create: false);
                        target = Path.Combine(directory, outputStem + ext);
                        for (int i = 2; Taken(target, used); i++)
                        {
                            collision = true;
                            target = Path.Combine(directory, $"{outputStem} ({i}){ext}");
                        }
                        if (export)
                        {
                            staged = Path.Combine(plan.Temp.FullName, Guid.NewGuid().ToString("N") + ext);
                            details = Encode(src, staged, options);
                        }
                    }
                    used.Add(target);
                    plan.Changes.Add(new(src, target, stamp, details + (collision ? " · name in use, next free name shown" : ""), staged));
                }
                catch (Exception ex) { plan.Notes.Add($"{src} — {ex.Message}"); }
            }
            return plan;
        }
        catch { plan.Dispose(); throw; }
    }
    private static bool Taken(string path, HashSet<string> used) => used.Contains(path) || Path.Exists(path);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateDirectoryW(string path, nint securityAttributes);

    private static string ExportFolder(string parent, bool create)
    {
        for (int number = 1; ; number++)
        {
            string path = Path.Combine(parent, number == 1 ? "ImageChecker Export" : $"ImageChecker Export ({number})");
            if (!create)
            {
                if (!Path.Exists(path)) return path;
                continue;
            }
            if (CreateDirectoryW(path, 0)) return path;
            int error = Marshal.GetLastWin32Error();
            if (error is not (80 or 183)) throw new IOException($"Could not create {path}.", new Win32Exception(error));
        }
    }

    internal static string OverwriteTarget(Change row) =>
        FormatOf(row.Source) == FormatOf(row.Staged!) ? row.Source : Path.ChangeExtension(row.Source, Path.GetExtension(row.Staged));

    internal static Change NumberedRename(string source, Stamp original, string day, ref int number)
    {
        int previous = number;
        string target;
        do
        {
            number = checked(number + 1);
            target = Path.Combine(Path.GetDirectoryName(source)!, $"{day.Replace('-', '_')}_{number:D3}{Path.GetExtension(source)}");
        } while (Path.Exists(target));
        return new(source, target, original, number == previous + 1 ? "Rename" : "Rename · name in use, next free name shown", Number: number);
    }

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
        if (options.Action == 4)
        {
            if (format != FormatOf(source) || new FileInfo(source).Length >= options.ByteLimit)
                return SizeLimitedEncoder.Write(image, dest, format, options.ByteLimit);
            File.Copy(source, dest);
            if (new FileInfo(dest).Length >= options.ByteLimit) throw new IOException("Source changed during preview. Refresh to try again.");
        }
        else
        {
            image.Quality = 88;
            image.Write(dest, format);
        }
        return $"{image.Width} × {image.Height} px · {new FileInfo(dest).Length:N0} bytes";
    }

    internal static ApplyResult Apply(Plan plan, int action, IReadOnlyList<Change>? selected = null, bool overwrite = false)
    {
        ApplyResult Run(RegistryKey? key)
        {
            if (action <= 1 && plan.Day != Today) throw new IOException("The date changed. Preview again.");
            if (key != null && (int)(key.GetValue(plan.Day) ?? 0) != plan.CounterStart)
                throw new IOException("The daily counter changed. Preview again for current names.");
            var errors = new List<string>();
            var completed = new List<Change>();
            string? exportFolder = null;
            foreach (var row in selected ?? plan.Changes)
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
                        string target;
                        if (overwrite) target = OverwriteTarget(row);
                        else
                        {
                            exportFolder ??= ExportFolder(Path.GetDirectoryName(Path.GetDirectoryName(row.Target)!)!, create: true);
                            target = Path.Combine(exportFolder, Path.GetFileName(row.Target));
                        }
                        string temp = Path.Combine(Path.GetDirectoryName(target)!, $".imagechecker-{Guid.NewGuid():N}.tmp");
                        try
                        {
                            File.Copy(row.Staged, temp);
                            bool samePath = Names.Equals(target, row.Source);
                            using var source = overwrite && !samePath
                                ? new FileStream(row.Source, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete) : null;
                            if (row.Original != Stamp.Read(row.Source)) throw new IOException("Source changed since preview.");
                            File.Move(temp, target, overwrite && samePath);
                            if (overwrite && !samePath)
                            {
                                try { File.Delete(row.Source); }
                                catch (Exception ex) { throw new IOException($"Converted image saved to {target}. Could not remove the original. {ex.Message}", ex); }
                            }
                        }
                        finally { File.Delete(temp); }
                        completed.Add(row with { Target = target });
                        continue;
                    }
                    completed.Add(row);
                }
                catch (Exception ex)
                {
                    errors.Add($"{row.Source} — {ex.Message}");
                    if (key != null) break;
                }
            }
            return new(completed, errors);
        }
        if (action == 1) return Locked(k => Run(k));
        if (!overwrite) return Run(null);
        using var gate = new Mutex(false, @"Local\MCrystal.ImageChecker.Overwrite");
        try { if (!gate.WaitOne(30000)) throw new IOException("Another overwrite is still running. Try again."); }
        catch (AbandonedMutexException) { }
        try { return Run(null); }
        finally { gate.ReleaseMutex(); }
    }

    internal static string Hash(Stream input) => Convert.ToHexString(SHA256.HashData(input));
}
