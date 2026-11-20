namespace PufferHelper;

/// <summary>
/// Full-screen "drag a box" picker over a frozen screenshot. Left-drag selects, Esc cancels,
/// right-click means "use the whole screen" (<see cref="UseWholeScreen"/>).
/// </summary>
internal sealed class RegionPickerForm : Form
{
    private const string Hint =
        "Drag a box around the part of the game that shows the category and the answers played so far.   " +
        "Esc = cancel  ·  right-click = use the whole screen";

    private readonly Bitmap _screenshot;
    private readonly Rectangle _virtualScreen;
    private Point _dragStart;
    private Rectangle _selection;
    private bool _dragging;

    /// <summary>The chosen area in screen coordinates.</summary>
    public Rectangle? SelectedArea { get; private set; }

    public bool UseWholeScreen { get; private set; }

    public RegionPickerForm()
    {
        _virtualScreen = SystemInformation.VirtualScreen;
        _screenshot = ScreenGrabber.Capture(_virtualScreen);

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = _virtualScreen;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        KeyPreview = true;
        Font = new Font("Segoe UI", 11f);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right)
        {
            UseWholeScreen = true;
            DialogResult = DialogResult.OK;
            Close();
            return;
        }
        if (e.Button == MouseButtons.Left)
        {
            _dragStart = e.Location;
            _selection = Rectangle.Empty;
            _dragging = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging)
            return;
        _selection = Rectangle.FromLTRB(
            Math.Min(_dragStart.X, e.X), Math.Min(_dragStart.Y, e.Y),
            Math.Max(_dragStart.X, e.X), Math.Max(_dragStart.Y, e.Y));
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging || e.Button != MouseButtons.Left)
            return;
        _dragging = false;

        if (_selection.Width < 40 || _selection.Height < 20)
        {
            _selection = Rectangle.Empty; // a click, not a drag
            Invalidate();
            return;
        }

        SelectedArea = new Rectangle(_selection.X + _virtualScreen.X, _selection.Y + _virtualScreen.Y, _selection.Width, _selection.Height);
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        // Explicit destination rectangle: DrawImage(img, x, y) would rescale by the bitmap's DPI.
        g.DrawImage(_screenshot, new Rectangle(0, 0, _virtualScreen.Width, _virtualScreen.Height));

        using (var dim = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
        using (var outside = new Region(ClientRectangle))
        {
            if (!_selection.IsEmpty)
                outside.Exclude(_selection);
            g.FillRegion(dim, outside);
        }

        if (!_selection.IsEmpty)
        {
            using var pen = new Pen(Color.Gold, 2f);
            g.DrawRectangle(pen, _selection);
        }

        // Hint banner at the top of the screen the cursor is on.
        Rectangle screen = Screen.FromPoint(Cursor.Position).Bounds;
        screen.Offset(-_virtualScreen.X, -_virtualScreen.Y);
        Size textSize = TextRenderer.MeasureText(Hint, Font);
        var banner = new Rectangle(screen.X + (screen.Width - textSize.Width) / 2 - 16, screen.Y + 24, textSize.Width + 32, textSize.Height + 16);
        using (var back = new SolidBrush(Color.FromArgb(230, 30, 30, 36)))
            g.FillRectangle(back, banner);
        TextRenderer.DrawText(g, Hint, Font, banner, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _screenshot.Dispose();
        base.Dispose(disposing);
    }
}
