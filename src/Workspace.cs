using ImageMagick;

namespace ImageChecker;

internal sealed class Workspace : Form
{
    private readonly string[] files;
    private readonly ComboBox action = Choice(Program.Actions);
    private readonly ComboBox format = Choice(["PNG", "JPG", "WebP"]);
    private readonly CheckBox date = new() { Text = "Include today's date", AutoSize = true };
    private readonly NumericUpDown width = Number(1920, 100000), height = Number(1080, 100000), limit = Number(500, 1000000);
    private readonly FlowLayoutPanel options = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
    private readonly ListView rows = new() { View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill, MultiSelect = false };
    private readonly PictureBox before = Picture(), after = Picture();
    private readonly Label leftTitle = Caption("Original"), rightTitle = Caption("Preview");
    private readonly Label status = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Button preview = new() { Text = "Refresh", AutoSize = true };
    private readonly System.Windows.Forms.Timer editDelay = new() { Interval = 350 };
    private readonly Button apply = new() { Text = "Apply", AutoSize = true, Enabled = false };
    private Plan? plan;
    private Dictionary<string, List<string>> groups = [];
    private bool busy;
    private int Mode => action.SelectedIndex;

    internal Workspace(string[] paths, int mode)
    {
        files = paths.Select(Path.GetFullPath).Distinct(Operations.Names).Order(Operations.Names).ToArray();
        Text = $"ImageChecker — {files.Length} selected";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new(780, 570); Size = new(1020, 690);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new(16) };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (var style in new[] { new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Percent, 54), new RowStyle(SizeType.Percent, 46), new RowStyle(SizeType.AutoSize) }) layout.RowStyles.Add(style);
        Controls.Add(layout);
        action.Width = 250; action.SelectedIndex = mode;
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        actions.Controls.AddRange([Caption("Action"), action]);
        layout.Controls.Add(actions, 0, 0); layout.Controls.Add(options, 0, 1);
        rows.Columns.Add("Source image", 310); rows.Columns.Add("Output", 235); rows.Columns.Add("Details", 390);
        rows.ShowItemToolTips = true;
        layout.Controls.Add(rows, 0, 2);
        var compare = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        compare.ColumnStyles.Add(new(SizeType.Percent, 50)); compare.ColumnStyles.Add(new(SizeType.Percent, 50));
        compare.RowStyles.Add(new(SizeType.AutoSize)); compare.RowStyles.Add(new(SizeType.Percent, 100));
        compare.Controls.Add(leftTitle, 0, 0); compare.Controls.Add(rightTitle, 1, 0);
        compare.Controls.Add(before, 0, 1); compare.Controls.Add(after, 1, 1); layout.Controls.Add(compare, 0, 3);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3 };
        footer.ColumnStyles.Add(new(SizeType.Percent, 100)); footer.ColumnStyles.Add(new(SizeType.AutoSize)); footer.ColumnStyles.Add(new(SizeType.AutoSize));
        footer.Controls.Add(status, 0, 0); footer.Controls.Add(preview, 1, 0); footer.Controls.Add(apply, 2, 0);
        layout.Controls.Add(footer, 0, 4);
        action.SelectedIndexChanged += async (_, _) => { Configure(); await Preview(); };
        format.SelectedIndexChanged += async (_, _) => await Preview();
        date.CheckedChanged += async (_, _) => await Preview();
        editDelay.Tick += async (_, _) => await Preview();
        foreach (var field in new[] { width, height, limit })
        {
            field.ValueChanged += (_, _) => QueueNumericPreview();
            // NumericUpDown commits typed digits on validation. Schedule a preview while typing, too.
            field.Controls.OfType<TextBox>().Single().TextChanged += (_, _) => QueueNumericPreview();
        }
        rows.SelectedIndexChanged += (_, _) => ShowPair();
        rows.ItemCheck += (_, e) =>
        {
            if (Mode == 5 && rows.Items[e.Index].Tag is not ValueTuple<string, string>) e.NewValue = CheckState.Unchecked;
        };
        rows.ItemChecked += (_, _) =>
        {
            if (Mode == 5) BeginInvoke(() => { if (Mode == 5) apply.Enabled = !busy && rows.CheckedItems.Count > 0; });
        };
        preview.Click += async (_, _) => await Preview();
        apply.Click += async (_, _) => await Apply();
        Configure();
        Shown += async (_, _) => await Preview();
        FormClosing += (_, e) => e.Cancel = busy;
        FormClosed += (_, _) => { editDelay.Dispose(); ClearPictures(); plan?.Dispose(); };
    }
    private static ComboBox Choice(string[] entries)
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        box.Items.AddRange(entries); box.SelectedIndex = 0; return box;
    }
    private static NumericUpDown Number(int value, int max) => new() { Minimum = 1, Maximum = max, Value = value, Width = 86 };
    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Padding = new(0, 5, 4, 3) };
    private static PictureBox Picture() => new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = SystemColors.ControlLight };
    private void Configure()
    {
        options.Controls.Clear();
        switch (Mode)
        {
            case 1: options.Controls.Add(date); break;
            case 2: options.Controls.AddRange([Caption("Output format"), format]); break;
            case 3: options.Controls.AddRange([Caption("Max width (px)"), width, Caption("Max height (px)"), height]); break;
            case 4: options.Controls.AddRange([Caption("Max file size (KB, 1 KB = 1,000 bytes)"), limit]); break;
            case 5: options.Controls.Add(Caption("Check the copies to recycle. Keep at least one image in each group.")); break;
        }
        rows.CheckBoxes = Mode == 5;
        rows.Columns[1].Text = Mode == 5 ? "Folder" : "Output";
        apply.Text = Mode == 5 ? "Review removal…" : Mode <= 1 ? "Rename" : "Save copies";
    }
    private void QueueNumericPreview()
    {
        if (busy || Mode is not (3 or 4)) return;
        apply.Enabled = false;
        status.Text = "Updating preview…";
        editDelay.Stop();
        editDelay.Start();
    }
    private void ClearPictures()
    {
        foreach (var box in new[] { before, after }) { var old = box.Image; box.Image = null; old?.Dispose(); }
        leftTitle.Text = "Original";
        rightTitle.Text = "Preview";
    }
    private void Reset()
    {
        rows.Items.Clear(); rows.Groups.Clear(); groups.Clear();
        ClearPictures(); plan?.Dispose(); plan = null; apply.Enabled = false;
        status.Text = "Preview the selected action.";
    }
    private void ShowPair()
    {
        ClearPictures();
        if (rows.SelectedItems.Count == 0) return;
        if (rows.SelectedItems[0].Tag is string source)
        {
            leftTitle.Text = Path.GetFileName(source);
            Thumb(before, source);
        }
        else if (rows.SelectedItems[0].Tag is Change c)
        {
            leftTitle.Text = Path.GetFileName(c.Source); rightTitle.Text = Path.GetFileName(c.Target);
            Thumb(before, c.Source); Thumb(after, c.Staged ?? c.Source);
        }
        else if (rows.SelectedItems[0].Tag is ValueTuple<string, string> pair)
        {
            string other = groups[pair.Item1].First(p => p != pair.Item2);
            leftTitle.Text = Path.GetFileName(pair.Item2); rightTitle.Text = Path.GetFileName(other);
            Thumb(before, pair.Item2); Thumb(after, other);
        }
    }
    private static void Thumb(PictureBox box, string path)
    {
        try
        {
            using var image = new MagickImage(path, new MagickReadSettings { Format = Operations.FormatOf(path), FrameCount = 1 });
            image.AutoOrient(); image.Thumbnail(480, 260);
            using var stream = new MemoryStream(image.ToByteArray(MagickFormat.Png));
            using var decoded = new Bitmap(stream); box.Image = new Bitmap(decoded);
        }
        catch (Exception) { /* A damaged image can still be renamed or compared by its bytes. */ }
    }
    private void Working(bool on)
    {
        busy = on; UseWaitCursor = on; action.Enabled = options.Enabled = preview.Enabled = rows.Enabled = !on;
        apply.Enabled = !on && (Mode == 5 ? rows.CheckedItems.Count > 0 : plan?.Changes.Count > 0);
    }
    private async Task Preview()
    {
        editDelay.Stop();
        if (busy) return;
        string? selectedSource = SourceOf(rows.SelectedItems.Cast<ListViewItem>().FirstOrDefault());
        Reset(); Working(true); status.Text = "Preparing preview…";
        try
        {
            var notes = new List<string>();
            rows.BeginUpdate();
            foreach (string source in files)
                rows.Items.Add(new ListViewItem([source, "", "Preparing preview…"]) { Tag = source });
            rows.EndUpdate();
            int selectedIndex = Array.IndexOf(files, selectedSource);
            rows.Items[Math.Max(0, selectedIndex)].Selected = true;
            if (Mode == 5)
            {
                groups = await Task.Run(() => Operations.Duplicates(files, notes));
                rows.Items.Clear();
                int n = 0;
                foreach (var (hash, paths) in groups)
                {
                    var group = new ListViewGroup($"Match {++n} · {paths.Count} identical files"); rows.Groups.Add(group);
                    foreach (var path in paths)
                        rows.Items.Add(new ListViewItem([Path.GetFileName(path), Path.GetDirectoryName(path)!, "Exact match"], group) { Tag = (hash, path), ToolTipText = path });
                }
                var otherFiles = files.Except(groups.Values.SelectMany(paths => paths), Operations.Names).ToArray();
                if (otherFiles.Length > 0)
                {
                    var other = new ListViewGroup("Other selected images"); rows.Groups.Add(other);
                    foreach (var path in otherFiles)
                        rows.Items.Add(new ListViewItem([Path.GetFileName(path), Path.GetDirectoryName(path)!, "No exact match found"], other) { Tag = path, ToolTipText = path });
                }
                status.Text = $"{groups.Count} duplicate groups";
            }
            else
            {
                var settings = new Options(Mode, date.Checked, format.Text, (int)width.Value, (int)height.Value, (long)limit.Value * 1000);
                plan = await Task.Run(() => Operations.Preview(files, settings));
                rows.Items.Clear();
                foreach (var c in plan.Changes)
                    rows.Items.Add(new ListViewItem([c.Source, Path.GetFileName(c.Target), c.Details]) { Tag = c, ToolTipText = c.Target });
                notes.AddRange(plan.Notes);
                status.Text = $"{plan.Changes.Count} of {files.Length} ready";
            }
            foreach (var note in notes) rows.Items.Add(new ListViewItem([note, "", "Skipped"]) { ForeColor = Color.Maroon, ToolTipText = note });
            if (notes.Count > 0) status.Text += $" · {notes.Count} skipped";
            if (rows.Items.Count > 0)
            {
                var selected = rows.Items.Cast<ListViewItem>().FirstOrDefault(item => SourceOf(item) == selectedSource) ?? rows.Items[0];
                selected.Selected = true;
                selected.EnsureVisible();
            }
        }
        catch (Exception ex) { Reset(); status.Text = "Preview failed."; MessageBox.Show(this, ex.Message, "Preview failed"); }
        finally { Working(false); }
    }
    private static string? SourceOf(ListViewItem? item) => item?.Tag switch
    {
        Change change => change.Source,
        ValueTuple<string, string> match => match.Item2,
        string path => path,
        _ => null
    };
    private async Task Apply()
    {
        if (Mode == 5) { RemoveCopies(); return; }
        if (plan == null) return;
        Working(true);
        try
        {
            int mode = Mode;
            var result = await Task.Run(() => Operations.Apply(plan, mode));
            if (result.Errors.Count > 0) Details("Some files could not be processed", result.Errors);
            if (result.Done == files.Length && mode <= 1) { Working(false); Close(); return; }
            Reset(); status.Text = $"{result.Done} files {(mode <= 1 ? "renamed" : "saved")}.";
        }
        catch (Exception ex) { Reset(); status.Text = ex.Message; }
        finally { if (!IsDisposed) Working(false); }
    }
    private void RemoveCopies()
    {
        var selected = rows.CheckedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<(string Hash, string Path)>().ToArray();
        if (selected.Length == 0) return;
        var targets = selected.Select(s => s.Path).ToHashSet(Operations.Names);
        if (selected.Any(s => groups[s.Hash].All(targets.Contains)))
        {
            MessageBox.Show(this, "Uncheck at least one file in each group to keep it.", "Keep a copy"); return;
        }
        if (!Details("Move these files to the Recycle Bin?", targets, true)) return;
        var errors = new List<string>();
        int done = 0;
        foreach (var batch in selected.GroupBy(s => s.Hash))
        {
            try
            {
                string keeper = groups[batch.Key].First(p => !targets.Contains(p));
                using var lockedKeeper = File.OpenRead(keeper);
                if (Operations.Hash(lockedKeeper) != batch.Key) throw new IOException("The copy to keep has changed.");
                foreach (var (_, path) in batch)
                {
                    using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                    if (Operations.Hash(locked) != batch.Key) throw new IOException($"{path} has changed.");
                    Recycle.FileToBin(path, Handle); done++;
                }
            }
            catch (Exception ex) { errors.Add(ex.Message); }
        }
        Reset(); status.Text = $"{done} files moved to the Recycle Bin.";
        if (errors.Count > 0) Details("Some copies could not be recycled", errors);
    }
    private bool Details(string title, IEnumerable<string> lines, bool confirm = false)
    {
        using var dialog = new Form { Text = title, Size = new(720, 340), StartPosition = FormStartPosition.CenterParent };
        var list = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Lines = lines.ToArray(), Dock = DockStyle.Fill };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var cancel = new Button { Text = confirm ? "Cancel" : "Close", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(cancel); dialog.CancelButton = cancel; dialog.AcceptButton = cancel;
        if (confirm) buttons.Controls.Add(new Button { Text = "Move to Recycle Bin", AutoSize = true, DialogResult = DialogResult.OK });
        dialog.Controls.Add(list); dialog.Controls.Add(buttons);
        return dialog.ShowDialog(this) == DialogResult.OK;
    }
}
