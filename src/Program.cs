namespace ImageChecker;

internal static class Program
{
    internal const int ViewMetadata = 6, PrefixImageDate = 7;
    internal static readonly string[] Actions = ["Add todays date to image", "Rename with date and iterating number", "Change file format", "Resize by dimensions", "Resize to a file size limit", "Find duplicates", "View image metadata", "Add detected date to filename"];
    internal static bool IsRename(int mode) => mode is 0 or 1 or PrefixImageDate;
    internal static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".webp"];
    internal static bool Supported(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    internal static string[] ResolveImages(IEnumerable<string> selection)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 };
        var children = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        try
        {
            foreach (string path in selection)
            {
                string full = Path.GetFullPath(path);
                if (Directory.Exists(full))
                    folders.Push(full);
                else if (Supported(full) && File.Exists(full))
                    files.Add(full);
            }
            while (folders.TryPop(out string? folder))
            {
                if (!visited.Add(Path.TrimEndingDirectorySeparator(folder))) continue;
                try
                {
                    files.UnionWith(Directory.EnumerateFiles(folder, "*", entries).Where(Supported));
                    foreach (var child in Directory.EnumerateDirectories(folder, "*", children)) folders.Push(child);
                }
                catch (DirectoryNotFoundException) { /* A subfolder was moved while scanning. */ }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, "Could not read the selection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return [];
        }
        if (files.Count == 0)
            MessageBox.Show("No PNG, JPG, JPEG or WebP images were found in the selection or its subfolders.", "ImageChecker");
        return files.ToArray();
    }

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Appearance.SetWindowsColorMode();
        try
        {
            if (args is ["--install"] or ["--uninstall"])
            {
                ShellMenu.Register(args[0] == "--install");
                return 0;
            }
            ImageResources.Configure();
            if (args.Length >= 2 && args[0] == "--shell" && int.TryParse(args[1], out int action))
            {
                ShellMenu.Listen(action);
                return 0;
            }
            string[] paths;
            if (args.Length == 0)
            {
                using var picker = new OpenFileDialog { Title = "Select images for ImageChecker", Multiselect = true, Filter = "Images|*.png;*.jpg;*.jpeg;*.webp" };
                if (picker.ShowDialog() != DialogResult.OK) return 0;
                paths = picker.FileNames;
            }
            else
            {
                paths = ResolveImages(args);
                if (paths.Length == 0) return 0;
            }
            Application.Run(new Workspace(paths, 0));
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "ImageChecker", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
