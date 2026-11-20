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
        }
        finally { dir.Delete(true); }
    }
    private static IEnumerable<Control> Descendants(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(c => Descendants(c).Prepend(c));
}
