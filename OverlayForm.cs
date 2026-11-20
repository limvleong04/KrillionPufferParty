using System.Diagnostics;
using Anthropic.Exceptions;
using static PufferHelper.NativeMethods;

namespace PufferHelper;

/// <summary>
/// The small always-on-top answer window. It never takes keyboard focus (so the game's text box keeps it while
/// you click answers off), stays out of the taskbar and Alt+Tab, and by default is left out of screen capture.
/// </summary>
internal sealed class OverlayForm : Form
{
    private enum HotkeyId { Ask = 1, Refresh = 2, PickRegion = 3, Toggle = 4 }

    private static readonly Color Background = Color.FromArgb(28, 28, 34);
    private static readonly Color Muted = Color.FromArgb(160, 160, 172);
    private const string Title = "Puffer Helper";

    private readonly AppSettings _settings;
    private readonly string? _loadError;
    private readonly bool _demo;
    private readonly AnswerService _answerService = new();
    private readonly Dictionary<HotkeyId, HotkeyBinding> _hotkeys = [];

    private readonly Label _categoryLabel;
    private readonly Label _statusLabel;
    private readonly AnswerListControl _list;
    private readonly Label _footerLabel;
    private readonly Label _costLabel;
    private readonly ToolTip _costTip = new() { InitialDelay = 300, AutoPopDelay = 15000 };

    private SpendLedger _ledger = SpendLedger.Load();
    private RequestUsage? _lastUsage;
    private int _sessionRequests;
    private decimal _sessionDollars;

    /// <summary>Answers used in the current category: seen on screen, or clicked off by you.</summary>
    private readonly List<string> _used = [];
    private string _category = "";

    private CancellationTokenSource? _askCts;
    private bool _refreshing;
    private bool _picking;
    private bool _hiddenFromCapture;
    private bool _dragging;
    private Point _dragOffset;

    public OverlayForm(AppSettings settings, string? loadError, bool demo = false)
    {
        _settings = settings;
        _loadError = loadError;
        _demo = demo;

        Text = Title;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Background;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10f);
        Padding = new Padding(12, 8, 12, 6);
        Opacity = 0.97;
        Width = 420;

