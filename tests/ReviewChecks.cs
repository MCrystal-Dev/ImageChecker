using ImageChecker;
using ImageMagick;
using System.Runtime.InteropServices;

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
            var refresh = Descendants(form).OfType<Button>().Single(b => b.Text == "Refresh");
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while ((list.Items.Count != 2 || !refresh.Enabled) && DateTime.UtcNow < deadline)
            {
                Application.DoEvents(); Thread.Sleep(20);
            }
            if (list.Items.Count != 2 || review.Enabled) throw new Exception("Duplicate review initial state failed");
            Descendants(form).OfType<Button>().Single(b => b.Text == "Select all").PerformClick();
            Application.DoEvents();
            if (list.SelectedItems.Count != 2 || list.CheckedItems.Count != 0 || review.Enabled)
                throw new Exception("Select all must highlight duplicates without marking them for recycling.");
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
        for (int i = 0; i < files.Length - 1; i++)
        {
            using var image = new MagickImage(MagickColors.Coral, (uint)(32 + i), 16);
            image.Write(files[i]);
        }
        File.Copy(files[0], files[^1]);
        using var form = new Workspace(files, 2);
        form.Show();
        var controls = Descendants(form).ToArray();
        var list = controls.OfType<ListView>().Single();
        var format = controls.OfType<ComboBox>().Single(box => box.Items.Contains("JPG"));
        var save = controls.OfType<Button>().Single(button => button.Text == "Export copies");
        var overwrite = controls.OfType<Button>().Single(button => button.Text == "Overwrite");
        var selectAll = controls.OfType<Button>().Single(button => button.Text == "Select all");
        var refresh = controls.OfType<Button>().Single(button => button.Text == "Refresh");
        if (selectAll.Parent != refresh.Parent || selectAll.Right > refresh.Left || selectAll.Parent != save.Parent)
            throw new Exception("Select all must sit to the left of Refresh in the footer.");
        void WaitFor(Func<bool> ready)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!ready() && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
            if (!ready()) throw new Exception("Format preview or save timed out.");
        }
        WaitFor(() => save.Enabled);
        if (!list.MultiSelect || list.SelectedItems.Count != files.Length) throw new Exception("Explorer batch was not selected.");
        list.SelectedItems.Clear();
        controls.OfType<Button>().Single(b => b.Text == "Select all").PerformClick();
        WaitFor(() => save.Enabled && list.SelectedItems.Count == files.Length);
        list.SelectedItems.Clear();
        list.Items[5].Selected = true;
        var action = controls.OfType<ComboBox>().Single(box => box.Items.Count == 6);
        void CheckRows()
        {
            WaitFor(() => refresh.Enabled && list.Items.Count == files.Length);
            if (list.SelectedItems.Count != 1 || !Descendants(form).OfType<Label>().Any(label => label.Text == Path.GetFileName(files[5])))
                throw new Exception("Changing an action lost the highlighted source.");
        }
        foreach (int mode in new[] { 0, 1, 3, 4, 5, 2 })
        {
            action.SelectedIndex = mode;
            CheckRows();
            if (overwrite.Visible != (mode is 2 or 3 or 4)) throw new Exception("Overwrite visible for wrong action.");
            if (mode == 5)
            {
                if (save.Enabled || list.Items.Cast<ListViewItem>().Count(i => i.Tag is (string, string)) != 2)
                    throw new Exception("Incorrect duplicate groups or removal enabled without a selection.");
                list.SelectedItems[0].Checked = true;
                Application.DoEvents();
                if (save.Enabled || list.CheckedItems.Count != 0) throw new Exception("An unmatched file could be checked.");
                continue;
            }
            if (!save.Enabled) throw new Exception($"Action {mode} did not refresh automatically.");
            if (mode == 0 && !((Change)list.Items[0].Tag!).Target.Contains(Operations.Today))
                throw new Exception("Date rename preview missing.");
            if (mode == 1)
            {
                if (Descendants(form).OfType<CheckBox>().Any()) throw new Exception("The date should always be included.");
                foreach (ListViewItem item in list.Items)
                {
                    var change = (Change)item.Tag!;
                    string expected = $"{Operations.Today.Replace('-', '_')}_{change.Number:D3}{Path.GetExtension(change.Source)}";
                    if (Path.GetFileName(change.Target) != expected) throw new Exception("Incorrect numbered rename preview.");
                }
            }
            if (mode is 3 or 4)
            {
                foreach (var field in Descendants(form).OfType<NumericUpDown>().Where(field => field.Visible).ToArray())
                {
                    field.Controls.OfType<TextBox>().Single().Text = mode == 3 ? "10" : "1";
                    if (save.Enabled) throw new Exception("Stale numeric preview could be saved.");
                    WaitFor(() => save.Enabled);
                }
                var row = (Change)list.Items[0].Tag!;
                using var staged = new MagickImage(row.Staged!);
                if (mode == 3 && staged.Width != 10 || mode == 4 && new FileInfo(row.Staged!).Length > 1000)
                    throw new Exception("Numeric settings did not refresh the staged result.");
            }
        }
        var previousRow = list.Items[5];
        format.SelectedItem = "WebP";
        if (!format.Enabled || !action.Enabled || !list.Enabled || list.Items[5] != previousRow || !previousRow.Selected)
            throw new Exception("Refreshing disabled a selector or cleared the highlighted row.");
        if (!SystemInformation.HighContrast)
        {
            foreach (var box in new[] { action, format })
            {
                using var frame = new Bitmap(box.Width, box.Height);
                box.DrawToBitmap(frame, box.ClientRectangle);
                var fill = frame.GetPixel(box.Width - SystemInformation.VerticalScrollBarWidth - 8, box.Height / 2);
                if (fill.GetBrightness() > 0.5f) throw new Exception("Selector painted a light background during refresh.");
            }
        }
        action.SelectedIndex = 3;
        action.SelectedIndex = 2;
        format.SelectedItem = "JPG";
        if (list.Items.Count != files.Length || save.Enabled) throw new Exception("Missing sources or stale save button during format change.");
        WaitFor(() => save.Enabled);
        var changes = list.Items.Cast<ListViewItem>().Select(row => (Change)row.Tag!).ToArray();
        if (!changes.Select(c => c.Source).SequenceEqual(files) || changes.Any(c => !c.Target.EndsWith(".jpg") || File.Exists(c.Target))
            || list.SelectedItems.Count != 1 || list.SelectedItems[0].Index != 5)
            throw new Exception("Format change lost the selection or produced incorrect output names.");
        ClickRow(list, 4, Keys.None);
        ClickRow(list, 6, Keys.ShiftKey);
        ClickRow(list, 1, Keys.ControlKey);
        int[] selected = [1, 4, 5, 6];
        if (!list.SelectedIndices.Cast<int>().SequenceEqual(selected)) throw new Exception("Ctrl/Shift selection did not match Explorer.");
        WaitFor(() => save.Enabled);
        void ConfirmOverwrite(bool accept, int expected)
        {
            bool valid = false;
            using var timer = new System.Windows.Forms.Timer { Interval = 50 };
            timer.Tick += (_, _) =>
            {
                var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Text == "Overwrite originals?");
                if (dialog == null) return;
                timer.Stop();
                var parts = Descendants(dialog).ToArray();
                valid = parts.OfType<Label>().Any(label => label.Text.Contains("destructive") && label.Text.Contains("permanently removed"))
                    && parts.OfType<TextBox>().Single().Lines.Length == expected
                    && dialog.ActiveControl is Button { Text: "Cancel" };
                parts.OfType<Button>().Single(b => b.Text == (accept ? "OK" : "Cancel")).PerformClick();
            };
            timer.Start();
            overwrite.PerformClick();
            if (!valid) throw new Exception("Missing destructive warning, paths, or default Cancel button.");
        }
        var originals = files.Select(File.ReadAllBytes).ToArray();
        ConfirmOverwrite(false, selected.Length);
        if (files.Where((path, i) => !File.ReadAllBytes(path).SequenceEqual(originals[i]) || File.Exists(Path.ChangeExtension(path, ".jpg"))).Any())
            throw new Exception("Cancel modified a source or produced a conversion.");
        save.PerformClick();
        WaitFor(() => refresh.Enabled && list.SelectedItems.Count == 0);
        for (int i = 0; i < files.Length; i++)
        {
            var change = changes[i];
            if (!File.Exists(change.Source) || File.Exists(change.Target) != selected.Contains(i))
                throw new Exception("Export affected files outside the selection.");
            if (!selected.Contains(i)) continue;
            using var output = new MagickImage(change.Target);
            if (output.Format != MagickFormat.Jpeg) throw new Exception("JPG save failed.");
        }
        if (save.Enabled) throw new Exception("An empty selection enabled Save.");
        list.Items[1].Selected = true;
        WaitFor(() => overwrite.Enabled);
        ConfirmOverwrite(true, 1);
        WaitFor(() => refresh.Enabled && list.SelectedItems.Count == 0);
        if (File.Exists(files[1]) || files.Where((path, i) => i != 1 && !File.ReadAllBytes(path).SequenceEqual(originals[i])).Any())
            throw new Exception("Confirmed overwrite did not respect the selection.");
        files[1] = Path.ChangeExtension(files[1], ".jpg");
        using (var updated = new MagickImage(files[1]))
            if (updated.Format != MagickFormat.Jpeg) throw new Exception("Incorrect replacement format.");
        foreach (int i in selected) list.Items[i].Selected = true;
        action.SelectedIndex = 1;
        WaitFor(() => save.Enabled);
        var numbered = list.SelectedItems.Cast<ListViewItem>().Select(item => (Change)item.Tag!).ToArray();
        if (!numbered.Select(c => c.Source).SequenceEqual(selected.Select(i => files[i])) ||
            !numbered.Select(c => c.Number).SequenceEqual(Enumerable.Range(numbered[0].Number, selected.Length)))
            throw new Exception("Numbered preview lost or skipped a selected row.");
        action.SelectedIndex = 0;
        WaitFor(() => save.Enabled);
        var renames = list.SelectedItems.Cast<ListViewItem>().Select(item => (Change)item.Tag!).ToArray();
        save.PerformClick();
        WaitFor(() => refresh.Enabled && list.SelectedItems.Count == 0);
        if (renames.Any(c => !File.Exists(c.Target) || File.Exists(c.Source)) ||
            files.Where((_, i) => !selected.Contains(i)).Any(file => !File.Exists(file)))
            throw new Exception("Rename did not respect highlighted rows.");
        form.Close();
        Console.WriteLine("Native Ctrl/Shift selection, selected-only export and rename, and consecutive numbering passed.");
    }
    private static void ClickRow(ListView list, int index, Keys modifier)
    {
        list.Focus();
        list.EnsureVisible(index);
        var bounds = list.Items[index].GetBounds(ItemBoundsPortion.Label);
        nint point = ((bounds.Top + bounds.Height / 2) << 16) | (bounds.Left + 4);
        var original = new byte[256];
        if (!GetKeyboardState(original)) throw new Exception("Could not read the test thread's key state.");
        var keys = (byte[])original.Clone();
        if (modifier != Keys.None) keys[(int)modifier] = 0x80;
        int flags = modifier switch { Keys.ControlKey => 8, Keys.ShiftKey => 4, _ => 0 };
        try
        {
            SetKeyboardState(keys);
            PostMessage(list.Handle, 0x201, flags | 1, point);
            PostMessage(list.Handle, 0x202, flags, point);
            Application.DoEvents();
        }
        finally { SetKeyboardState(original); }
        Application.DoEvents();
    }
    [DllImport("user32.dll")] private static extern bool GetKeyboardState([Out] byte[] keys);
    [DllImport("user32.dll")] private static extern bool SetKeyboardState(byte[] keys);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, uint message, nint keys, nint point);
    private static IEnumerable<Control> Descendants(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(c => Descendants(c).Prepend(c));
}
