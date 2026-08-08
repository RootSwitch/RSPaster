// RSPaster - types multi-line text into whatever window has focus, for targets
// that ignore the clipboard: hypervisor VM consoles, VNC, IPMI/BMC KVM, and UAC
// prompts shown on the normal desktop.
//
// C# 5 only (in-box csc).

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace RSPaster
{
    public class MainForm : Form
    {
        const int HOTKEY_ID = 1;
        const int WM_HOTKEY = 0x0312;
        const uint MOD_ALT = 0x0001;
        const uint MOD_CONTROL = 0x0002;
        const uint VK_V = 0x56;
        const string HOTKEY_LABEL = "Ctrl+Alt+V";

        enum RunState { Idle, Countdown, Typing }

        Settings _settings;

        Panel _topBar;
        Panel _content;
        Panel _bottom;
        Panel _statusBar;
        ScrollAwareTextBox _txtInput;
        InputHost _txtHost;
        ThemedScrollBar _scroll;
        bool _syncing;
        SpinBox _startDelay;
        SpinBox _keyDelay;
        SpinBox _lineDelay;
        Label _lblStart;
        Label _lblKey;
        Label _lblLineUnit;
        ThemedCheck _chkLineDelay;
        ThemedCheck _chkUnicode;
        ThemedCheck _chkEnterAtEnd;
        ThemedCheck _chkClearAfter;
        ThemedCheck _chkOnTop;
        ThemedButton _btnGo;
        ThemedButton _btnTheme;
        Label _lblStatus;
        Label _lnkAdmin;

        ContextMenuStrip _themeMenu;
        NotifyIcon _tray;
        ContextMenuStrip _trayMenu;
        ToolStripMenuItem _trayShow;
        ToolStripMenuItem _trayType;
        IntPtr _iconHandle = IntPtr.Zero;
        Icon _icon;

        System.Windows.Forms.Timer _countdownTimer;
        RunState _state = RunState.Idle;
        int _remainingSeconds;
        volatile bool _cancelRequested;
        bool _hotkeyRegistered;
        bool _exiting;
        bool _trayHintShown;
        string _baseTitle = "RSPaster";

        public MainForm()
        {
            _settings = Settings.Load();
            Th.Set(_settings.Theme);

            if (IsElevated()) _baseTitle += " [Admin]";
            Text = _baseTitle;
            ClientSize = new Size(
                _settings.WindowWidth >= 520 ? _settings.WindowWidth : 600,
                _settings.WindowHeight >= 500 ? _settings.WindowHeight : 530);
            MinimumSize = new Size(520, 500);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            TopMost = _settings.AlwaysOnTop;
            Font = new Font("Segoe UI", 9F);
            DoubleBuffered = true;

            BuildTopBar();
            BuildStatusBar();
            BuildContent();
            BuildThemeMenu();
            BuildTray();

            // Docked children are z-ordered back to front, so Fill is added first.
            Controls.Add(_content);
            Controls.Add(_topBar);
            Controls.Add(_statusBar);

            _countdownTimer = new System.Windows.Forms.Timer();
            _countdownTimer.Interval = 1000;
            _countdownTimer.Tick += delegate(object s, EventArgs e) { OnCountdownTick(); };

            Th.Changed += delegate(object s, EventArgs e) { ApplyTheme(); };
            ApplyTheme();
        }

        // ---- construction ---------------------------------------------------

        void BuildTopBar()
        {
            _topBar = new Panel();
            _topBar.Dock = DockStyle.Top;
            _topBar.Height = 42;
            _topBar.Paint += PaintTopBar;

            _btnTheme = new ThemedButton();
            _btnTheme.Text = "Theme";
            _btnTheme.Size = new Size(84, 26);
            _btnTheme.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnTheme.Location = new Point(_topBar.Width - 96, 8);
            _btnTheme.Click += delegate(object s, EventArgs e)
            {
                MenuTheme.Apply(_themeMenu);
                _themeMenu.Show(_btnTheme, new Point(0, _btnTheme.Height + 2));
            };
            _topBar.Controls.Add(_btnTheme);
            _topBar.Resize += delegate(object s, EventArgs e)
            {
                _btnTheme.Location = new Point(_topBar.Width - _btnTheme.Width - 12, 8);
            };
        }

        void PaintTopBar(object sender, PaintEventArgs e)
        {
            Theme t = Th.T;
            e.Graphics.Clear(t.Panel);
            using (Pen p = new Pen(t.Border))
                e.Graphics.DrawLine(p, 0, _topBar.Height - 1, _topBar.Width, _topBar.Height - 1);

            Brand.PaintMark(e.Graphics, new Rectangle(11, 10, 22, 22), t.Accent, t.Accent);

            using (Font f = new Font("Segoe UI", 10.5F, FontStyle.Bold))
                TextRenderer.DrawText(e.Graphics, "RSPaster", f,
                    new Rectangle(40, 0, 220, _topBar.Height - 1), t.Txt,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            using (Font f = new Font("Segoe UI", 8.25F))
                TextRenderer.DrawText(e.Graphics, _hotkeyRegistered ? HOTKEY_LABEL : "hotkey unavailable", f,
                    new Rectangle(122, 0, 200, _topBar.Height - 1), t.TxtDim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        void BuildStatusBar()
        {
            _statusBar = new Panel();
            _statusBar.Dock = DockStyle.Bottom;
            _statusBar.Height = 26;
            _statusBar.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.Clear(Th.T.Panel);
                using (Pen p = new Pen(Th.T.Border))
                    e.Graphics.DrawLine(p, 0, 0, _statusBar.Width, 0);
            };

            _lblStatus = new Label();
            _lblStatus.AutoSize = false;
            _lblStatus.Location = new Point(11, 1);
            _lblStatus.Size = new Size(380, 24);
            _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            _lblStatus.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            _lblStatus.Text = "Paste text, then focus the target during the countdown.";

            _lnkAdmin = new Label();
            _lnkAdmin.AutoSize = true;
            _lnkAdmin.TextAlign = ContentAlignment.MiddleRight;
            _lnkAdmin.Text = "Restart as Admin";
            _lnkAdmin.Cursor = Cursors.Hand;
            _lnkAdmin.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            _lnkAdmin.Visible = !IsElevated();
            _lnkAdmin.Click += delegate(object s, EventArgs e) { RestartAsAdmin(); };

            _statusBar.Controls.Add(_lblStatus);
            _statusBar.Controls.Add(_lnkAdmin);
            _statusBar.Resize += delegate(object s, EventArgs e) { LayoutStatusBar(); };
        }

        void LayoutStatusBar()
        {
            _lnkAdmin.Location = new Point(_statusBar.Width - _lnkAdmin.Width - 11, 5);
            _lblStatus.Size = new Size(Math.Max(80, _lnkAdmin.Left - 20), 24);
        }

        void BuildContent()
        {
            _content = new Panel();
            _content.Dock = DockStyle.Fill;
            _content.Padding = new Padding(12, 12, 12, 8);

            _txtInput = new ScrollAwareTextBox();
            _txtInput.Multiline = true;
            _txtInput.AcceptsReturn = true;
            _txtInput.AcceptsTab = true;
            // Wrapping on is what removes the need for a horizontal scrollbar,
            // and for reviewing a long command before sending it, seeing the
            // whole line beats scrolling sideways through it.
            _txtInput.WordWrap = true;
            _txtInput.ScrollBars = ScrollBars.None;
            _txtInput.BorderStyle = BorderStyle.None;
            _txtInput.Font = new Font("Consolas", 10F);

            _txtHost = new InputHost(_txtInput, 6, 5);
            _txtHost.Dock = DockStyle.Fill;

            _scroll = new ThemedScrollBar();
            _scroll.Dock = DockStyle.Right;
            _txtHost.Controls.Add(_scroll);   // docks before the Fill child
            _txtInput.ViewChanged += delegate(object s, EventArgs e) { SyncScrollFromText(); };
            _txtInput.TextChanged += delegate(object s, EventArgs e) { SyncScrollFromText(); };
            _scroll.UserScrolled += delegate(object s, EventArgs e)
            {
                if (_syncing) return;
                _syncing = true;
                _txtInput.ScrollToLine(_scroll.Value);
                _syncing = false;
            };

            _bottom = new Panel();
            _bottom.Dock = DockStyle.Bottom;
            _bottom.Height = 152;
            _bottom.Padding = new Padding(0, 10, 0, 0);

            _lblStart = MakeLabel("Start delay (s)", 0, 16);
            _startDelay = new SpinBox(0, 60, _settings.StartDelaySeconds);
            _startDelay.SetBounds(96, 10, 64, 27);

            _lblKey = MakeLabel("Key delay (ms)", 176, 16);
            _keyDelay = new SpinBox(0, 500, _settings.KeyDelayMs);
            _keyDelay.SetBounds(268, 10, 64, 27);

            ToolTip tip = new ToolTip();

            _chkUnicode = MakeCheck("Unicode mode", 350, 13, _settings.UnicodeMode);
            tip.SetToolTip(_chkUnicode,
                "Send every character as a unicode event instead of scancodes.\r\n" +
                "Use this if the typed output comes out garbled, which means the\r\n" +
                "target's keyboard layout differs from yours.");

            _chkLineDelay = MakeCheck("Delay between lines", 0, 48, _settings.LineDelayEnabled);
            _lineDelay = new SpinBox(0, 600, _settings.LineDelaySeconds);
            _lineDelay.SetBounds(176, 45, 64, 27);
            _lineDelay.Enabled = _settings.LineDelayEnabled;
            _lblLineUnit = MakeLabel("seconds", 248, 51);
            _chkLineDelay.CheckedChanged += delegate(object s, EventArgs e)
            {
                _lineDelay.Enabled = _chkLineDelay.Checked;
            };
            string lineDelayHelp =
                "Wait after each Enter before typing the next line.\r\n" +
                "For slow or busy machines where a command needs time to\r\n" +
                "finish before the next one can be entered. Cancel stays\r\n" +
                "responsive during the wait.";
            tip.SetToolTip(_chkLineDelay, lineDelayHelp);
            tip.SetToolTip(_lineDelay, lineDelayHelp);

            _chkEnterAtEnd = MakeCheck("Press Enter at end", 0, 82, _settings.EnterAtEnd);
            _chkClearAfter = MakeCheck("Clear after typing", 148, 82, _settings.ClearAfter);
            _chkOnTop = MakeCheck("Always on top", 296, 82, _settings.AlwaysOnTop);
            _chkOnTop.CheckedChanged += delegate(object s, EventArgs e)
            {
                TopMost = _chkOnTop.Checked;
            };

            _btnGo = new ThemedButton();
            _btnGo.Primary = true;
            _btnGo.Font = new Font("Segoe UI", 9.75F, FontStyle.Bold);
            _btnGo.SetBounds(0, 110, 100, 36);
            _btnGo.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            _btnGo.Click += delegate(object s, EventArgs e) { StartOrCancel(); };

            _bottom.Controls.Add(_lblStart);
            _bottom.Controls.Add(_startDelay);
            _bottom.Controls.Add(_lblKey);
            _bottom.Controls.Add(_keyDelay);
            _bottom.Controls.Add(_chkUnicode);
            _bottom.Controls.Add(_chkLineDelay);
            _bottom.Controls.Add(_lineDelay);
            _bottom.Controls.Add(_lblLineUnit);
            _bottom.Controls.Add(_chkEnterAtEnd);
            _bottom.Controls.Add(_chkClearAfter);
            _bottom.Controls.Add(_chkOnTop);
            _bottom.Controls.Add(_btnGo);
            _bottom.Resize += delegate(object s, EventArgs e)
            {
                _btnGo.Width = Math.Max(120, _bottom.Width);
            };

            _content.Controls.Add(_txtHost);
            _content.Controls.Add(_bottom);
        }

        void SyncScrollFromText()
        {
            if (_syncing) return;
            _syncing = true;
            _scroll.Configure(_txtInput.VisualLineCount, _txtInput.VisibleLines, _txtInput.FirstVisibleLine);
            _syncing = false;
        }

        Label MakeLabel(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Location = new Point(x, y);
            return l;
        }

        ThemedCheck MakeCheck(string text, int x, int y, bool chk)
        {
            ThemedCheck c = new ThemedCheck();
            // Set the font before measuring: a control not yet added to a parent
            // still carries Control.DefaultFont, which is narrower than the form
            // font it will inherit, and the label paints clipped.
            c.Font = Font;
            c.Text = text;
            c.Checked = chk;
            c.Location = new Point(x, y);
            c.SizeToText();
            return c;
        }

        void BuildThemeMenu()
        {
            _themeMenu = new ContextMenuStrip();
            _themeMenu.Renderer = new ToolStripProfessionalRenderer(new ThemeColorTable());
            _themeMenu.ShowImageMargin = false;

            ToolStripMenuItem group = null;
            foreach (Theme t in Themes.All)
            {
                ToolStripMenuItem item = new ToolStripMenuItem(t.Label);
                item.Tag = t.Key;
                item.Click += OnThemePicked;
                if (t.Group == null)
                {
                    _themeMenu.Items.Add(item);
                }
                else
                {
                    if (group == null || group.Text != t.Group)
                    {
                        group = new ToolStripMenuItem(t.Group);
                        _themeMenu.Items.Add(group);
                    }
                    group.DropDownItems.Add(item);
                }
            }
            _themeMenu.Opening += delegate(object s, CancelEventArgs e) { MarkActiveTheme(); };
        }

        void MarkActiveTheme()
        {
            foreach (ToolStripItem raw in _themeMenu.Items)
            {
                ToolStripMenuItem item = raw as ToolStripMenuItem;
                if (item == null) continue;
                if (item.HasDropDownItems)
                {
                    foreach (ToolStripItem sub in item.DropDownItems)
                    {
                        ToolStripMenuItem s = sub as ToolStripMenuItem;
                        if (s != null) s.Checked = Equals(s.Tag, Th.T.Key);
                    }
                }
                else
                {
                    item.Checked = Equals(item.Tag, Th.T.Key);
                }
            }
        }

        void OnThemePicked(object sender, EventArgs e)
        {
            ToolStripMenuItem item = (ToolStripMenuItem)sender;
            Th.Set((string)item.Tag);
            _settings.Theme = (string)item.Tag;
            _settings.Save();
        }

        void BuildTray()
        {
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Renderer = new ToolStripProfessionalRenderer(new ThemeColorTable());
            _trayMenu.ShowImageMargin = false;

            _trayShow = new ToolStripMenuItem("Show RSPaster");
            _trayShow.ToolTipText = "Restore the window";
            _trayShow.Click += delegate(object s, EventArgs e) { RestoreWindow(); };

            _trayType = new ToolStripMenuItem("Type after delay");
            _trayType.ToolTipText = "Same as " + HOTKEY_LABEL + ": focus your target first";
            _trayType.Click += delegate(object s, EventArgs e) { StartOrCancel(); };

            ToolStripMenuItem exit = new ToolStripMenuItem("Exit");
            exit.ToolTipText = "Quit RSPaster and release the hotkey";
            exit.Click += delegate(object s, EventArgs e) { _exiting = true; Close(); };

            _trayMenu.Items.Add(_trayShow);
            _trayMenu.Items.Add(_trayType);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(exit);
            _trayMenu.Opening += delegate(object s, CancelEventArgs e)
            {
                _trayShow.Text = Visible ? "Hide RSPaster" : "Show RSPaster";
                MenuTheme.Apply(_trayMenu);
            };

            _tray = new NotifyIcon();
            _tray.ContextMenuStrip = _trayMenu;
            _tray.Text = "RSPaster";
            _tray.Visible = true;
            _tray.DoubleClick += delegate(object s, EventArgs e)
            {
                if (Visible && WindowState != FormWindowState.Minimized) HideToTray(false);
                else RestoreWindow();
            };
        }

        // ---- theming --------------------------------------------------------

        void ApplyTheme()
        {
            Theme t = Th.T;
            BackColor = t.Panel2;
            ForeColor = t.Txt;

            _content.BackColor = t.Panel2;
            _bottom.BackColor = t.Panel2;
            _topBar.BackColor = t.Panel;
            _statusBar.BackColor = t.Panel;

            _txtInput.BackColor = t.Input;
            _txtInput.ForeColor = t.Txt;

            // .form-grid label { color: var(--se-txt-dim) }
            _lblStart.BackColor = t.Panel2;
            _lblStart.ForeColor = t.TxtDim;
            _lblKey.BackColor = t.Panel2;
            _lblKey.ForeColor = t.TxtDim;

            _lblStatus.BackColor = t.Panel;
            _lblStatus.ForeColor = t.TxtDim;
            _lnkAdmin.BackColor = t.Panel;
            _lnkAdmin.ForeColor = t.Accent;

            UpdateTrayIcon();
            OsChrome.ApplyTitleBar(this);
            OsChrome.ApplyScrollBars(_txtInput);

            _topBar.Invalidate();
            _statusBar.Invalidate();
            Invalidate(true);
        }

        void UpdateTrayIcon()
        {
            IntPtr old = _iconHandle;
            Icon fresh = Brand.CreateIcon(32, out _iconHandle);
            _icon = fresh;
            _tray.Icon = fresh;
            Icon = fresh;
            // Only safe once both the tray and the form have taken the new one.
            if (old != IntPtr.Zero) Native.DestroyIcon(old);
        }

        // ---- window / tray behaviour ---------------------------------------

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _hotkeyRegistered = Native.RegisterHotKey(Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_V);
            if (!_hotkeyRegistered)
                SetStatus(HOTKEY_LABEL + " is taken by another app - use the button instead.");
            UpdateGoButtonText();
            // The constructor ran before there was a handle to theme.
            OsChrome.ApplyTitleBar(this);
            OsChrome.ApplyScrollBars(_txtInput);
            _topBar.Invalidate();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (_hotkeyRegistered) Native.UnregisterHotKey(Handle, HOTKEY_ID);
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                // No Activate() here on purpose: when the hotkey fires, the
                // target window must keep focus or the keys land in RSPaster.
                StartOrCancel();
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized) HideToTray(true);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveSettings();
            // X hides rather than exits, so the hotkey survives; Exit lives in
            // the tray menu. Windows shutdown and task-manager close still win.
            if (!_exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray(true);
                return;
            }
            _tray.Visible = false;
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _cancelRequested = true;
            _tray.Visible = false;
            _tray.Dispose();
            if (_iconHandle != IntPtr.Zero) Native.DestroyIcon(_iconHandle);
            base.OnFormClosed(e);
        }

        void HideToTray(bool announce)
        {
            Hide();
            ShowInTaskbar = false;
            if (announce && !_trayHintShown)
            {
                _trayHintShown = true;
                _tray.BalloonTipTitle = "RSPaster is still running";
                _tray.BalloonTipText = _hotkeyRegistered
                    ? HOTKEY_LABEL + " still works. Right-click the tray icon to exit."
                    : "Right-click the tray icon to show the window or exit.";
                _tray.ShowBalloonTip(4000);
            }
        }

        void RestoreWindow()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            _txtInput.Focus();
        }

        void SaveSettings()
        {
            _settings.StartDelaySeconds = _startDelay.Value;
            _settings.KeyDelayMs = _keyDelay.Value;
            _settings.LineDelayEnabled = _chkLineDelay.Checked;
            _settings.LineDelaySeconds = _lineDelay.Value;
            _settings.EnterAtEnd = _chkEnterAtEnd.Checked;
            _settings.ClearAfter = _chkClearAfter.Checked;
            _settings.AlwaysOnTop = _chkOnTop.Checked;
            _settings.UnicodeMode = _chkUnicode.Checked;
            _settings.Theme = Th.T.Key;
            if (WindowState == FormWindowState.Normal)
            {
                _settings.WindowWidth = ClientSize.Width;
                _settings.WindowHeight = ClientSize.Height;
            }
            _settings.Save();
        }

        // ---- typing ---------------------------------------------------------

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && _state != RunState.Idle)
            {
                StartOrCancel();
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        void StartOrCancel()
        {
            if (_state == RunState.Idle) StartCountdown();
            else CancelRun();
        }

        void StartCountdown()
        {
            if (_txtInput.Text.Length == 0)
            {
                SetStatus("Nothing to type - paste some text first.");
                return;
            }
            _state = RunState.Countdown;
            _cancelRequested = false;
            SetInputsEnabled(false);
            UpdateGoButtonText();
            _remainingSeconds = _startDelay.Value;
            if (_remainingSeconds <= 0)
            {
                BeginTyping();
            }
            else
            {
                UpdateCountdownUi();
                _countdownTimer.Start();
            }
        }

        void OnCountdownTick()
        {
            _remainingSeconds--;
            if (_remainingSeconds > 0)
            {
                UpdateCountdownUi();
            }
            else
            {
                _countdownTimer.Stop();
                BeginTyping();
            }
        }

        void UpdateCountdownUi()
        {
            SetStatus(string.Format(CultureInfo.InvariantCulture,
                "Typing in {0}s - focus the target window now.", _remainingSeconds));
            Text = string.Format(CultureInfo.InvariantCulture, "[{0}] {1}", _remainingSeconds, _baseTitle);
        }

        void BeginTyping()
        {
            _state = RunState.Typing;
            Text = "[typing] " + _baseTitle;
            SetStatus("Typing...");

            string text = _txtInput.Text;
            if (_chkEnterAtEnd.Checked) text += "\n";

            TypeOptions options = new TypeOptions();
            options.PerKeyDelayMs = _keyDelay.Value;
            options.UnicodeMode = _chkUnicode.Checked;
            options.LineDelayMs = _chkLineDelay.Checked ? _lineDelay.Value * 1000 : 0;
            options.Cancelled = delegate() { return _cancelRequested; };
            options.Progress = delegate(int done, int total)
            {
                Report(string.Format(CultureInfo.InvariantCulture,
                    "Typing... {0}/{1} characters", done, total));
            };
            options.LineWait = delegate(int msLeft, int nextLine, int totalLines)
            {
                Report(string.Format(CultureInfo.InvariantCulture,
                    "Waiting {0}s before line {1} of {2}. Esc cancels.",
                    (msLeft + 999) / 1000, nextLine, totalLines));
            };

            Thread worker = new Thread(delegate()
            {
                TypeResult result = KeySender.TypeText(text, options);
                try
                {
                    BeginInvoke(new Action(delegate() { FinishTyping(result); }));
                }
                catch (InvalidOperationException) { }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        void FinishTyping(TypeResult result)
        {
            _state = RunState.Idle;
            Text = _baseTitle;
            UpdateGoButtonText();
            SetInputsEnabled(true);

            if (result.Cancelled)
                SetStatus(string.Format(CultureInfo.InvariantCulture,
                    "Cancelled after {0} characters.", result.KeysSent));
            else if (result.Blocked > 0)
                SetStatus(string.Format(CultureInfo.InvariantCulture,
                    "Done, but {0} keystrokes were blocked - if the target is elevated, restart as Admin.",
                    result.Blocked));
            else
                SetStatus(string.Format(CultureInfo.InvariantCulture,
                    "Done - {0} characters typed.", result.KeysSent));

            if (_chkClearAfter.Checked && !result.Cancelled) _txtInput.Clear();
        }

        void CancelRun()
        {
            if (_state == RunState.Countdown)
            {
                _countdownTimer.Stop();
                _state = RunState.Idle;
                Text = _baseTitle;
                UpdateGoButtonText();
                SetInputsEnabled(true);
                SetStatus("Cancelled.");
            }
            else if (_state == RunState.Typing)
            {
                // The worker notices between keystrokes and FinishTyping resets the UI.
                _cancelRequested = true;
            }
        }

        void UpdateGoButtonText()
        {
            if (_state == RunState.Idle)
                _btnGo.Text = _hotkeyRegistered
                    ? "Type after delay  (" + HOTKEY_LABEL + ")"
                    : "Type after delay";
            else
                _btnGo.Text = _hotkeyRegistered
                    ? "Cancel  (Esc or " + HOTKEY_LABEL + ")"
                    : "Cancel  (Esc)";
            _trayType.Text = _state == RunState.Idle ? "Type after delay" : "Cancel";
        }

        // Status from the typing thread. Marshals to the UI thread and tolerates
        // the window being closed mid-run.
        void Report(string text)
        {
            try
            {
                BeginInvoke(new Action(delegate() { SetStatus(text); }));
            }
            catch (InvalidOperationException) { }
        }

        // The tray tooltip carries the same text as the status bar, because the
        // window is often hidden while the countdown runs.
        void SetStatus(string text)
        {
            _lblStatus.Text = text;
            _tray.Text = text.Length > 62 ? text.Substring(0, 59) + "..." : text;
        }

        void SetInputsEnabled(bool enabled)
        {
            _txtInput.ReadOnly = !enabled;
            _startDelay.Enabled = enabled;
            _keyDelay.Enabled = enabled;
            _chkLineDelay.Enabled = enabled;
            _lineDelay.Enabled = enabled && _chkLineDelay.Checked;
            _chkEnterAtEnd.Enabled = enabled;
            _chkClearAfter.Enabled = enabled;
            _chkUnicode.Enabled = enabled;
        }

        // ---- elevation ------------------------------------------------------

        static bool IsElevated()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        void RestartAsAdmin()
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = Program.RelaunchFile != null ? Program.RelaunchFile : Application.ExecutablePath;
            psi.Arguments = Program.RelaunchArgs != null ? Program.RelaunchArgs : "";
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            try
            {
                SaveSettings();
                Process.Start(psi);
                _exiting = true;
                Close();
            }
            catch (Win32Exception)
            {
                SetStatus("Elevation cancelled.");
            }
        }
    }

    public static class Program
    {
        // Set by RSPaster.ps1 so "Restart as Admin" relaunches the script.
        // Left null when running as the compiled exe.
        public static string RelaunchFile;
        public static string RelaunchArgs;

        [STAThread]
        public static void Main()
        {
            Run();
        }

        public static void Run()
        {
            Application.EnableVisualStyles();
            try { Application.SetCompatibleTextRenderingDefault(false); }
            catch (InvalidOperationException) { }   // a control already exists (Add-Type host)
            OsChrome.EnableDarkModeSupport();       // must precede the first window
            Application.Run(new MainForm());
        }
    }
}
