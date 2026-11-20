using System.ComponentModel;

namespace PufferHelper;

/// <summary>
/// Two-column numbered answer list. The first answer not yet used is highlighted; clicking an answer
/// crosses it off (click again to undo).
/// </summary>
internal sealed class AnswerListControl : Control
{
    private sealed class Item(string text, bool used)
    {
        public string Text { get; } = text;
        public bool Used { get; set; } = used;
        public Rectangle Bounds { get; set; }
    }

    private const int Columns = 2;
    private const int RowPadding = 6;

    private readonly List<Item> _items = [];
    private Font _boldFont;
    private Font _strikeFont;

    /// <summary>Raised when the user clicks an answer: (answer, nowUsed).</summary>
    public event Action<string, bool>? UsedToggled;

    /// <summary>Shown while the list is empty.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Placeholder
    {
        get => _placeholder;
        set
        {
            _placeholder = value;
            Invalidate();
        }
    }

    private string _placeholder = "";
    private static readonly Color HighlightColor = Color.FromArgb(90, 255, 190, 40);
    private static readonly Color UsedColor = Color.FromArgb(115, 115, 125);

    public AnswerListControl()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        _boldFont = new Font(Font, FontStyle.Bold);
        _strikeFont = new Font(Font, FontStyle.Strikeout);
    }

    public bool HasAnswers => _items.Count > 0;

    public IEnumerable<string> UnusedAnswers => _items.Where(i => !i.Used).Select(i => i.Text);

    private int RowHeight => Font.Height + RowPadding;

    private int RowCount => (_items.Count + Columns - 1) / Columns;

    public int PreferredListHeight => _items.Count == 0 ? Font.Height * 3 : RowCount * RowHeight + 2;

    public void SetAnswers(IEnumerable<string> answers, Func<string, bool> isUsed)
    {
        _items.Clear();
        foreach (string answer in answers)
            _items.Add(new Item(answer, isUsed(answer)));
        Invalidate();
    }

    /// <summary>Crosses off every listed answer matching one of <paramref name="answers"/>; returns how many changed.</summary>
    public int MarkUsed(IEnumerable<string> answers)
    {
        var list = answers.ToList();
        int changed = 0;
        foreach (Item item in _items.Where(i => !i.Used && list.Any(a => AnswerMatcher.SameAnswer(a, i.Text))))
        {
            item.Used = true;
            changed++;
        }
        if (changed > 0)
            Invalidate();
        return changed;
    }

    public void ClearAnswers()
    {
        _items.Clear();
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _boldFont.Dispose();
        _strikeFont.Dispose();
        _boldFont = new Font(Font, FontStyle.Bold);
        _strikeFont = new Font(Font, FontStyle.Strikeout);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);

        if (_items.Count == 0)
        {
            TextRenderer.DrawText(g, Placeholder, Font, ClientRectangle, UsedColor,
                TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        int columnWidth = Width / Columns;
        int rows = RowCount;
        Item? next = _items.FirstOrDefault(i => !i.Used);

        using var highlight = new SolidBrush(HighlightColor);
        for (int index = 0; index < _items.Count; index++)
        {
            Item item = _items[index];
            int column = index / rows, row = index % rows;
            var bounds = new Rectangle(column * columnWidth, row * RowHeight, columnWidth - 4, RowHeight);
            item.Bounds = bounds;

            if (item == next)
                g.FillRectangle(highlight, bounds);

            Font font = item.Used ? _strikeFont : item == next ? _boldFont : Font;
            Color color = item.Used ? UsedColor : ForeColor;
            var textBounds = Rectangle.Inflate(bounds, -4, 0);
            TextRenderer.DrawText(g, $"{index + 1}. {item.Text}", font, textBounds, color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left)
            return;
        Item? item = _items.FirstOrDefault(i => i.Bounds.Contains(e.Location));
        if (item is null)
            return;
        item.Used = !item.Used;
        Invalidate();
        UsedToggled?.Invoke(item.Text, item.Used);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _boldFont.Dispose();
            _strikeFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
