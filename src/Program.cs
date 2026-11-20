namespace ImageChecker;

internal static class Program
{
    internal static readonly string[] Actions = ["Rename with today's date", "Rename with daily counter", "Change file format", "Resize by dimensions", "Resize to a file size limit", "Find duplicates"];
    internal static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".webp"];
    internal static bool Supported(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            if (args is ["--install"] or ["--uninstall"])
            {
                ShellMenu.Register(args[0] == "--install");
                return 0;
            }
            if (args.Length >= 2 && args[0] == "--shell" && int.TryParse(args[1], out int action))
            {
                ShellMenu.Listen(action);
                return 0;
            }
            var paths = args.Where(File.Exists).Where(Supported).ToArray();
            if (paths.Length == 0)
            {
                using var picker = new OpenFileDialog { Title = "Select images for ImageChecker", Multiselect = true, Filter = "Images|*.png;*.jpg;*.jpeg;*.webp" };
                if (picker.ShowDialog() != DialogResult.OK) return 0;
                paths = picker.FileNames;
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
