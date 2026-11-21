namespace ImageChecker;

internal static class Appearance
{
    internal static void SetWindowsColorMode()
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) && !SystemInformation.HighContrast)
            Application.SetColorMode(SystemColorMode.Dark);
    }

    internal static void FitWindow(Form form, Size preferred, Size minimum)
    {
        void Fit(bool opening)
        {
            Rectangle area = Screen.FromControl(form).WorkingArea;
            Size WithinArea(Size size) => new(Math.Min(size.Width, area.Width), Math.Min(size.Height, area.Height));
            form.MinimumSize = Size.Empty;
            form.MinimumSize = WithinArea(form.LogicalToDeviceUnits(minimum));
            if (form.WindowState != FormWindowState.Normal) return;
            Size size = WithinArea(opening ? form.LogicalToDeviceUnits(preferred) : form.Size);
            var center = opening && form.Owner != null ? form.Owner.Bounds : area;
            int x = opening ? center.Left + (center.Width - size.Width) / 2 : form.Left;
            int y = opening ? center.Top + (center.Height - size.Height) / 2 : form.Top;
            form.Bounds = new Rectangle(Math.Clamp(x, area.Left, area.Right - size.Width),
                Math.Clamp(y, area.Top, area.Bottom - size.Height), size.Width, size.Height);
        }
        form.Load += (_, _) => Fit(true);
        form.DpiChanged += (_, _) => form.BeginInvoke(() => { if (!form.IsDisposed) Fit(false); });
    }
    internal static Icon AppIcon { get; } = new(typeof(Appearance), "ImageChecker.ico");
    internal static Color Window => Tone(0x171A1F, SystemColors.Control);
    internal static Color Panel => Tone(0x24292F, SystemColors.Window);
    internal static Color Canvas => Tone(0x101317, SystemColors.Window);
    internal static Color Text => Tone(0xEEF2F5, SystemColors.WindowText);
    internal static Color Secondary => Tone(0xB8C3CC, SystemColors.ControlText);
    internal static Color Warning => Tone(0xFFC394, SystemColors.WindowText);
    private static readonly Color Selection = Color.FromArgb(44, 78, 83);
    private static readonly Color Accent = Color.FromArgb(113, 216, 202);
    private static Color Tone(int rgb, Color accessible) => SystemInformation.HighContrast
        ? accessible : Color.FromArgb(unchecked((int)0xFF000000) | rgb);

    internal static void StyleChoice(ComboBox box)
    {
        if (SystemInformation.HighContrast) return;
        box.FlatStyle = FlatStyle.Flat;
        box.BackColor = Panel;
        box.ForeColor = Text;
        box.DrawMode = DrawMode.OwnerDrawFixed;
        box.DrawItem += (_, e) =>
        {
            bool highlighted = e.State.HasFlag(DrawItemState.Selected) && !e.State.HasFlag(DrawItemState.ComboBoxEdit);
            Color background = highlighted ? Selection : Panel;
            using var brush = new SolidBrush(background);
            e.Graphics.FillRectangle(brush, e.Bounds);
            if (e.Index < 0) return;
            TextRenderer.DrawText(e.Graphics, box.GetItemText(box.Items[e.Index]), box.Font, e.Bounds,
                box.Enabled ? Text : Secondary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            if (e.State.HasFlag(DrawItemState.Focus)) ControlPaint.DrawFocusRectangle(e.Graphics, e.Bounds, Text, background);
        };
    }

    internal static void StyleButton(Button button, bool primary = false)
    {
        button.MinimumSize = new(88, 32);
        button.Padding = new(8, 2, 8, 2);
        if (SystemInformation.HighContrast) return;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(74, 84, 96);
        button.FlatAppearance.BorderSize = 1;
        void Colors()
        {
            button.BackColor = primary && button.Enabled ? Accent : Panel;
            button.ForeColor = primary && button.Enabled ? Color.FromArgb(13, 33, 34) : Text;
        }
        Colors();
        button.EnabledChanged += (_, _) => Colors();
    }

    internal static void DrawHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        using var brush = new SolidBrush(Window);
        e.Graphics.FillRectangle(brush, e.Bounds);
        var bounds = Rectangle.Inflate(e.Bounds, -6, 0);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text, e.Font, bounds, Secondary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    internal static void DrawCell(object? sender, DrawListViewSubItemEventArgs e)
    {
        if (e.Item == null || e.SubItem == null) return;
        var bounds = e.Bounds;
        if (e.ColumnIndex == 0 && e.Header != null) bounds.Width = e.Header.Width;
        var background = e.Item.Selected ? Selection : Panel;
        using var brush = new SolidBrush(background);
        e.Graphics.FillRectangle(brush, bounds);
        if (e.ColumnIndex == 0 && e.Item.Selected)
        {
            using var marker = new SolidBrush(Accent);
            e.Graphics.FillRectangle(marker, bounds.X, bounds.Y, 3, bounds.Height);
        }
        bounds.Inflate(-6, 0);
        Color ink = e.Item.ForeColor == Warning ? Warning : e.ColumnIndex == 2 ? Secondary : Text;
        TextRenderer.DrawText(e.Graphics, e.SubItem.Text, e.Item.Font, bounds, ink,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (e.ColumnIndex == 0 && e.Item.Focused && sender is ListView { Focused: true })
            ControlPaint.DrawFocusRectangle(e.Graphics, bounds, ink, background);
    }
}