        _list = new AnswerListControl
        {
            Dock = DockStyle.Fill,
            BackColor = Background,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10.5f),
        };
        _footerLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 22,
            ForeColor = Muted,
            Font = new Font("Segoe UI", 8f),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        };
        _statusLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 40,
            ForeColor = Muted,
            Font = new Font("Segoe UI", 8.5f),
            AutoEllipsis = true,
        };
        _categoryLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 32,
            Font = new Font("Segoe UI Semibold", 13f),
            AutoEllipsis = true,
            Text = Title,
        };

        _costLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 20,
            ForeColor = Muted,
            Font = new Font("Segoe UI", 8f),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        };

        // Docking is laid out from the last-added control to the first: Fill goes in first, the top label last,
        // and the footer after the cost line so the footer ends up at the very bottom.
        Controls.Add(_list);
        Controls.Add(_costLabel);
        Controls.Add(_footerLabel);
        Controls.Add(_statusLabel);
        Controls.Add(_categoryLabel);

        _list.UsedToggled += OnAnswerClicked;
        _answerService.UsageReported += OnUsageReported;
        UpdateCostLabel();

        var menu = BuildMenu();
        ContextMenuStrip = menu;
        foreach (Control c in Controls)
            c.ContextMenuStrip = menu;

        foreach (Control c in new Control[] { this, _categoryLabel, _statusLabel, _footerLabel, _costLabel })
        {
            c.MouseDown += OnDragStart;
            c.MouseMove += OnDragMove;
            c.MouseUp += OnDragEnd;
        }

        FitHeight();
        PlaceInitially();
    }

    // ---- window behaviour -------------------------------------------------------------------------------

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        int round = DWMWCP_ROUND;
        _ = DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

        _hiddenFromCapture = _settings.HideFromScreenCapture && SetWindowDisplayAffinity(Handle, WDA_EXCLUDEFROMCAPTURE);

        var problems = RegisterHotkeys();
        _list.Placeholder = $"Press {Key(HotkeyId.Ask)} when a category appears.\nRight-click here for the menu (pick game area, exit).";
        _footerLabel.Text = $"{Key(HotkeyId.Ask)} answers  ·  {Key(HotkeyId.Refresh)} cross off played  ·  {Key(HotkeyId.Toggle)} hide";
        SetStatus(StartupMessage(problems));

        if (_demo)
            ShowDemo();
    }

    /// <summary>Fills the list with a sample round (no API call) so you can see and place the window.</summary>
    private void ShowDemo()
    {
        _category = "Dog breeds (demo)";
        _categoryLabel.Text = _category;
        _used.Clear();
        AddUsed(["Beagle", "Golden Retriever"]);
        _list.SetAnswers(
        [
            "Labrador Retriever", "German Shepherd", "Poodle", "Bulldog", "Beagle", "Golden Retriever", "Rottweiler",
            "Dachshund", "Boxer", "Chihuahua", "Pug", "Husky", "Shih Tzu", "Doberman", "Great Dane", "Corgi",
            "Border Collie", "Dalmatian", "Pomeranian", "Maltese", "Greyhound", "Shiba Inu", "Akita", "Saint Bernard", "Bichon Frise",
        ], IsUsed);
        FitHeight();
        SetStatus("Demo list, no API call. Click answers to cross them off; drag the title to move the window.");
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_MOUSEACTIVATE:
                // Clicking the overlay must not pull focus away from the game's answer box.
                m.Result = MA_NOACTIVATE;
                return;
            case WM_HOTKEY:
                OnHotkey((HotkeyId)m.WParam.ToInt32());
                return;
        }
        base.WndProc(ref m);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _askCts?.Cancel();
        foreach (HotkeyId id in _hotkeys.Keys)
            UnregisterHotKey(Handle, (int)id);
        _settings.OverlayX = Left;
        _settings.OverlayY = Top;
        _settings.TrySave();
        base.OnFormClosed(e);
    }

    // ---- hotkeys ------------------------------------------------------------------------------------

    private List<string> RegisterHotkeys()
    {
        var problems = new List<string>();
        Register(HotkeyId.Ask, _settings.HotkeyAsk, nameof(AppSettings.HotkeyAsk));
        Register(HotkeyId.Refresh, _settings.HotkeyRefresh, nameof(AppSettings.HotkeyRefresh));
        Register(HotkeyId.PickRegion, _settings.HotkeyPickRegion, nameof(AppSettings.HotkeyPickRegion));
        Register(HotkeyId.Toggle, _settings.HotkeyToggle, nameof(AppSettings.HotkeyToggle));
        return problems;

        void Register(HotkeyId id, string? text, string settingName)
        {
            if (!HotkeyBinding.TryParse(text, out HotkeyBinding binding))
                problems.Add($"{settingName} \"{text}\" isn't a valid hotkey");
            else if (!RegisterHotKey(Handle, (int)id, binding.Modifiers | MOD_NOREPEAT, (uint)binding.Key))
                problems.Add($"{binding.Text} is taken by another app (change {settingName})");
            else
                _hotkeys[id] = binding;
        }
    }

    private string Key(HotkeyId id) => _hotkeys.TryGetValue(id, out HotkeyBinding b) ? b.Text : "(no hotkey)";

    private void OnHotkey(HotkeyId id)
    {
        switch (id)
        {
            case HotkeyId.Ask: _ = AskAsync(); break;
            case HotkeyId.Refresh: _ = RefreshAsync(); break;
            case HotkeyId.PickRegion: PickRegion(); break;
            case HotkeyId.Toggle: Visible = !Visible; break;
        }
    }

    private string StartupMessage(List<string> hotkeyProblems)
    {
        var lines = new List<string>();
        if (_loadError is not null)
            lines.Add(_loadError);
        if (hotkeyProblems.Count > 0)
            lines.Add("Hotkey problem: " + string.Join("; ", hotkeyProblems) + ". Edit settings.json from the right-click menu.");
        if (_settings.HideFromScreenCapture && !_hiddenFromCapture)
            lines.Add("Couldn't hide this window from screen capture on this Windows version.");
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
            lines.Add("ANTHROPIC_API_KEY isn't set. Add your key (see README), then restart the helper.");
        if (lines.Count == 0)
            lines.Add(_settings.Region is null
                ? $"Press {Key(HotkeyId.Ask)} when a category appears. Optional: {Key(HotkeyId.PickRegion)} to pick the game area."
                : $"Press {Key(HotkeyId.Ask)} when a category appears.");
        return string.Join(" ", lines);
    }

    // ---- actions ------------------------------------------------------------------------------------

    private async Task AskAsync()
    {
        if (!Visible)
            Show();

        _askCts?.Cancel(); // a newer press replaces an answer still on its way
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        _askCts = cts;
        try
        {
            SetStatus("Reading the screen…");
            byte[] png;
            using (Bitmap shot = await CaptureGameAreaAsync())
                png = ScreenGrabber.ToPng(shot);

            SetStatus($"Asking {_settings.Model}…");
            var watch = Stopwatch.StartNew();
            AnswerResult result = await _answerService.AskAsync(png, _used.ToList(), _settings, cts.Token);
            if (_askCts != cts)
                return;

            if (result.Category.Length == 0)
            {
                SetStatus($"No category found in the captured area. Pick the game area with {Key(HotkeyId.PickRegion)}, then try again.");
                return;
            }

            if (AnswerMatcher.Normalize(result.Category) != AnswerMatcher.Normalize(_category))
                _used.Clear(); // new category, new round
            _category = result.Category;
            AddUsed(result.AlreadyPlayed);

            _categoryLabel.Text = result.Category;
            _list.SetAnswers(result.Answers, IsUsed);
            FitHeight();
            SetStatus($"{result.Answers.Count} answers in {watch.Elapsed.TotalSeconds:0.0}s. Click one to cross it off; " +
                      $"{Key(HotkeyId.Refresh)} crosses off what the others played.");
        }
        catch (OperationCanceledException) when (_askCts != cts)
        {
            // Replaced by a newer press.
        }
        catch (OperationCanceledException)
        {
            SetStatus($"Claude took too long. Press {Key(HotkeyId.Ask)} again.");
        }
        catch (AnthropicUnauthorizedException)
        {
            SetStatus("The API key was rejected. Check ANTHROPIC_API_KEY (see README), then restart the helper.");
        }
        catch (AnthropicRateLimitException)
        {
            SetStatus("The Claude API is rate-limiting this key. Wait a few seconds and try again.");
        }
        catch (Anthropic5xxException)
        {
            SetStatus("The Claude API is having trouble right now. Try again in a moment.");
        }
        catch (AnthropicIOException)
        {
            SetStatus("Couldn't reach the Claude API. Check the internet connection.");
        }
        catch (Exception ex)
        {
            SetStatus("Error: " + ex.Message);
        }
        finally
        {
            if (_askCts == cts)
                _askCts = null;
            cts.Dispose();
        }
    }

    private async Task RefreshAsync()
    {
        if (!_list.HasAnswers)
        {
            SetStatus($"Nothing to cross off yet. Press {Key(HotkeyId.Ask)} when a category appears.");
            return;
        }
        if (_refreshing)
            return;

        _refreshing = true;
        try
        {
            string screenText;
            using (Bitmap shot = await CaptureGameAreaAsync())
                screenText = await OcrReader.ReadTextAsync(shot);

            List<string> seen = AnswerMatcher.FindOnScreen(_list.UnusedAnswers, screenText, _category);
            AddUsed(seen);
            int crossed = _list.MarkUsed(seen);
            SetStatus(crossed == 0
                ? "No new played answers spotted on screen."
                : $"Crossed off {crossed}: {string.Join(", ", seen)}");
        }
        catch (Exception ex)
        {
            SetStatus("Couldn't read the screen: " + ex.Message);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void PickRegion()
    {
        if (_picking)
            return;
        _picking = true;
        bool hideWhilePicking = Visible && !_hiddenFromCapture;
        if (hideWhilePicking)
            Hide();
        try
        {
            using var picker = new RegionPickerForm();
            if (picker.ShowDialog() != DialogResult.OK)
            {
                SetStatus("Game area unchanged.");
                return;
            }

            _settings.Region = picker.UseWholeScreen || picker.SelectedArea is null ? null : RegionSetting.From(picker.SelectedArea.Value);
            _settings.TrySave();
            SetStatus(_settings.Region is null
                ? $"Using the whole screen. Press {Key(HotkeyId.Ask)} when a category appears."
                : $"Game area saved ({_settings.Region.Width}×{_settings.Region.Height}). Press {Key(HotkeyId.Ask)} when a category appears.");
        }
        catch (Exception ex)
        {
            SetStatus("Couldn't pick the area: " + ex.Message);
        }
        finally
        {
            if (hideWhilePicking)
                Show();
            _picking = false;
        }
    }

    private void ClearList()
    {
        _askCts?.Cancel();
        _used.Clear();
        _category = "";
        _categoryLabel.Text = Title;
        _list.ClearAnswers();
        FitHeight();
        SetStatus($"Cleared. Press {Key(HotkeyId.Ask)} when a category appears.");
    }

    /// <summary>Screenshot of the game area, without this overlay in it.</summary>
    private async Task<Bitmap> CaptureGameAreaAsync()
    {
        Rectangle area = _settings.Region?.ToRectangle() ?? Screen.FromPoint(Cursor.Position).Bounds;
        area.Intersect(SystemInformation.VirtualScreen);
        if (area.Width < 10 || area.Height < 10)
            throw new InvalidOperationException($"the saved game area is off-screen; press {Key(HotkeyId.PickRegion)} to pick it again.");

        // When the overlay can't hide itself from capture, step aside for the screenshot if it overlaps.
        bool stepAside = Visible && !_hiddenFromCapture && Bounds.IntersectsWith(area);
        if (stepAside)
        {
            Hide();
            await Task.Delay(150); // let the desktop repaint underneath
        }
        try
        {
            return ScreenGrabber.Capture(area);
        }
        finally
        {
            if (stepAside)
                Show();
        }
    }

    // ---- used answers -------------------------------------------------------------------------------

    private bool IsUsed(string answer) => _used.Any(u => AnswerMatcher.SameAnswer(u, answer));

    private void AddUsed(IEnumerable<string> answers)
    {
        foreach (string answer in answers)
            if (!IsUsed(answer))
                _used.Add(answer);
    }

    private void OnAnswerClicked(string answer, bool nowUsed)
    {
        if (nowUsed)
            AddUsed([answer]);
        else
            _used.RemoveAll(u => AnswerMatcher.SameAnswer(u, answer));
    }

    // ---- cost readout -------------------------------------------------------------------------------

    private void OnUsageReported(RequestUsage usage)
    {
        _lastUsage = usage;
        _sessionRequests++;
        _sessionDollars += usage.Dollars ?? 0m;
        _ledger.Add(usage);
        _ledger.TrySave();
        UpdateCostLabel();
    }

    private void ResetCostTotal()
    {
        _ledger = new SpendLedger();
        _ledger.TrySave();
        _lastUsage = null;
        _sessionRequests = 0;
        _sessionDollars = 0m;
        UpdateCostLabel();
        SetStatus("Cost total reset.");
    }

    private void UpdateCostLabel()
    {
        if (_ledger.Requests == 0)
        {
            _costLabel.Text = "Cost: nothing spent yet";
            _costTip.SetToolTip(_costLabel, "Each answer request shows its estimated cost here.");
            return;
        }

        string last = _lastUsage is { } u ? (u.Dollars is decimal d ? Pricing.Format(d) : "?") : "–";
        string total = Pricing.Format(_ledger.Dollars) + (_ledger.UnpricedRequests > 0 ? "+" : "");
        string asks = _sessionRequests == 1 ? "1 ask" : $"{_sessionRequests} asks";
        _costLabel.Text = $"Cost: last {last}  ·  session {Pricing.Format(_sessionDollars)} in {asks}  ·  " +
                          $"total {total} since {_ledger.Since:d MMM}";

        string lastDetail = _lastUsage is { } lu
            ? $"Last ask: {lu.InputTokens:N0} input + {lu.OutputTokens:N0} output tokens on {lu.Model}" +
              (lu.Dollars is null ? " (no price on file for this model, so it isn't in the totals)." : ".")
            : "No ask yet this session.";
        _costTip.SetToolTip(_costLabel,
            $"{lastDetail}\n" +
            $"All time: {_ledger.Requests:N0} asks, {_ledger.InputTokens:N0} input + {_ledger.OutputTokens:N0} output tokens.\n" +
            "Estimates from list prices. platform.claude.com > Usage has the exact figures.\n" +
            "Right-click > Reset cost total starts the count again.");
    }

    // ---- layout, menu, dragging ---------------------------------------------------------------------

    private void SetStatus(string text) => _statusLabel.Text = text;

    private void FitHeight()
    {
        Height = Padding.Vertical + _categoryLabel.Height + _statusLabel.Height + _list.PreferredListHeight
                 + _costLabel.Height + _footerLabel.Height + 4;
        KeepOnScreen();
    }

    private void PlaceInitially()
    {
        if (_settings.OverlayX is int x && _settings.OverlayY is int y
            && Screen.AllScreens.Any(s => s.WorkingArea.Contains(x + 20, y + 20)))
        {
            Location = new Point(x, y);
            return;
        }
        Rectangle work = Screen.PrimaryScreen?.WorkingArea ?? SystemInformation.VirtualScreen;
        Location = new Point(work.Right - Width - 16, work.Top + 120);
    }

    private void KeepOnScreen()
    {
        Rectangle work = Screen.FromRectangle(Bounds).WorkingArea;
        int left = Math.Clamp(Left, work.Left, Math.Max(work.Left, work.Right - Width));
        int top = Math.Clamp(Top, work.Top, Math.Max(work.Top, work.Bottom - Height));
        Location = new Point(left, top);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Get answers now", null, (_, _) => _ = AskAsync());
        menu.Items.Add("Cross off what others played", null, (_, _) => _ = RefreshAsync());
        menu.Items.Add("Pick game area…", null, (_, _) => PickRegion());
        menu.Items.Add("Use the whole screen", null, (_, _) =>
        {
            _settings.Region = null;
            _settings.TrySave();
            SetStatus("Using the whole screen.");
        });
        menu.Items.Add("Clear list", null, (_, _) => ClearList());
        menu.Items.Add("Reset cost total", null, (_, _) => ResetCostTotal());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open settings file", null, (_, _) => OpenSettingsFile());
        menu.Items.Add("Exit", null, (_, _) => Close());
        return menu;
    }

    private void OpenSettingsFile()
    {
        _settings.TrySave();
        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{AppSettings.FilePath}\"") { UseShellExecute = true });
            SetStatus("Settings changes take effect after you restart the helper.");
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't open {AppSettings.FilePath}: {ex.Message}");
        }
    }

    private void OnDragStart(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;
        _dragging = true;
        _dragOffset = new Point(Cursor.Position.X - Left, Cursor.Position.Y - Top);
    }

    private void OnDragMove(object? sender, MouseEventArgs e)
    {
        if (_dragging)
            Location = new Point(Cursor.Position.X - _dragOffset.X, Cursor.Position.Y - _dragOffset.Y);
    }

    private void OnDragEnd(object? sender, MouseEventArgs e)
    {
        if (!_dragging)
            return;
        _dragging = false;
        KeepOnScreen();
        _settings.OverlayX = Left;
        _settings.OverlayY = Top;
        _settings.TrySave();
    }
}
