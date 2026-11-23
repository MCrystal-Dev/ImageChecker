using System.Runtime.InteropServices;
using ImageChecker;
using ImageMagick;

internal static class LayoutChecks
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { internal int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hwnd, uint message, nint dpi, ref NativeRect bounds);

    private static IEnumerable<Control> Children(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(child => Children(child).Prepend(child));

    internal static void Run()
    {
        var dir = Directory.CreateTempSubdirectory("ImageChecker-layout-");
        try
        {
            string first = Path.Combine(dir.FullName, "original.png"), second = Path.Combine(dir.FullName, "copy.png");
            using (var image = new MagickImage(MagickColors.Teal, 32, 32)) image.Write(first);
            File.Copy(first, second);
            using var form = new Workspace([first, second], 2);
            form.Show();
            Application.DoEvents();
            if (!Screen.FromControl(form).WorkingArea.Contains(form.Bounds)) throw new Exception("Window opened outside its monitor.");
            var controls = Children(form).ToArray();
            var refresh = controls.OfType<Button>().Single(b => b.Text == "Refresh");
            var action = controls.OfType<ComboBox>().Single(c => c.Items.Count == Program.Actions.Length);
            var count = controls.OfType<Label>().Single(label => label.AccessibleName == "Image selection count");
            foreach (var (width, height, dpi) in new[] { (800, 600, 96), (1024, 768, 120), (1280, 720, 144), (1280, 720, 192), (1920, 1080, 144), (3840, 2160, 192), (1024, 768, 96) })
            {
                // Reserve room for a taskbar. Send the same DPI notification a monitor switch produces.
                var size = new Size(Math.Min(width, 1060 * dpi / 96), Math.Min(height - 48, 735 * dpi / 96));
                if (dpi != form.DeviceDpi)
                {
                    var rect = new NativeRect { Right = size.Width, Bottom = size.Height };
                    SendMessage(form.Handle, 0x02E0, (dpi << 16) | dpi, ref rect);
                    Application.DoEvents();
                }
                form.Size = size;
                if (form.DeviceDpi != dpi || form.Width > width || form.Height > height - 48)
                    throw new Exception($"Window exceeds {width}×{height} or failed to adopt {dpi} DPI (was {form.DeviceDpi}).");
                foreach (int mode in Enumerable.Range(0, Program.Actions.Length))
                {
                    action.SelectedIndex = mode;
                    var deadline = DateTime.UtcNow.AddSeconds(15);
                    while (!refresh.Enabled && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(5); }
                    if (!refresh.Enabled) throw new Exception("Layout preview timed out.");
                    if (count.Text != "2 of 2 images selected" || !count.Visible) throw new Exception("Missing selection count.");
                    var visible = controls.Where(c => c.Visible && (c == count || c is Button or ComboBox or NumericUpDown or PictureBox));
                    foreach (var c in visible)
                    {
                        if (c is ComboBox box && box.ItemHeight < box.Font.Height)
                            throw new Exception($"Dropdown text exceeds its row at {dpi} DPI ({box.ItemHeight} px for {box.Font.Height} px text).");
                        var bounds = c.RectangleToScreen(c.ClientRectangle);
                        for (Control? parent = c.Parent; parent != null; parent = parent.Parent)
                            if (bounds.IsEmpty || !parent.RectangleToScreen(parent.ClientRectangle).Contains(bounds))
                                throw new Exception($"{width}×{height}, {dpi} DPI, action {mode}: {c.GetType().Name} '{c.Text}' {bounds} clipped by {parent.GetType().Name} {parent.RectangleToScreen(parent.ClientRectangle)}.");
                    }
                }
                Console.WriteLine($"Passed all {Program.Actions.Length} actions at {width}×{height}, {dpi * 100 / 96}%.");
            }
            action.SelectedIndex = Program.ViewMetadata;
            var images = controls.OfType<ListView>().Single();
            images.Items[1].Selected = false;
            Application.DoEvents();
            if (count.Text != "1 of 2 images selected") throw new Exception("Selection count did not update.");
            form.Close();
            CheckLargeFolder(dir, first);
        }
        finally { dir.Delete(true); }
    }

    private static void CheckLargeFolder(DirectoryInfo root, string sample)
    {
        const int batchSize = 128;
        var folder = root.CreateSubdirectory("Selected folder");
        var nested = folder.CreateSubdirectory("Nested");
        for (int i = 0; i < batchSize; i++) File.Copy(sample, Path.Combine(nested.FullName, $"image-{i:D3}.png"));
        using var form = new Workspace([folder.FullName], 2, resolveSelection: true);
        form.Show();
        var controls = Children(form).ToArray();
        var count = controls.OfType<Label>().Single(label => label.AccessibleName == "Image selection count");
        var list = controls.OfType<ListView>().Single();
        var export = controls.OfType<Button>().Single(button => button.Text == "Export copies");

        void WaitFor(Func<bool> ready)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!ready() && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(5); }
            if (!ready()) throw new Exception("Large folder workflow timed out.");
        }

        WaitFor(() => export.Enabled && list.Items.Count == batchSize);
        if (count.Text != "128 of 128 images selected" || list.Items[1].SubItems[2].Text != "Select to preview")
            throw new Exception("Large folder count or deferred encoding missing.");
        list.SelectedItems.Clear();
        list.Items[1].Selected = true;
        WaitFor(() => count.Text == "1 of 128 images selected" && list.Items[1].SubItems[2].Text.Contains("bytes"));
        export.PerformClick();
        WaitFor(() => count.Text == "0 of 128 images selected");
        var outputs = Directory.EnumerateFiles(nested.FullName, "*-converted.png", SearchOption.AllDirectories).ToArray();
        if (outputs.Length != 1 || Path.GetFileName(outputs[0]) != "image-001-converted.png")
            throw new Exception("Large folder export must honor the highlighted selection.");
        form.Close();
        Console.WriteLine("Verified recursive selection, live count, deferred preview and single-image export in a 128-image folder.");
    }
}
