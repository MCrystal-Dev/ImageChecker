using ImageChecker;
using ImageMagick;

internal static class ReviewChecks
{
    // UI regression: scanning, checking a copy, and cancelling must all preserve both files.
    internal static void Run()
    {
        var dir = Directory.CreateTempSubdirectory("ImageChecker-review-");
        try
        {
            string first = Path.Combine(dir.FullName, "original.png"), second = Path.Combine(dir.FullName, "copy.png");
            using (var image = new MagickImage(MagickColors.Green, 20, 20)) image.Write(first);
            File.Copy(first, second);
            using var form = new Workspace([first], 5);
            form.Show();
            var list = Descendants(form).OfType<ListView>().Single();
            var review = Descendants(form).OfType<Button>().Single(b => b.Text.StartsWith("Review removal"));
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while ((list.Items.Count != 2 || !list.Enabled) && DateTime.UtcNow < deadline)
            {
                Application.DoEvents(); Thread.Sleep(20);
            }
            if (list.Items.Count != 2 || review.Enabled) throw new Exception("Duplicate review initial state failed");
            list.Items[0].Checked = true;
            Application.DoEvents();
            if (!review.Enabled) throw new Exception("Review button did not enable.");
            using var timer = new System.Windows.Forms.Timer { Interval = 100 };
            bool sawPath = false;
            timer.Tick += (_, _) =>
            {
                var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f != form);
                if (dialog == null) return;
                timer.Stop();
                var paths = Descendants(dialog).OfType<TextBox>().Single().Lines;
                sawPath = paths.Length == 1 && (paths[0] == first || paths[0] == second);
                dialog.DialogResult = DialogResult.Cancel;
            };
            timer.Start();
            review.PerformClick();
            if (!sawPath || !File.Exists(first) || !File.Exists(second)) throw new Exception("Cancellation did not preserve both files");
            form.Close();
            Console.WriteLine("Duplicate review listed the exact selected path. Cancelling preserved both files.");
            CheckFormatSwitch(dir);
        }
        finally { dir.Delete(true); }
    }
    private static void CheckFormatSwitch(DirectoryInfo dir)
    {
        string[] files = Enumerable.Range(0, 8).Select(i => Path.Combine(dir.FullName, $"batch-{i}.png")).ToArray();
        using (var image = new MagickImage(MagickColors.Coral, 32, 16))
            foreach (string file in files) image.Write(file);
        using var form = new Workspace(files, 2);
        form.Show();
        var controls = Descendants(form).ToArray();
        var list = controls.OfType<ListView>().Single();
        var format = controls.OfType<ComboBox>().Single(box => box.Items.Contains("JPG"));
        var save = controls.OfType<Button>().Single(button => button.Text == "Save copies");
        void WaitFor(Func<bool> ready)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!ready() && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
            if (!ready()) throw new Exception("Format preview or save timed out.");
        }
        WaitFor(() => save.Enabled);
        list.Items[0].Selected = false;
        list.Items[5].Selected = true;
        format.SelectedItem = "JPG";
        if (list.Items.Count != files.Length || save.Enabled) throw new Exception("Missing sources or stale save button during format change.");
        WaitFor(() => save.Enabled);
        var changes = list.Items.Cast<ListViewItem>().Select(row => (Change)row.Tag!).ToArray();
        if (!changes.Select(c => c.Source).SequenceEqual(files) || changes.Any(c => !c.Target.EndsWith(".jpg") || File.Exists(c.Target))
            || list.SelectedItems.Count != 1 || list.SelectedItems[0].Index != 5)
            throw new Exception("Format change lost the selection or produced incorrect output names.");
        save.PerformClick();
        WaitFor(() => list.Enabled && list.Items.Count == 0);
        foreach (var change in changes)
        {
            using var output = new MagickImage(change.Target);
            if (output.Format != MagickFormat.Jpeg || !File.Exists(change.Source)) throw new Exception("JPG save failed.");
        }
        form.Close();
        Console.WriteLine("Changing to JPG kept all eight rows and the selected image, refreshed automatically, and saved eight JPEGs.");
    }
    private static IEnumerable<Control> Descendants(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(c => Descendants(c).Prepend(c));
}
