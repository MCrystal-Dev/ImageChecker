using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;

namespace ImageChecker;

internal static class AdminReview
{
    internal const string Switch = "--admin-duplicate-review";

    internal static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    internal static FileStream CreateSelection(string[] files)
    {
        var selection = new FileStream(Path.Combine(Path.GetTempPath(), $"ImageChecker-review-{Guid.NewGuid():N}.json"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete, 4096, FileOptions.DeleteOnClose);
        try
        {
            JsonSerializer.Serialize(selection, files);
            selection.Flush();
            return selection;
        }
        catch
        {
            selection.Dispose();
            throw;
        }
    }

    internal static string[] ReadSelection(string path)
    {
        using var selection = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var files = JsonSerializer.Deserialize<string[]>(selection);
        if (files is not { Length: > 0 } ||
            files.Any(file => string.IsNullOrEmpty(file) || !Path.IsPathFullyQualified(file) || !Program.Supported(file)))
            throw new InvalidDataException("The duplicate-review selection could not be restored.");
        return files;
    }

    internal static async Task OpenAsync(string[] files)
    {
        using var selection = CreateSelection(files);
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = true,
            Verb = "runas"
        };
        start.ArgumentList.Add(Switch);
        start.ArgumentList.Add(selection.Name);
        using var process = Process.Start(start) ?? throw new IOException("Windows could not start the administrator review.");
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(20))
        {
            process.Refresh();
            if (process.HasExited) break;
            if (process.MainWindowTitle.StartsWith("ImageChecker —", StringComparison.Ordinal)) return;
            await Task.Delay(100);
        }
        throw new IOException("The administrator review did not open. Your current review is still available.");
    }
}
