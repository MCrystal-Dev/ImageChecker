using ImageMagick;

namespace ImageChecker;

internal sealed class Workspace : Form
{
    private sealed class ImageListView : ListView
    {
        internal ImageListView() => DoubleBuffered = true;
    }
    private string[] files;
    private readonly ComboBox action = Choice(Program.Actions);
    private readonly ComboBox format = Choice(["PNG", "JPG", "WebP"]);
    private static readonly string[] LimitFormats = ["JPG", "WebP", "PNG"];
    private readonly ComboBox limitFormat = Choice(["Do not convert file format", .. LimitFormats.Select(f => $"Convert to {f}")]);
    private readonly NumericUpDown width = Number(1920, 100000), height = Number(1080, 100000), limit = Number(500, 1000000);
    private readonly FlowLayoutPanel options = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
    private readonly Control[][] optionFields;
    private readonly ListView rows = new ImageListView { View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill, MultiSelect = true };
    private readonly PictureBox before = Picture(), after = Picture();
    private readonly TextBox metadata = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        BackColor = Appearance.Canvas, ForeColor = Appearance.Text, BorderStyle = BorderStyle.None, AccessibleName = "Image metadata" };
    private string? metadataPath;
    private int metadataRequest;
    private readonly ToolTip fileTips = new();
    private readonly Label leftTitle = Caption("Original"), rightTitle = Caption("Preview");
    private readonly Label leftRole = Caption("Original"), rightRole = Caption("Preview");
    private readonly Label status = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Button preview = new() { Text = "Refresh", AutoSize = true };
    private readonly Button selectAll = new() { Text = "Select all", AutoSize = true, Anchor = AnchorStyles.Right, Enabled = false };
    private readonly System.Windows.Forms.Timer editDelay = new() { Interval = 350 };
    private readonly Button apply = new() { Text = "Apply", AutoSize = true, Enabled = false };
    private readonly Button overwrite = new() { Text = "Overwrite", AutoSize = true, Enabled = false };
    private readonly Button keepBoth = new() { Text = "Keep both", AutoSize = true };
    private readonly Button deleteLeft = new() { Text = "Delete Left", AutoSize = true };
    private readonly Button deleteRight = new() { Text = "Delete Right", AutoSize = true };
    private readonly Label reviewMessage = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Appearance.Secondary };
    private readonly RowStyle imageListHeight = new(SizeType.Percent, 48);
    private Plan? plan;
    private DuplicateReview? review;
    private bool busy;
    private bool previewPending;
    private bool selectionPending;
    private int Mode => action.SelectedIndex;

    internal Workspace(string[] paths, int mode)
    {
        files = paths.Select(Path.GetFullPath).Distinct(Operations.Names).Order(Operations.Names).ToArray();
        Text = $"ImageChecker — {files.Length} image{(files.Length == 1 ? "" : "s")}";
        Icon = Appearance.AppIcon;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Appearance.Window;
        ForeColor = Appearance.Text;
        SuspendLayout();
        AutoScaleDimensions = new(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Appearance.FitWindow(this, new(1060, 735), new(500, 300));
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new(16) };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (var style in new[] { new RowStyle(SizeType.AutoSize), imageListHeight, new RowStyle(SizeType.Percent, 52), new RowStyle(SizeType.AutoSize) }) layout.RowStyles.Add(style);
        Controls.Add(layout);
        action.Width = 285; action.SelectedIndex = mode;
        limitFormat.Width = 195;
        var toolbar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Padding = new(8), BackColor = Appearance.Panel, Margin = new(0, 0, 0, 10) };
        options.Dock = DockStyle.None;
        options.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        options.Margin = new(10, 0, 0, 0);
        optionFields = [
            [], [],
            [Caption("Output format"), format],
            [Caption("Max width (px)"), width, Caption("Max height (px)"), height],
            [Caption("Max size (KB)"), limit, Caption("1 MB = 1,000 KB"), Caption("Convert to"), limitFormat],
            []
        ];
        foreach (var fields in optionFields) options.Controls.AddRange(fields);
        toolbar.Controls.AddRange([Caption("Action"), action, options]);
        toolbar.ClientSizeChanged += (_, _) => options.MaximumSize = new(Math.Max(1,
            toolbar.ClientSize.Width - toolbar.Padding.Horizontal - options.Margin.Horizontal), 0);
        layout.Controls.Add(toolbar, 0, 0);
        rows.Columns.Add("Source image", 310); rows.Columns.Add("Output", 235); rows.Columns.Add("Details", 390);
        rows.ShowItemToolTips = true;
        rows.BackColor = Appearance.Panel;
        rows.ForeColor = Appearance.Text;
        rows.BorderStyle = BorderStyle.None;
        rows.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        rows.DrawColumnHeader += Appearance.DrawHeader;
        rows.DrawSubItem += Appearance.DrawCell;
        rows.OwnerDraw = !SystemInformation.HighContrast;
        rows.Margin = new(0);
        rows.ClientSizeChanged += (_, _) =>
        {
            int available = rows.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2;
            if (available <= 0) return;
            rows.Columns[0].Width = available * 36 / 100;
            rows.Columns[1].Width = available * 32 / 100;
            rows.Columns[2].Width = available - rows.Columns[0].Width - rows.Columns[1].Width;
        };
        layout.Controls.Add(rows, 0, 1);
        var compare = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new(0, 12, 0, 0) };
        compare.ColumnStyles.Add(new(SizeType.Percent, 50)); compare.ColumnStyles.Add(new(SizeType.Percent, 50));
        compare.RowStyles.Add(new(SizeType.Percent, 100));
        compare.Controls.Add(PreviewPanel(leftRole, leftTitle, before, new(0, 0, 5, 0)), 0, 0);
        var rightContent = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        rightContent.Controls.AddRange([after, metadata]);
        compare.Controls.Add(PreviewPanel(rightRole, rightTitle, rightContent, new(5, 0, 0, 0)), 1, 0);
        var viewer = new Panel { Dock = DockStyle.Fill, Margin = new(0, 12, 0, 0) };
        viewer.Controls.Add(compare);
        viewer.Controls.Add(reviewMessage);
        reviewMessage.BringToFront();
        layout.Controls.Add(viewer, 0, 2);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2, Margin = new(0, 6, 0, 0) };
        var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Right, WrapContents = false, Margin = Padding.Empty };
        status.ForeColor = Appearance.Secondary;
        Appearance.StyleButton(preview);
        Appearance.StyleButton(selectAll);
        Appearance.StyleButton(overwrite);
        Appearance.StyleButton(apply, primary: true);
        Appearance.StyleButton(keepBoth, primary: true);
        Appearance.StyleButton(deleteLeft);
        Appearance.StyleButton(deleteRight);
        footer.ColumnStyles.Add(new(SizeType.Percent, 100));
        footer.RowStyles.Add(new(SizeType.AutoSize));
        footer.RowStyles.Add(new(SizeType.AutoSize));
        foreach (Button button in new[] { selectAll, preview, overwrite, apply, deleteLeft, keepBoth, deleteRight })
        {
            buttons.Controls.Add(button);
        }
        footer.Controls.Add(status, 0, 0);
        footer.Controls.Add(buttons, 0, 1);
        layout.Controls.Add(footer, 0, 3);
        action.SelectedIndexChanged += async (_, _) => { Configure(); await Preview(); };
        format.SelectedIndexChanged += async (_, _) => await Preview();
        limitFormat.SelectedIndexChanged += async (_, _) => await Preview();
        editDelay.Tick += async (_, _) => await Preview();
        foreach (var field in new[] { width, height, limit })
        {
            field.ValueChanged += (_, _) => QueueNumericPreview();
            // NumericUpDown commits typed digits on validation. Schedule a preview while typing, too.
            field.Controls.OfType<TextBox>().Single().TextChanged += (_, _) => QueueNumericPreview();
        }
        rows.SelectedIndexChanged += (_, _) =>
        {
            if (busy || selectionPending) return;
            selectionPending = true;
            if (Mode != 5) apply.Enabled = overwrite.Enabled = false;
            BeginInvoke(() =>
            {
                selectionPending = false;
                if (!busy) UpdateSelection();
            });
        };
        selectAll.Click += (_, _) => SelectAllRows();
        rows.KeyDown += (_, e) =>
        {
            if (e.KeyData != (Keys.Control | Keys.A)) return;
            SelectAllRows();
            e.SuppressKeyPress = true;
        };
        keepBoth.Click += async (_, _) => await DecidePair(null);
        deleteLeft.Click += async (_, _) => await DecidePair(true);
        deleteRight.Click += async (_, _) => await DecidePair(false);
        before.Click += async (_, _) => await DecidePair(true);
        after.Click += async (_, _) => await DecidePair(false);
        preview.Click += async (_, _) => await Preview();
        apply.Click += async (_, _) => await Apply();
        overwrite.Click += async (_, _) => await Apply(replaceOriginals: true);
        Configure();
        ResumeLayout(true);
        Shown += async (_, _) => await Preview();
        FormClosing += (_, e) => e.Cancel = busy;
        FormClosed += (_, _) => { editDelay.Dispose(); fileTips.Dispose(); ClearPictures(); plan?.Dispose(); };
    }
    private void SelectAllRows()
    {
        rows.BeginUpdate();
        try { foreach (ListViewItem item in rows.Items) item.Selected = true; }
        finally { rows.EndUpdate(); }
        rows.Focus();
    }
    private static ComboBox Choice(string[] entries)
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        Appearance.StyleChoice(box);
        box.Items.AddRange(entries); box.SelectedIndex = 0; return box;
    }
    private static NumericUpDown Number(int value, int max) => new()
    {
        Minimum = 1, Maximum = max, Value = value, Width = 86,
        BackColor = Appearance.Panel, ForeColor = Appearance.Text
    };
    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Padding = new(0, 5, 4, 3) };
    private static PictureBox Picture() => new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Appearance.Canvas, Margin = new(0) };
    private static TableLayoutPanel PreviewPanel(Label role, Label name, Control picture, Padding margin)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Appearance.Panel, Padding = new(1), Margin = margin, ColumnCount = 2, RowCount = 2 };
        panel.ColumnStyles.Add(new(SizeType.AutoSize)); panel.ColumnStyles.Add(new(SizeType.Percent, 100));
        panel.RowStyles.Add(new(SizeType.AutoSize)); panel.RowStyles.Add(new(SizeType.Percent, 100));
        role.ForeColor = Appearance.Secondary; role.Margin = new(10, 3, 6, 5);
        name.AutoSize = false; name.AutoEllipsis = true; name.Dock = DockStyle.Fill;
        name.TextAlign = ContentAlignment.MiddleRight; name.Margin = new(0, 0, 10, 0); name.Padding = new(0);
        panel.Controls.Add(role, 0, 0); panel.Controls.Add(name, 1, 0);
        panel.Controls.Add(picture, 0, 1); panel.SetColumnSpan(picture, 2);
        return panel;
    }
    private void Configure()
    {
        options.SuspendLayout();
        for (int mode = 0; mode < optionFields.Length; mode++)
            foreach (var field in optionFields[mode]) field.Visible = Mode == mode;
        options.ResumeLayout(true);
        bool comparing = Mode == 5;
        fileTips.RemoveAll();
        imageListHeight.Height = comparing ? 0 : 48;
        bool inspecting = Mode == Program.ViewMetadata;
        rows.Visible = selectAll.Visible = !comparing;
        apply.Visible = !comparing && !inspecting;
        metadata.Visible = inspecting;
        after.Visible = !inspecting;
        rows.Columns[1].Text = inspecting ? "Folder" : "Output";
        rows.Columns[2].Text = inspecting ? "File type" : "Details";
        keepBoth.Visible = deleteLeft.Visible = deleteRight.Visible = comparing;
        before.Cursor = after.Cursor = comparing ? Cursors.Hand : Cursors.Default;
        reviewMessage.Visible = comparing;
        reviewMessage.Text = "Looking for matches…";
        leftRole.Text = comparing ? "Left" : "Original";
        rightRole.Text = inspecting ? "Metadata" : comparing ? "Right" : "Preview";
        overwrite.Visible = Mode is 2 or 3 or 4;
        apply.Text = Program.IsRename(Mode) ? "Rename" : "Export copies";
    }
    private void QueueNumericPreview()
    {
        if (!options.Enabled || Mode is not (3 or 4)) return;
        apply.Enabled = overwrite.Enabled = false;
        status.Text = "Updating preview…";
        editDelay.Stop();
        editDelay.Start();
    }
    private void ClearPictures()
    {
        Thumb(before, null); Thumb(after, null);
        leftTitle.Text = rightTitle.Text = "";
    }
    private void Reset()
    {
        selectAll.Enabled = false;
        rows.Items.Clear(); review = null;
        ClearPictures(); plan?.Dispose(); plan = null; apply.Enabled = overwrite.Enabled = false;
        status.Text = "Preview the selected action.";
    }
    private void ShowPair()
    {
        string? leftPath = null, rightPath = null, outputName = null;
        if (Mode == 5)
        {
            if (review?.Current is {} pair)
            {
                leftPath = pair.Left.Path;
                rightPath = pair.Right.Path;
                leftRole.Text = $"Left · {pair.Left.Details}";
                rightRole.Text = $"Right · {pair.Right.Details}";
                fileTips.SetToolTip(leftTitle, pair.Left.Path);
                fileTips.SetToolTip(rightTitle, pair.Right.Path);
            }
        }
        else
        {
            var active = ActiveRow();
            leftPath = SourceOf(active);
            if (active?.Tag is Change change)
            {
                rightPath = change.Staged ?? change.Source;
                outputName = change.Target;
            }
        }
        leftTitle.Text = Path.GetFileName(leftPath) ?? "";
        rightTitle.Text = Path.GetFileName(outputName ?? rightPath) ?? "";
        Thumb(before, leftPath);
        Thumb(after, rightPath);
        if (Mode == Program.ViewMetadata && !busy) ShowMetadata(leftPath);
    }
    private async void ShowMetadata(string? path)
    {
        if (path == metadataPath && path != null) return;
        metadataPath = path;
        int request = ++metadataRequest;
        metadata.Text = path == null ? "Select an image to view its metadata." : "Reading metadata…";
        if (path == null) return;
        string text;
        try { text = await Task.Run(() => ImageMetadata.Read(path).Text); }
        catch (Exception ex) { text = $"Could not read metadata. {ex.Message}"; }
        if (!IsDisposed && request == metadataRequest && Mode == Program.ViewMetadata) metadata.Text = text;
    }
    private ListViewItem? ActiveRow() => rows.FocusedItem is { Selected: true } focused
        ? focused : rows.SelectedItems.Cast<ListViewItem>().FirstOrDefault();

    private void UpdateSelection()
    {
        if (Mode != 5 && plan != null)
        {
            int count = 0, number = plan.CounterStart;
            foreach (ListViewItem item in rows.Items)
            {
                if (item.Tag is not Change change) continue;
                if (item.Selected) count++;
                if (Mode != 1) continue;
                if (item.Selected)
                {
                    change = Operations.NumberedRename(change.Source, change.Original, plan.Day, ref number);
                    item.Tag = change;
                }
                item.SubItems[1].Text = item.Selected ? Path.GetFileName(change.Target) : "";
                item.SubItems[2].Text = item.Selected ? change.Details : "";
                item.ToolTipText = item.Selected ? change.Source + Environment.NewLine + change.Target : change.Source;
            }
            status.Text = $"{count} of {files.Length} selected · Ctrl-click or Shift-click to select";
            if (plan.Notes.Count > 0) status.Text += $" · {plan.Notes.Count} skipped";
            apply.Enabled = count > 0 && !editDelay.Enabled;
            overwrite.Enabled = apply.Enabled && Mode is 2 or 3 or 4;
        }
        ShowPair();
    }
    private static void Thumb(PictureBox box, string? path)
    {
        Bitmap? next = null;
        try
        {
            if (path != null)
            {
                var key = (path, Stamp.Read(path), box.ClientSize);
                if (key.Equals(box.Tag)) return;
                using var image = new MagickImage(path, new MagickReadSettings { Format = Operations.FormatOf(path), FrameCount = 1 });
                image.AutoOrient(); image.Thumbnail((uint)Math.Max(1, box.ClientSize.Width), (uint)Math.Max(1, box.ClientSize.Height));
                using var stream = new MemoryStream(image.ToByteArray(MagickFormat.Png));
                using var decoded = new Bitmap(stream);
                next = new Bitmap(decoded);
                box.Tag = key;
            }
        }
        catch (Exception) { /* A damaged image can still be renamed or compared by its bytes. */ }
        var previous = box.Image;
        box.Image = next;
        if (next == null) box.Tag = null;
        previous?.Dispose();
    }
    private void Working(bool on, bool previewing = false)
    {
        busy = on;
        UseWaitCursor = on && !previewing;
        action.Enabled = options.Enabled = rows.Enabled = !on || previewing;
        preview.Enabled = !on;
        selectAll.Enabled = !on && rows.Items.Count > 0;
        overwrite.Enabled = false;
        apply.Enabled = false;
        keepBoth.Enabled = deleteLeft.Enabled = deleteRight.Enabled = !on && Mode == 5 && review?.Current != null;
        if (!on) UpdateSelection();
    }
    private async Task Preview(HashSet<string>? selection = null)
    {
        editDelay.Stop();
        if (busy) { previewPending = true; return; }
        Working(true, previewing: true);
        status.Text = "Preparing preview…";
        try
        {
            if (rows.Items.Count == 0 && Mode != 5)
            {
                rows.Items.AddRange(files.Select(source => new ListViewItem([Path.GetFileName(source), "", "Preparing preview…"]) { Tag = source, ToolTipText = source }).ToArray());
                RestoreSelection(selection ?? files.ToHashSet(Operations.Names), null);
                ShowPair();
            }
            do
            {
                previewPending = false;
                var notes = new List<string>();
                var items = new List<ListViewItem>();
                plan?.Dispose(); plan = null;
                review = null;
                if (Mode == 5)
                {
                    reviewMessage.Visible = true;
                    reviewMessage.Text = "Looking for matches…";
                    var scanned = await Task.Run(() => new DuplicateReview(files));
                    if (previewPending) continue;
                    review = scanned;
                    UpdateReviewStatus();
                    break;
                }
                if (Mode == Program.ViewMetadata)
                {
                    metadataPath = null;
                    foreach (string file in files)
                        items.Add(new ListViewItem([Path.GetFileName(file), Path.GetDirectoryName(file)!, Path.GetExtension(file)]) { Tag = file, ToolTipText = file });
                    status.Text = "Select an image to inspect its metadata.";
                }
                else
                {
                    string? convertTo = limitFormat.SelectedIndex > 0 ? LimitFormats[limitFormat.SelectedIndex - 1] : null;
                    var settings = new Options(Mode, format.Text, (int)width.Value, (int)height.Value, (long)limit.Value * 1000, convertTo);
                    plan = await Task.Run(() => Operations.Preview(files, settings));
                    if (previewPending) continue;
                    foreach (var c in plan.Changes)
                        items.Add(new ListViewItem([Path.GetFileName(c.Source), Path.GetFileName(c.Target), c.Details]) { Tag = c, ToolTipText = c.Source + Environment.NewLine + c.Target });
                    notes.AddRange(plan.Notes);
                }
                foreach (var note in notes) items.Add(new ListViewItem([note, "", "Skipped"]) { ForeColor = Appearance.Warning, ToolTipText = note });
                if (notes.Count > 0) status.Text += $" · {notes.Count} skipped";
                var highlighted = selection ?? (rows.Items.Count == 0 ? files.ToHashSet(Operations.Names)
                    : rows.SelectedItems.Cast<ListViewItem>().Select(SourceOf).OfType<string>().ToHashSet(Operations.Names));
                string? focusedSource = SourceOf(ActiveRow());
                rows.BeginUpdate();
                try
                {
                    rows.Items.Clear();
                    rows.Items.AddRange(items.ToArray());
                    RestoreSelection(highlighted, focusedSource);
                }
                finally { rows.EndUpdate(); }
            } while (previewPending);
        }
        catch (Exception ex) { Reset(); status.Text = "Preview failed."; MessageBox.Show(this, ex.Message, "Preview failed"); }
        finally { Working(false); }
    }
    private void RestoreSelection(HashSet<string> selection, string? focusedSource)
    {
        ListViewItem? focused = null;
        rows.BeginUpdate();
        foreach (ListViewItem item in rows.Items)
        {
            string? path = SourceOf(item);
            item.Selected = path != null && selection.Contains(path);
            if (item.Selected && (focused == null || Operations.Names.Equals(path, focusedSource))) focused = item;
        }
        rows.EndUpdate();
        if (focused != null) { focused.Focused = true; focused.EnsureVisible(); }
    }
    private static string? SourceOf(ListViewItem? item) => item?.Tag switch
    {
        Change change => change.Source,
        string path => path,
        _ => null
    };
    private async Task Apply(bool replaceOriginals = false)
    {
        if (busy) return;
        if (Mode == 5) return;
        if (plan == null || selectionPending || editDelay.Enabled) return;
        var selected = rows.SelectedItems.Cast<ListViewItem>().OrderBy(item => item.Index)
            .Select(item => item.Tag).OfType<Change>().ToArray();
        if (selected.Length == 0) return;
        if (replaceOriginals)
        {
            string warning = $"This is destructive. The original {(selected.Length == 1 ? "version of this image" : $"versions of these {selected.Length} images")} will be permanently removed. Click OK to replace with the previewed result.";
            var paths = selected.Select(row => Operations.Names.Equals(row.Source, Operations.OverwriteTarget(row))
                ? row.Source : $"{row.Source} → {Path.GetFileName(Operations.OverwriteTarget(row))}");
            if (!Details("Overwrite originals?", paths, "OK", warning)) return;
        }
        Working(true);
        try
        {
            int mode = Mode;
            var result = await Task.Run(() => Operations.Apply(plan, mode, selected, replaceOriginals));
            if (result.Errors.Count > 0) Details("Some files could not be processed", result.Errors);
            bool renaming = Program.IsRename(mode);
            if (result.Done == files.Length && renaming) { Working(false); Close(); return; }
            string summary = renaming ? $"Renamed {result.Done}." : replaceOriginals ? $"Replaced {result.Done} original{(result.Done == 1 ? "" : "s")}."
                : result.Done > 0 ? $"Exported {result.Done} to {Path.GetDirectoryName(result.Completed[0].Target)}" : "No copies exported.";
            if (renaming || replaceOriginals)
            {
                var renamed = result.Completed.ToDictionary(change => change.Source, change => change.Target, Operations.Names);
                for (int i = 0; i < files.Length; i++) files[i] = renamed.GetValueOrDefault(files[i], files[i]);
            }
            if (result.Done < files.Length)
            {
                busy = false;
                await Preview(new HashSet<string>(Operations.Names));
                status.Text = summary;
                return;
            }
            Reset(); status.Text = summary;
        }
        catch (Exception ex) { Reset(); status.Text = ex.Message; }
        finally { if (!IsDisposed && busy) Working(false); }
    }
    private void UpdateReviewStatus()
    {
        var pair = review?.Current;
        reviewMessage.Visible = pair == null;
        reviewMessage.Text = review?.Reviewed > 0 ? "Review complete" : "No matching pairs found";
        status.Text = pair != null ? $"{(pair.Exact ? "Exact copies" : "Visually similar")} · Click an image to choose it for deletion."
            : $"{review?.Reviewed ?? 0} pairs reviewed.";
        if (review?.Notes.Count > 0) status.Text += $" · {review.Notes.Count} scan warnings";
        fileTips.SetToolTip(status, review == null ? "" : string.Join(Environment.NewLine, review.Notes.Take(5)));
    }

    private async Task DecidePair(bool? deleteLeftSide)
    {
        if (busy || Mode != 5 || review?.Current is not {} pair) return;
        if (deleteLeftSide.HasValue)
        {
            var target = deleteLeftSide.Value ? pair.Left : pair.Right;
            string side = deleteLeftSide.Value ? "left" : "right";
            if (!Details($"Delete the {side} image?", [target.Path], "Move to Recycle Bin",
                $"Move the {side} image to the Recycle Bin? The other image will be kept.")) return;
        }
        Working(true);
        try
        {
            string? recycled = null;
            if (deleteLeftSide is bool left)
            {
                pair.RecycleSide(left, Handle);
                recycled = left ? pair.Left.Path : pair.Right.Path;
                files = files.Where(path => !Operations.Names.Equals(path, recycled)).ToArray();
                Text = $"ImageChecker — {files.Length} image{(files.Length == 1 ? "" : "s")}";
            }
            await Task.Run(() => review.Advance(recycled));
            UpdateReviewStatus();
        }
        catch (Exception ex) { Details("Could not complete review", [ex.Message]); }
        finally { Working(false); }
    }
    private bool Details(string title, IEnumerable<string> lines, string? confirm = null, string? warning = null)
    {
        using var dialog = new Form { Text = title, Icon = Appearance.AppIcon, AutoScaleDimensions = new(96, 96), AutoScaleMode = AutoScaleMode.Dpi, Padding = new(16), BackColor = Appearance.Window, ForeColor = Appearance.Text, StartPosition = FormStartPosition.CenterParent };
        Appearance.FitWindow(dialog, new(720, 340), new(360, 240));
        var list = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Lines = lines.ToArray(), Dock = DockStyle.Fill, BackColor = Appearance.Panel, ForeColor = Appearance.Text };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new(0, 8, 0, 0) };
        var cancel = new Button { Text = confirm != null ? "Cancel" : "Close", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(cancel); dialog.CancelButton = cancel; dialog.AcceptButton = cancel;
        if (confirm != null) buttons.Controls.Add(new Button { Text = confirm, AutoSize = true, DialogResult = DialogResult.OK });
        foreach (Button button in buttons.Controls) Appearance.StyleButton(button);
        dialog.Controls.Add(list); dialog.Controls.Add(buttons);
        if (warning != null)
        {
            var message = new Label { Text = warning, Dock = DockStyle.Top, AutoSize = true, Padding = new(0, 0, 0, 12), ForeColor = Appearance.Warning };
            dialog.Controls.Add(message);
            dialog.ClientSizeChanged += (_, _) => message.MaximumSize = new(Math.Max(1, dialog.DisplayRectangle.Width), 0);
            dialog.Shown += (_, _) => cancel.Focus();
        }
        return dialog.ShowDialog(this) == DialogResult.OK;
    }
}
