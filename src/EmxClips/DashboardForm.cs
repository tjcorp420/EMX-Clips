using System.Diagnostics;
using System.Collections.Specialized;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.VisualBasic.FileIO;

namespace EmxClips;

public sealed class DashboardForm : Form
{
    private const int WmNcHitTest = 0x0084;
    private const int WmNcLeftButtonDown = 0x00A1;
    private const int HtClient = 1;
    private const int HtCaption = 2;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const int ResizeBorder = 8;

    private readonly AppSettings _settings;
    private readonly Image? _logo;
    private readonly ListView _clips = new();
    private readonly Label _status = new();
    private readonly NoWheelNumericUpDown _clipLength = new();
    private readonly NoWheelNumericUpDown _memoryLimit = new();
    private readonly TextBox _clipsFolder = new();
    private readonly TextBox _obsPath = new();
    private readonly TextBox _host = new();
    private readonly NoWheelNumericUpDown _port = new();
    private readonly TextBox _password = new();
    private readonly TextBox _hotkeyDisplay = new();
    private readonly TextBox _toggleHotkeyDisplay = new();
    private readonly EmxCheckBox _useFirebaseCloudShare = new();
    private readonly TextBox _firebaseApiKey = new();
    private readonly TextBox _firebaseStorageBucket = new();
    private readonly TextBox _firebaseSessionId = new();
    private readonly ComboBox _microphone = new();
    private readonly EmxCheckBox _captureMic = new();
    private readonly ProgressBar _micLevel = new();
    private readonly Label _micLevelText = new();
    private readonly System.Windows.Forms.Timer _micTestTimer = new() { Interval = 120 };
    private readonly Label _obsStatus = new();
    private readonly Panel _pageHost = new();
    private readonly EmxCheckBox _autoLaunch = new();
    private readonly EmxCheckBox _autoStart = new();
    private readonly EmxCheckBox _minimizeObs = new();
    private readonly EmxCheckBox _dedicatedObsWorkspace = new();
    private Keys _selectedHotkeyKey = Keys.F8;
    private HotkeyModifiers _selectedHotkeyModifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt;
    private Keys _selectedToggleHotkeyKey = Keys.H;
    private HotkeyModifiers _selectedToggleHotkeyModifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt;
    private Control? _clipsPage;
    private Control? _settingsPage;
    private Button? _clipsTabButton;
    private Button? _settingsTabButton;
    private Button? _micTestButton;
    private bool _micTestRunning;
    private bool _loadingMicrophones;

    public DashboardForm(AppSettings settings, Icon icon)
    {
        _settings = settings;
        _logo = LoadLogoImage();

        Text = "EMX Clips";
        Icon = (Icon)icon.Clone();
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 620);
        ClientSize = new Size(1040, 680);
        BackColor = EmxTheme.Background;
        ForeColor = EmxTheme.Text;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        DoubleBuffered = true;

        Controls.Add(BuildLayout());
        _micTestTimer.Tick += (_, _) => UpdateMicTestMeter();
        _microphone.SelectedIndexChanged += (_, _) =>
        {
            if (!_loadingMicrophones && _captureMic.Checked)
            {
                SetStatus("Mic changed. Click Auto Setup Mic so OBS uses this device in new clips.");
            }
        };
        LoadSettingsIntoControls();
        RefreshClips();
    }

    public event EventHandler? SaveClipRequested;
    public event EventHandler? StartReplayBufferRequested;
    public event EventHandler? StopReplayBufferRequested;
    public event EventHandler? AutoSetupCaptureRequested;
    public event EventHandler? AutoSetupMicRequested;
    public event EventHandler? PhoneCompanionRequested;
    public event EventHandler? InstallObsRequested;
    public event EventHandler? CheckUpdatesRequested;
    public event EventHandler? HideToTrayRequested;
    public event EventHandler? SettingsSaved;

    public void RefreshClips()
    {
        var clips = ClipLibrary.Load(_settings.ClipsFolder);

        _clips.BeginUpdate();
        try
        {
            _clips.Items.Clear();

            foreach (var clip in clips)
            {
                var item = new ListViewItem(clip.Name)
                {
                    Tag = clip,
                    ForeColor = EmxTheme.Text
                };
                item.SubItems.Add(clip.ModifiedAt.ToString("g"));
                item.SubItems.Add(ClipLibrary.FormatSize(clip.SizeBytes));
                item.SubItems.Add("");
                _clips.Items.Add(item);
            }
        }
        finally
        {
            _clips.EndUpdate();
        }

        var totalBytes = clips.Sum(clip => clip.SizeBytes);
        _status.Text = clips.Count == 0
            ? "No clips yet. Minimize to tray, play, then press your clip hotkey."
            : $"{clips.Count} clips loaded - {ClipLibrary.FormatSize(totalBytes)} total.";
    }

    public void SetStatus(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetStatus(message));
            return;
        }

        _status.Text = message;
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(0),
            BackColor = EmxTheme.Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        root.Controls.Add(BuildTitleBar(), 0, 0);

        var header = BuildHeader();
        header.Margin = new Padding(18, 8, 18, 0);
        root.Controls.Add(header, 0, 1);

        var tabs = BuildTabs();
        tabs.Margin = new Padding(18, 0, 18, 0);
        root.Controls.Add(tabs, 0, 2);

        _status.Text = "Ready";
        _status.Dock = DockStyle.Fill;
        _status.ForeColor = EmxTheme.MutedText;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Margin = new Padding(18, 0, 18, 4);
        root.Controls.Add(_status, 0, 3);

        return root;
    }

    private Control BuildTitleBar()
    {
        var bar = new EmxTitleBarPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 0, 8, 0)
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            BackColor = Color.Transparent
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));

        if (_logo is not null)
        {
            grid.Controls.Add(new PictureBox
            {
                Image = _logo,
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 7, 8, 7)
            }, 0, 0);
        }

        var title = new Label
        {
            Text = "EMX Clips",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = EmxTheme.Text,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };
        grid.Controls.Add(title, 1, 0);

        var version = new Label
        {
            Text = $"EMX replay buffer v{AppVersion()}",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = EmxTheme.MutedText,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular)
        };
        grid.Controls.Add(version, 2, 0);

        grid.Controls.Add(ChromeButton("-", () => HideToTrayRequested?.Invoke(this, EventArgs.Empty)), 3, 0);
        grid.Controls.Add(ChromeButton("[ ]", ToggleMaximize), 4, 0);
        grid.Controls.Add(ChromeButton("X", Close, close: true), 5, 0);

        bar.Controls.Add(grid);
        MakeDraggable(bar);
        MakeDraggable(title);
        MakeDraggable(version);
        return bar;
    }

    private Button ChromeButton(string text, Action onClick, bool close = false)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            ForeColor = close ? EmxTheme.MagentaGlow : EmxTheme.Text,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            Margin = new Padding(3, 6, 3, 6),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = EmxTheme.Border;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = close ? EmxTheme.MagentaDark : EmxTheme.Hover;
        button.FlatAppearance.MouseDownBackColor = EmxTheme.Surface;
        button.Click += (_, _) => onClick();
        return button;
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;
    }

    private void MakeDraggable(Control control)
    {
        control.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            ReleaseCapture();
            SendMessage(Handle, WmNcLeftButtonDown, HtCaption, 0);
        };

        control.DoubleClick += (_, _) => ToggleMaximize();
    }

    private Control BuildHeader()
    {
        var header = new EmxHeaderPanel(_logo)
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 14, 18, 14)
        };

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            BackColor = Color.Transparent
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142));

        if (_logo is not null)
        {
            panel.Controls.Add(new PictureBox
            {
                Image = _logo,
                SizeMode = PictureBoxSizeMode.Zoom,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            }, 0, 0);
        }

        var titleStack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(10, 12, 0, 0)
        };
        titleStack.Controls.Add(new Label
        {
            Text = "EMX CLIPS",
            AutoSize = true,
            Font = new Font("Segoe UI", 25, FontStyle.Bold),
            ForeColor = EmxTheme.Text,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        });
        titleStack.Controls.Add(new Label
        {
            Text = "Replay buffer, clip library, MP4 exports, and sharing",
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            ForeColor = EmxTheme.MutedText,
            BackColor = Color.Transparent,
            Margin = new Padding(2, 4, 0, 0)
        });
        panel.Controls.Add(titleStack, 1, 0);

        panel.Controls.Add(Button("Save Clip", ButtonKind.Primary, () => SaveClipRequested?.Invoke(this, EventArgs.Empty)), 2, 0);
        panel.Controls.Add(Button("Restart Buffer", ButtonKind.Green, () => StartReplayBufferRequested?.Invoke(this, EventArgs.Empty)), 3, 0);
        panel.Controls.Add(Button("Pause Buffer", ButtonKind.Magenta, () => StopReplayBufferRequested?.Invoke(this, EventArgs.Empty)), 4, 0);

        header.Controls.Add(panel);
        return header;
    }

    private Control BuildTabs()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = EmxTheme.Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = EmxTheme.Background,
            Padding = new Padding(0, 8, 0, 0)
        };

        _clipsTabButton = NavButton("Clips", () => SetActivePage(showClips: true));
        _settingsTabButton = NavButton("Settings", () => SetActivePage(showClips: false));
        nav.Controls.Add(_clipsTabButton);
        nav.Controls.Add(_settingsTabButton);

        _pageHost.Dock = DockStyle.Fill;
        _pageHost.BackColor = EmxTheme.Panel;
        _pageHost.Padding = new Padding(1);

        _clipsPage = BuildClipsTab();
        _settingsPage = BuildSettingsTab();
        _pageHost.Controls.Add(_settingsPage);
        _pageHost.Controls.Add(_clipsPage);

        root.Controls.Add(nav, 0, 0);
        root.Controls.Add(_pageHost, 0, 1);
        SetActivePage(showClips: true);
        return root;
    }

    private Control BuildClipsTab()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(16),
            BackColor = EmxTheme.Panel
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));

        _clips.Dock = DockStyle.Fill;
        _clips.View = View.Details;
        _clips.FullRowSelect = true;
        _clips.MultiSelect = false;
        _clips.BorderStyle = BorderStyle.None;
        _clips.GridLines = false;
        _clips.BackColor = EmxTheme.Surface;
        _clips.ForeColor = EmxTheme.Text;
        _clips.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _clips.OwnerDraw = true;
        _clips.Columns.Add("Clip", 520);
        _clips.Columns.Add("Saved", 190);
        _clips.Columns.Add("Size", 120);
        _clips.Columns.Add("", 400);
        _clips.DrawColumnHeader += DrawClipHeader;
        _clips.DrawItem += DrawClipItem;
        _clips.DrawSubItem += DrawClipSubItem;
        _clips.DoubleClick += (_, _) => PreviewSelectedClip();
        root.Controls.Add(WrapSurface(_clips), 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = EmxTheme.Panel
        };
        buttons.Controls.Add(Button("Preview", ButtonKind.Green, PreviewSelectedClip));
        buttons.Controls.Add(Button("Play External", ButtonKind.Secondary, PlaySelectedClipExternal));
        buttons.Controls.Add(Button("Export MP4", ButtonKind.Primary, ExportSelectedClipAsMp4));
        buttons.Controls.Add(Button("Copy File", ButtonKind.Green, CopySelectedClipFile));
        buttons.Controls.Add(Button("Phone Share", ButtonKind.Primary, () => PhoneCompanionRequested?.Invoke(this, EventArgs.Empty)));
        buttons.Controls.Add(Button("Export Copy", ButtonKind.Secondary, ExportSelectedClip));
        buttons.Controls.Add(Button("Open Location", ButtonKind.Secondary, OpenSelectedClipLocation));
        buttons.Controls.Add(Button("Delete", ButtonKind.Magenta, DeleteSelectedClip));
        buttons.Controls.Add(Button("Refresh", ButtonKind.Secondary, RefreshClips));
        buttons.Controls.Add(Button("Open Folder", ButtonKind.Secondary, OpenClipsFolder));
        root.Controls.Add(buttons, 0, 1);

        return root;
    }

    private Control BuildSettingsTab()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(16),
            BackColor = EmxTheme.Panel
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 22,
            BackColor = EmxTheme.Panel
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));

        AddRow(grid, 0, "Setup check", BuildObsCheckPanel(), null);
        AddRow(grid, 1, "Clip length seconds", _clipLength, null);
        AddRow(grid, 2, "Save hotkey", BuildSaveHotkeyPanel(), null);
        AddRow(grid, 3, "Show/hide hotkey", BuildToggleHotkeyPanel(), null);
        AddRow(grid, 4, "OBS host", _host, null);
        AddRow(grid, 5, "OBS port", _port, null);
        AddRow(grid, 6, "OBS password", _password, null);
        AddRow(grid, 7, "Capture mic", _captureMic, null);
        AddRow(grid, 8, "Mic device", _microphone, Button("Refresh", ButtonKind.Secondary, LoadMicrophoneDevices));
        AddRow(grid, 9, "Mic test", BuildMicTestPanel(), BuildMicTestButton());
        AddRow(grid, 10, "Buffer memory MB", _memoryLimit, null);
        AddRow(grid, 11, "Clips folder", _clipsFolder, Button("Browse", ButtonKind.Secondary, BrowseClipsFolder));
        AddRow(grid, 12, "OBS path", _obsPath, Button("Browse", ButtonKind.Secondary, BrowseObsPath));

        _useFirebaseCloudShare.Text = "Use Firebase Remote Share instead of local Wi-Fi portal";
        StyleCheckBox(_useFirebaseCloudShare);
        AddRow(grid, 13, "Remote share", _useFirebaseCloudShare, null);
        AddRow(grid, 14, "Firebase API key", _firebaseApiKey, null);
        AddRow(grid, 15, "Realtime DB URL", _firebaseStorageBucket, null);
        AddRow(grid, 16, "Remote session", _firebaseSessionId, Button("New", ButtonKind.Secondary, ResetFirebaseSession));

        _autoLaunch.Text = "Auto-launch OBS";
        _autoStart.Text = "Auto-start replay buffer";
        _minimizeObs.Text = "Launch OBS minimized";
        _dedicatedObsWorkspace.Text = "Streamer safe: use EMX OBS profile and scene collection";
        foreach (var checkBox in new[] { _autoLaunch, _autoStart, _minimizeObs, _dedicatedObsWorkspace, _captureMic, _useFirebaseCloudShare })
        {
            StyleCheckBox(checkBox);
        }

        AddRow(grid, 17, "OBS workspace", _dedicatedObsWorkspace, null);

        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        grid.Controls.Add(Label("Startup"), 0, 18);
        var startup = Stack(_autoLaunch, _autoStart, _minimizeObs);
        grid.Controls.Add(startup, 1, 18);
        grid.SetColumnSpan(startup, 2);

        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = EmxTheme.SurfaceAlt,
            Padding = new Padding(0)
        };
        scroll.Controls.Add(grid);
        root.Controls.Add(WrapSurface(scroll), 0, 0);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true,
            BackColor = EmxTheme.Panel
        };
        bottom.Controls.Add(Button("Save Settings", ButtonKind.Primary, SaveSettingsFromControls));
        bottom.Controls.Add(Button("Auto Setup Capture", ButtonKind.Green, () => AutoSetupCaptureRequested?.Invoke(this, EventArgs.Empty)));
        bottom.Controls.Add(Button("Auto Setup Mic", ButtonKind.Green, () =>
        {
            SaveSettingsFromControls();
            AutoSetupMicRequested?.Invoke(this, EventArgs.Empty);
        }));
        bottom.Controls.Add(Button("Check Updates", ButtonKind.Secondary, () => CheckUpdatesRequested?.Invoke(this, EventArgs.Empty)));
        bottom.Controls.Add(Button("Phone Companion", ButtonKind.Secondary, () => PhoneCompanionRequested?.Invoke(this, EventArgs.Empty)));
        bottom.Controls.Add(Button("Quick Help", ButtonKind.Secondary, ShowQuickHelp));
        bottom.Controls.Add(Button("Refresh Clips", ButtonKind.Secondary, RefreshClips));
        root.Controls.Add(bottom, 0, 1);

        return root;
    }

    private static Panel WrapSurface(Control child)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(1),
            BackColor = EmxTheme.Border
        };
        child.BackColor = child is ListView ? EmxTheme.Surface : EmxTheme.SurfaceAlt;
        panel.Controls.Add(child);
        return panel;
    }

    private Control BuildMicTestPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = EmxTheme.SurfaceAlt
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));

        _micLevel.Dock = DockStyle.Fill;
        _micLevel.Minimum = 0;
        _micLevel.Maximum = 100;
        _micLevel.Value = 0;
        _micLevel.Margin = new Padding(0, 9, 10, 9);
        panel.Controls.Add(_micLevel, 0, 0);

        _micLevelText.Dock = DockStyle.Fill;
        _micLevelText.Text = "Idle";
        _micLevelText.ForeColor = EmxTheme.MutedText;
        _micLevelText.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(_micLevelText, 1, 0);
        return panel;
    }

    private Button BuildMicTestButton()
    {
        _micTestButton = Button("Test Mic", ButtonKind.Green, ToggleMicTest);
        return _micTestButton;
    }

    private static Button NavButton(string text, Action onClick)
    {
        var button = new Button
        {
            Text = text,
            Width = 132,
            Height = 38,
            Margin = new Padding(0, 0, 8, 0),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private void SetActivePage(bool showClips)
    {
        if (_clipsPage is null || _settingsPage is null)
        {
            return;
        }

        _clipsPage.Visible = showClips;
        _settingsPage.Visible = !showClips;
        (showClips ? _clipsPage : _settingsPage).BringToFront();

        if (_clipsTabButton is not null)
        {
            StyleNavButton(_clipsTabButton, showClips);
        }

        if (_settingsTabButton is not null)
        {
            StyleNavButton(_settingsTabButton, !showClips);
        }
    }

    private static void StyleNavButton(Button button, bool active)
    {
        button.BackColor = active ? EmxTheme.SurfaceAlt : EmxTheme.Background;
        button.ForeColor = active ? EmxTheme.Text : EmxTheme.MutedText;
        button.FlatAppearance.BorderColor = active ? EmxTheme.Magenta : EmxTheme.Border;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = EmxTheme.Hover;
        button.FlatAppearance.MouseDownBackColor = EmxTheme.Surface;
    }

    private static void DrawClipHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        using var back = new SolidBrush(EmxTheme.SurfaceAlt);
        using var border = new Pen(EmxTheme.Border);
        e.Graphics.FillRectangle(back, e.Bounds);
        e.Graphics.DrawRectangle(border, e.Bounds);
        TextRenderer.DrawText(
            e.Graphics,
            e.Header?.Text ?? "",
            new Font("Segoe UI", 9.2f, FontStyle.Bold),
            e.Bounds,
            EmxTheme.MutedText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static void DrawClipItem(object? sender, DrawListViewItemEventArgs e)
    {
        e.DrawDefault = false;
    }

    private static void DrawClipSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        var selected = e.Item?.Selected ?? false;
        using var back = new SolidBrush(selected ? EmxTheme.Hover : EmxTheme.Surface);
        using var border = new Pen(Color.FromArgb(25, EmxTheme.Border));
        e.Graphics.FillRectangle(back, e.Bounds);
        e.Graphics.DrawLine(border, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

        var color = selected ? EmxTheme.GreenGlow : EmxTheme.Text;
        var textBounds = e.Bounds;
        textBounds.Inflate(-8, 0);
        TextRenderer.DrawText(
            e.Graphics,
            e.SubItem?.Text ?? "",
            new Font("Segoe UI", 9.2f, FontStyle.Regular),
            textBounds,
            color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static Button Button(string text, ButtonKind kind, Action onClick)
    {
        var button = new Button
        {
            Text = text,
            Width = 128,
            Height = 34,
            Margin = new Padding(6, 10, 6, 4),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };

        var (back, fore, border) = kind switch
        {
            ButtonKind.Primary => (EmxTheme.Green, Color.Black, EmxTheme.GreenGlow),
            ButtonKind.Green => (EmxTheme.GreenDark, EmxTheme.GreenGlow, EmxTheme.Green),
            ButtonKind.Magenta => (EmxTheme.MagentaDark, EmxTheme.MagentaGlow, EmxTheme.Magenta),
            _ => (EmxTheme.SurfaceAlt, EmxTheme.Text, EmxTheme.Border)
        };

        button.BackColor = back;
        button.ForeColor = fore;
        button.FlatAppearance.BorderColor = border;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = kind == ButtonKind.Primary ? EmxTheme.GreenGlow : EmxTheme.Hover;
        button.FlatAppearance.MouseDownBackColor = EmxTheme.Surface;
        button.Click += (_, _) => onClick();
        return button;
    }

    private static Label Label(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = EmxTheme.MutedText,
        BackColor = Color.Transparent,
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
    };

    private static void AddRow(TableLayoutPanel grid, int row, string label, Control control, Control? button)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        grid.Controls.Add(Label(label), 0, row);
        StyleInput(control);
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 5, 10, 5);
        grid.Controls.Add(control, 1, row);

        if (button is not null)
        {
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(0, 5, 0, 5);
            grid.Controls.Add(button, 2, row);
        }
    }

    private static FlowLayoutPanel Stack(params Control[] controls)
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = EmxTheme.SurfaceAlt
        };

        foreach (var control in controls)
        {
            if (control is EmxCheckBox checkBox)
            {
                checkBox.AutoSize = false;
                checkBox.Width = Math.Max(180, TextRenderer.MeasureText(checkBox.Text, checkBox.Font).Width + 42);
                checkBox.Height = 28;
            }
            else
            {
                control.AutoSize = true;
            }

            control.Margin = new Padding(0, 10, 18, 0);
            panel.Controls.Add(control);
        }

        return panel;
    }

    private Control BuildObsCheckPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = EmxTheme.SurfaceAlt
        };

        _obsStatus.AutoSize = false;
        _obsStatus.Width = 360;
        _obsStatus.Height = 32;
        _obsStatus.Margin = new Padding(0, 7, 8, 5);
        _obsStatus.ForeColor = EmxTheme.MutedText;
        _obsStatus.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(_obsStatus);

        var installButton = Button("Install OBS", ButtonKind.Primary, () => InstallObsRequested?.Invoke(this, EventArgs.Empty));
        installButton.Width = 116;
        installButton.Margin = new Padding(0, 5, 0, 5);
        panel.Controls.Add(installButton);
        return panel;
    }

    private Control BuildSaveHotkeyPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = EmxTheme.SurfaceAlt
        };

        _hotkeyDisplay.ReadOnly = true;
        _hotkeyDisplay.Width = 240;
        _hotkeyDisplay.Margin = new Padding(0, 5, 8, 5);
        StyleInput(_hotkeyDisplay);
        panel.Controls.Add(_hotkeyDisplay);

        var bindButton = Button("Bind Hotkey", ButtonKind.Primary, OpenHotkeyBinder);
        bindButton.Width = 132;
        bindButton.Margin = new Padding(0, 5, 8, 5);
        panel.Controls.Add(bindButton);

        var resetButton = Button("Reset", ButtonKind.Secondary, () =>
        {
            _selectedHotkeyModifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt;
            _selectedHotkeyKey = Keys.F8;
            UpdateHotkeyDisplay();
            SetStatus("Hotkey reset to Ctrl+Alt+F8. Click Save Settings to apply.");
        });
        resetButton.Width = 92;
        resetButton.Margin = new Padding(0, 5, 0, 5);
        panel.Controls.Add(resetButton);
        return panel;
    }

    private Control BuildToggleHotkeyPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = EmxTheme.SurfaceAlt
        };

        _toggleHotkeyDisplay.ReadOnly = true;
        _toggleHotkeyDisplay.Width = 240;
        _toggleHotkeyDisplay.Margin = new Padding(0, 5, 8, 5);
        StyleInput(_toggleHotkeyDisplay);
        panel.Controls.Add(_toggleHotkeyDisplay);

        var bindButton = Button("Bind Hotkey", ButtonKind.Primary, OpenToggleHotkeyBinder);
        bindButton.Width = 132;
        bindButton.Margin = new Padding(0, 5, 8, 5);
        panel.Controls.Add(bindButton);

        var resetButton = Button("Reset", ButtonKind.Secondary, () =>
        {
            _selectedToggleHotkeyModifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt;
            _selectedToggleHotkeyKey = Keys.H;
            UpdateToggleHotkeyDisplay();
            SetStatus("Show/hide hotkey reset to Ctrl+Alt+H. Click Save Settings to apply.");
        });
        resetButton.Width = 92;
        resetButton.Margin = new Padding(0, 5, 0, 5);
        panel.Controls.Add(resetButton);
        return panel;
    }

    private void OpenHotkeyBinder()
    {
        using var dialog = new HotkeyCaptureForm(_selectedHotkeyKey, _selectedHotkeyModifiers);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _selectedHotkeyKey = dialog.HotkeyKey;
        _selectedHotkeyModifiers = dialog.HotkeyModifiers;
        UpdateHotkeyDisplay();
        SetStatus($"Hotkey set to {HotkeyText.Format(_selectedHotkeyKey, _selectedHotkeyModifiers)}. Click Save Settings to apply.");
    }

    private void UpdateHotkeyDisplay()
    {
        _hotkeyDisplay.Text = HotkeyText.Format(_selectedHotkeyKey, _selectedHotkeyModifiers);
    }

    private void OpenToggleHotkeyBinder()
    {
        using var dialog = new HotkeyCaptureForm(_selectedToggleHotkeyKey, _selectedToggleHotkeyModifiers);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _selectedToggleHotkeyKey = dialog.HotkeyKey;
        _selectedToggleHotkeyModifiers = dialog.HotkeyModifiers;
        UpdateToggleHotkeyDisplay();
        SetStatus($"Show/hide hotkey set to {HotkeyText.Format(_selectedToggleHotkeyKey, _selectedToggleHotkeyModifiers)}. Click Save Settings to apply.");
    }

    private void UpdateToggleHotkeyDisplay()
    {
        _toggleHotkeyDisplay.Text = HotkeyText.Format(_selectedToggleHotkeyKey, _selectedToggleHotkeyModifiers);
    }

    private void RefreshObsCheck()
    {
        var obsPath = ObsTools.ResolveObsPath(_obsPath.Text);
        if (obsPath is null)
        {
            _obsStatus.Text = "OBS not found";
            _obsStatus.ForeColor = EmxTheme.MagentaGlow;
            return;
        }

        _obsStatus.Text = "OBS available";
        _obsStatus.ForeColor = EmxTheme.GreenGlow;
    }

    private static void StyleInput(Control control)
    {
        switch (control)
        {
            case TextBox textBox:
                textBox.BackColor = EmxTheme.Surface;
                textBox.ForeColor = EmxTheme.Text;
                textBox.BorderStyle = BorderStyle.FixedSingle;
                break;
            case NumericUpDown numeric:
                numeric.BackColor = EmxTheme.Surface;
                numeric.ForeColor = EmxTheme.Text;
                numeric.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ComboBox comboBox:
                comboBox.BackColor = EmxTheme.Surface;
                comboBox.ForeColor = EmxTheme.Text;
                comboBox.FlatStyle = FlatStyle.Flat;
                comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
                break;
            case Panel or FlowLayoutPanel:
                control.BackColor = EmxTheme.SurfaceAlt;
                break;
        }
    }

    private static void StyleCheckBox(CheckBox checkBox)
    {
        checkBox.ForeColor = EmxTheme.Text;
        checkBox.BackColor = Color.Transparent;
        checkBox.FlatStyle = FlatStyle.Flat;
        checkBox.AutoSize = false;
        checkBox.Height = 28;
        checkBox.FlatAppearance.BorderColor = EmxTheme.Magenta;
        checkBox.FlatAppearance.CheckedBackColor = EmxTheme.MagentaDark;
    }

    private void LoadSettingsIntoControls()
    {
        _clipLength.Minimum = 5;
        _clipLength.Maximum = 1800;
        _clipLength.Value = Math.Clamp(_settings.ReplayBufferSeconds, 5, 1800);

        _memoryLimit.Minimum = 512;
        _memoryLimit.Maximum = 65536;
        _memoryLimit.Increment = 512;
        _memoryLimit.Value = Math.Clamp(_settings.ReplayBufferMemoryMb, 512, 65536);

        _clipsFolder.Text = _settings.ClipsFolder;
        _obsPath.Text = _settings.ObsPath;
        _host.Text = _settings.ObsWebSocketHost;
        _host.PlaceholderText = "127.0.0.1";
        _port.Minimum = 1;
        _port.Maximum = 65535;
        _port.Value = Math.Clamp(_settings.ObsWebSocketPort, 1, 65535);
        _password.Text = _settings.ObsWebSocketPassword;
        _password.UseSystemPasswordChar = true;
        _password.PlaceholderText = "Password from OBS WebSocket settings";
        _captureMic.Text = "Include selected microphone in clips";
        _captureMic.Checked = _settings.CaptureMicrophone;
        _captureMic.CheckedChanged += (_, _) =>
        {
            RefreshMicControlsState();
            if (IsHandleCreated)
            {
                SetStatus(_captureMic.Checked
                    ? "Mic on. Click Save Settings or Auto Setup Mic so OBS uses it in new clips."
                    : "Mic off. Click Save Settings to mute EMX mic capture in OBS.");
            }
        };
        LoadMicrophoneDevices();
        _autoLaunch.Checked = _settings.AutoLaunchObs;
        _autoStart.Checked = _settings.AutoStartReplayBuffer;
        _minimizeObs.Checked = _settings.MinimizeObsToTray;
        _dedicatedObsWorkspace.Checked = _settings.UseDedicatedObsWorkspace;
        _useFirebaseCloudShare.Checked = _settings.UseFirebaseCloudShare || FirebaseRemoteShare.IsConfigured(_settings);
        _firebaseApiKey.Text = _settings.FirebaseApiKey;
        _firebaseApiKey.PlaceholderText = "Firebase Web API key";
        _firebaseStorageBucket.Text = _settings.FirebaseDatabaseUrl;
        _firebaseStorageBucket.PlaceholderText = "https://your-project-default-rtdb.firebaseio.com";
        _firebaseSessionId.Text = _settings.FirebaseSessionId;
        _firebaseSessionId.PlaceholderText = "Auto-generated share room";

        _selectedHotkeyKey = _settings.HotkeyKey;
        _selectedHotkeyModifiers = _settings.HotkeyModifiers;
        _selectedToggleHotkeyKey = _settings.ToggleDashboardHotkeyKey;
        _selectedToggleHotkeyModifiers = _settings.ToggleDashboardHotkeyModifiers;
        UpdateHotkeyDisplay();
        UpdateToggleHotkeyDisplay();
        RefreshObsCheck();
        RefreshMicControlsState();
    }

    private void SaveSettingsFromControls()
    {
        _settings.ReplayBufferSeconds = (int)_clipLength.Value;
        _settings.ReplayBufferMemoryMb = (int)_memoryLimit.Value;
        _settings.ClipsFolder = _clipsFolder.Text.Trim();
        _settings.ObsPath = _obsPath.Text.Trim();
        _settings.ObsWebSocketHost = string.IsNullOrWhiteSpace(_host.Text) ? "127.0.0.1" : _host.Text.Trim();
        _settings.ObsWebSocketPort = (int)_port.Value;
        _settings.ObsWebSocketPassword = _password.Text;
        _settings.AutoLaunchObs = _autoLaunch.Checked;
        _settings.AutoStartReplayBuffer = _autoStart.Checked;
        _settings.MinimizeObsToTray = _minimizeObs.Checked;
        _settings.UseDedicatedObsWorkspace = _dedicatedObsWorkspace.Checked;
        _settings.CaptureMicrophone = _captureMic.Checked;
        _settings.UseFirebaseCloudShare = _useFirebaseCloudShare.Checked;
        _settings.FirebaseApiKey = _firebaseApiKey.Text.Trim();
        _settings.FirebaseDatabaseUrl = _firebaseStorageBucket.Text.Trim();
        _settings.FirebaseSessionId = string.IsNullOrWhiteSpace(_firebaseSessionId.Text)
            ? Guid.NewGuid().ToString("N")
            : _firebaseSessionId.Text.Trim();
        if (_microphone.SelectedItem is AudioInputDevice device)
        {
            _settings.MicrophoneDeviceId = device.Id;
            _settings.MicrophoneDeviceName = device.Name;
        }
        _settings.SetupCompleted = true;
        _settings.HotkeyKey = _selectedHotkeyKey;
        _settings.HotkeyModifiers = _selectedHotkeyModifiers;
        _settings.ToggleDashboardHotkeyKey = _selectedToggleHotkeyKey;
        _settings.ToggleDashboardHotkeyModifiers = _selectedToggleHotkeyModifiers;

        _settings.Save();
        RefreshClips();
        RefreshObsCheck();
        RefreshMicControlsState();
        SettingsSaved?.Invoke(this, EventArgs.Empty);
        SetStatus("Settings saved");
    }

    private void ResetFirebaseSession()
    {
        _firebaseSessionId.Text = Guid.NewGuid().ToString("N");
        SetStatus("New Firebase remote session created. Save settings before sharing.");
    }

    private void LoadMicrophoneDevices()
    {
        IReadOnlyList<AudioInputDevice> devices;
        try
        {
            devices = AudioDeviceService.ListCaptureDevices();
        }
        catch (Exception ex)
        {
            SetStatus($"Could not read microphones: {ex.Message}");
            return;
        }

        var selectedId = _microphone.SelectedItem is AudioInputDevice current
            ? current.Id
            : _settings.MicrophoneDeviceId;

        _microphone.BeginUpdate();
        _loadingMicrophones = true;
        try
        {
            _microphone.Items.Clear();
            foreach (var device in devices)
            {
                _microphone.Items.Add(device);
            }

            var selected = devices.FirstOrDefault(device =>
                string.Equals(device.Id, selectedId, StringComparison.OrdinalIgnoreCase)) ?? devices.FirstOrDefault();

            if (selected is not null)
            {
                _microphone.SelectedItem = selected;
            }
        }
        finally
        {
            _loadingMicrophones = false;
            _microphone.EndUpdate();
        }
    }

    private void ToggleMicTest()
    {
        if (_micTestRunning)
        {
            _micTestTimer.Stop();
            _micTestRunning = false;
            _micLevel.Value = 0;
            _micLevelText.Text = _captureMic.Checked ? "Ready" : "Mic Off";
            if (_micTestButton is not null) _micTestButton.Text = "Test Mic";
            return;
        }

        _micTestRunning = true;
        if (_micTestButton is not null) _micTestButton.Text = "Stop Test";
        _micTestTimer.Start();
        UpdateMicTestMeter();
    }

    private void UpdateMicTestMeter()
    {
        if (!_captureMic.Checked)
        {
            _micLevel.Value = 0;
            _micLevelText.Text = "Mic Off";
            return;
        }

        try
        {
            var deviceId = _microphone.SelectedItem is AudioInputDevice device ? device.Id : "default";
            var peak = AudioDeviceService.GetPeakLevel(deviceId);
            var value = Math.Clamp((int)Math.Round(peak * 100), 0, 100);
            _micLevel.Value = value;
            _micLevelText.Text = value switch
            {
                >= 65 => "Loud",
                >= 20 => "Good",
                >= 4 => "Signal",
                _ => "Quiet"
            };
            _micLevelText.ForeColor = value >= 4 ? EmxTheme.GreenGlow : EmxTheme.MutedText;
        }
        catch (Exception ex)
        {
            _micTestTimer.Stop();
            _micTestRunning = false;
            _micLevel.Value = 0;
            _micLevelText.Text = "Test failed";
            if (_micTestButton is not null) _micTestButton.Text = "Test Mic";
            SetStatus($"Mic test failed: {ex.Message}");
        }
    }

    private void RefreshMicControlsState()
    {
        var enabled = _captureMic.Checked;
        _microphone.Enabled = enabled;
        _micLevel.Enabled = enabled;
        if (_micTestButton is not null)
        {
            _micTestButton.Enabled = enabled;
        }

        _micLevelText.Text = enabled ? "Ready" : "Mic Off";
        if (!enabled && _micTestRunning)
        {
            ToggleMicTest();
        }
    }

    private ClipFile? SelectedClip()
    {
        if (_clips.SelectedItems.Count == 0)
        {
            SetStatus("Select a clip first");
            return null;
        }

        return _clips.SelectedItems[0].Tag as ClipFile;
    }

    private void PreviewSelectedClip()
    {
        var clip = SelectedClip();
        if (clip is null) return;

        using var preview = new PreviewForm(clip, _logo, Icon);
        preview.ShowDialog(this);
    }

    private void OpenSelectedClipLocation()
    {
        var clip = SelectedClip();
        if (clip is null) return;

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{clip.FullPath}\"",
            UseShellExecute = true
        });
    }

    private void PlaySelectedClipExternal()
    {
        var clip = SelectedClip();
        if (clip is null) return;

        Process.Start(new ProcessStartInfo
        {
            FileName = clip.FullPath,
            UseShellExecute = true
        });
    }

    private async void ExportSelectedClipAsMp4()
    {
        var clip = SelectedClip();
        if (clip is null) return;

        using var dialog = new SaveFileDialog
        {
            FileName = Path.GetFileName(ClipExporter.SuggestedMp4Path(clip)),
            Filter = "MP4 video|*.mp4",
            Title = "Export MP4 for CapCut/TikTok"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            SetStatus("Exporting MP4...");
            await ClipExporter.ExportMp4Async(clip, dialog.FileName, _settings.ObsPath);
            SetStatus($"MP4 ready: {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
            MessageBox.Show(this, ex.Message, "EMX Clips", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void CopySelectedClipFile()
    {
        var clip = SelectedClip();
        if (clip is null) return;

        var files = new StringCollection { clip.FullPath };
        Clipboard.SetFileDropList(files);
        SetStatus("Clip copied. Paste it into Discord, folders, or an editor import window.");
    }

    private void ExportSelectedClip()
    {
        var clip = SelectedClip();
        if (clip is null) return;

        using var dialog = new SaveFileDialog
        {
            FileName = clip.Name,
            Filter = $"Video file|*{Path.GetExtension(clip.Name)}|All files|*.*",
            Title = "Export EMX clip"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        File.Copy(clip.FullPath, dialog.FileName, true);
        SetStatus($"Exported {Path.GetFileName(dialog.FileName)}");
    }

    private void DeleteSelectedClip()
    {
        var clip = SelectedClip();
        if (clip is null) return;

        var result = MessageBox.Show(this, $"Delete {clip.Name}?", "EMX Clips", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes)
        {
            return;
        }

        FileSystem.DeleteFile(clip.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        RefreshClips();
        SetStatus("Clip moved to Recycle Bin");
    }

    private void OpenClipsFolder()
    {
        Directory.CreateDirectory(_settings.ClipsFolder);
        Process.Start(new ProcessStartInfo { FileName = _settings.ClipsFolder, UseShellExecute = true });
    }

    private void BrowseClipsFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select where EMX Clips should save clips",
            SelectedPath = Directory.Exists(_clipsFolder.Text) ? _clipsFolder.Text : _settings.ClipsFolder
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _clipsFolder.Text = dialog.SelectedPath;
        }
    }

    private void BrowseObsPath()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "OBS executable|obs64.exe|Executable files|*.exe|All files|*.*",
            Title = "Select obs64.exe"
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _obsPath.Text = dialog.FileName;
            RefreshObsCheck();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(EmxTheme.Magenta, 1);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized)
        {
            HideToTrayRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNcHitTest && WindowState == FormWindowState.Normal)
        {
            base.WndProc(ref m);
            if (m.Result.ToInt32() != HtClient)
            {
                return;
            }

            var screenPoint = new Point(
                unchecked((short)(long)m.LParam),
                unchecked((short)((long)m.LParam >> 16)));
            var point = PointToClient(screenPoint);

            var left = point.X <= ResizeBorder;
            var right = point.X >= ClientSize.Width - ResizeBorder;
            var top = point.Y <= ResizeBorder;
            var bottom = point.Y >= ClientSize.Height - ResizeBorder;

            if (left && top) m.Result = HtTopLeft;
            else if (right && top) m.Result = HtTopRight;
            else if (left && bottom) m.Result = HtBottomLeft;
            else if (right && bottom) m.Result = HtBottomRight;
            else if (left) m.Result = HtLeft;
            else if (right) m.Result = HtRight;
            else if (top) m.Result = HtTop;
            else if (bottom) m.Result = HtBottom;

            return;
        }

        base.WndProc(ref m);
    }

    private static Image? LoadLogoImage()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("EmxClips.Assets.emx-logo.png");
        if (stream is not null)
        {
            return Image.FromStream(stream);
        }

        var localPath = Path.Combine(AppContext.BaseDirectory, "Assets", "emx-logo.png");
        return File.Exists(localPath) ? Image.FromFile(localPath) : null;
    }

    private static string AppVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private void ShowQuickHelp()
    {
        var help = $"""
EMX Clips quick start

1. OBS available should be green.
2. Click Auto Setup Capture once.
3. Pick your mic, turn Capture Mic on or off, and use Test Mic to check signal.
4. Click Auto Setup Mic if you want EMX to apply it to OBS now.
5. Save Settings.
6. Minimize EMX Clips to tray and play.
7. Press {HotkeyText.Format(_selectedHotkeyKey, _selectedHotkeyModifiers)} to save the last {_clipLength.Value:0} seconds.

Use Export MP4 for CapCut, TikTok, Discord, and most editors.
""";

        MessageBox.Show(this, help, "EMX Clips Help", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Icon?.Dispose();
            _logo?.Dispose();
            _micTestTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hWnd, int msg, int wParam, int lParam);
}

internal enum ButtonKind
{
    Primary,
    Green,
    Magenta,
    Secondary
}

internal sealed class NoWheelNumericUpDown : NumericUpDown
{
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (e is HandledMouseEventArgs handled)
        {
            handled.Handled = true;
        }
    }
}

internal sealed class EmxCheckBox : CheckBox
{
    public EmxCheckBox()
    {
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        Appearance = Appearance.Button;
        TextAlign = ContentAlignment.MiddleLeft;
        UseVisualStyleBackColor = false;
        Font = new Font("Segoe UI", 9.2f, FontStyle.Bold);
        Height = 28;
    }

    protected override void OnCheckedChanged(EventArgs e)
    {
        base.OnCheckedChanged(e);
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var parentBack = Parent?.BackColor ?? EmxTheme.SurfaceAlt;
        e.Graphics.Clear(parentBack);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var box = new Rectangle(4, Math.Max(4, (Height - 18) / 2), 18, 18);
        var borderColor = Enabled
            ? (Checked ? EmxTheme.Green : EmxTheme.Magenta)
            : Color.FromArgb(70, EmxTheme.MutedText);
        var fillColor = Checked
            ? (Enabled ? EmxTheme.Green : Color.FromArgb(80, EmxTheme.Green))
            : EmxTheme.Surface;

        using (var fill = new SolidBrush(fillColor))
        using (var border = new Pen(borderColor, 1.4f))
        {
            e.Graphics.FillRectangle(fill, box);
            e.Graphics.DrawRectangle(border, box);
        }

        if (Checked)
        {
            using var check = new Pen(Color.Black, 2.2f)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round
            };
            e.Graphics.DrawLines(check, new[]
            {
                new Point(box.Left + 4, box.Top + 9),
                new Point(box.Left + 8, box.Top + 13),
                new Point(box.Right - 4, box.Top + 5)
            });
        }

        var textBounds = new Rectangle(30, 0, Width - 32, Height);
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            textBounds,
            Enabled ? EmxTheme.Text : EmxTheme.MutedText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (Focused)
        {
            using var focus = new Pen(Color.FromArgb(120, EmxTheme.GreenGlow), 1);
            var focusBounds = ClientRectangle;
            focusBounds.Inflate(-1, -1);
            e.Graphics.DrawRectangle(focus, focusBounds);
        }
    }
}

internal static class HotkeyText
{
    private static readonly KeysConverter Converter = new();

    public static string Format(Keys key, HotkeyModifiers modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(KeyName(key));
        return string.Join("+", parts);
    }

    private static string KeyName(Keys key)
    {
        if (key >= Keys.D0 && key <= Keys.D9)
        {
            return ((int)key - (int)Keys.D0).ToString();
        }

        return Converter.ConvertToString(key) ?? key.ToString();
    }
}

internal sealed class HotkeyCaptureForm : Form
{
    private readonly Label _preview = new();

    public HotkeyCaptureForm(Keys currentKey, HotkeyModifiers currentModifiers)
    {
        HotkeyKey = currentKey;
        HotkeyModifiers = currentModifiers;

        Text = "Bind EMX Hotkey";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 220);
        MinimumSize = ClientSize;
        MaximumSize = ClientSize;
        BackColor = EmxTheme.Background;
        ForeColor = EmxTheme.Text;
        KeyPreview = true;

        Controls.Add(BuildContent());
        UpdatePreview("Press your clip hotkey now");
    }

    public Keys HotkeyKey { get; private set; }
    public HotkeyModifiers HotkeyModifiers { get; private set; }

    private Control BuildContent()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(22),
            BackColor = EmxTheme.Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        root.Controls.Add(new Label
        {
            Text = "SmartBind Hotkey",
            Dock = DockStyle.Fill,
            ForeColor = EmxTheme.Green,
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        root.Controls.Add(new Label
        {
            Text = "Press the combo you want, like Ctrl+Shift+F8 or Alt+F9. Keyboard hotkeys only.",
            Dock = DockStyle.Fill,
            ForeColor = EmxTheme.MutedText,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);

        _preview.Dock = DockStyle.Fill;
        _preview.ForeColor = EmxTheme.Text;
        _preview.BackColor = EmxTheme.SurfaceAlt;
        _preview.BorderStyle = BorderStyle.FixedSingle;
        _preview.Font = new Font("Segoe UI", 13f, FontStyle.Bold);
        _preview.TextAlign = ContentAlignment.MiddleCenter;
        root.Controls.Add(_preview, 0, 2);

        root.Controls.Add(new Label
        {
            Text = "Esc cancels. Single-key binds work, but modifier combos are safer while gaming.",
            Dock = DockStyle.Fill,
            ForeColor = EmxTheme.MutedText,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 3);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = EmxTheme.Background
        };

        var cancel = new Button
        {
            Text = "Cancel",
            Width = 104,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
            BackColor = EmxTheme.SurfaceAlt,
            ForeColor = EmxTheme.Text,
            DialogResult = DialogResult.Cancel
        };
        cancel.FlatAppearance.BorderColor = EmxTheme.Border;
        cancel.Click += (_, _) => Close();
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 4);

        return root;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        if (IsModifierKey(e.KeyCode))
        {
            UpdatePreview("Now press a non-modifier key");
            return;
        }

        HotkeyModifiers = CaptureModifiers(e);
        HotkeyKey = e.KeyCode;
        UpdatePreview(HotkeyText.Format(HotkeyKey, HotkeyModifiers));
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(EmxTheme.Magenta, 1);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }

    private void UpdatePreview(string text)
    {
        _preview.Text = text;
    }

    private static HotkeyModifiers CaptureModifiers(KeyEventArgs e)
    {
        var modifiers = HotkeyModifiers.None;
        if (e.Control) modifiers |= HotkeyModifiers.Control;
        if (e.Alt) modifiers |= HotkeyModifiers.Alt;
        if (e.Shift) modifiers |= HotkeyModifiers.Shift;

        if ((e.KeyData & Keys.LWin) == Keys.LWin || (e.KeyData & Keys.RWin) == Keys.RWin)
        {
            modifiers |= HotkeyModifiers.Win;
        }

        return modifiers;
    }

    private static bool IsModifierKey(Keys key) =>
        key is Keys.ControlKey
            or Keys.LControlKey
            or Keys.RControlKey
            or Keys.ShiftKey
            or Keys.LShiftKey
            or Keys.RShiftKey
            or Keys.Menu
            or Keys.LMenu
            or Keys.RMenu
            or Keys.LWin
            or Keys.RWin;
}

internal static class EmxTheme
{
    public static readonly Color Background = Color.FromArgb(2, 6, 7);
    public static readonly Color Panel = Color.FromArgb(7, 13, 14);
    public static readonly Color Surface = Color.FromArgb(4, 8, 10);
    public static readonly Color SurfaceAlt = Color.FromArgb(12, 19, 21);
    public static readonly Color Hover = Color.FromArgb(24, 39, 28);
    public static readonly Color Border = Color.FromArgb(34, 86, 46);
    public static readonly Color Text = Color.FromArgb(246, 250, 246);
    public static readonly Color MutedText = Color.FromArgb(150, 166, 156);
    public static readonly Color Green = Color.FromArgb(108, 255, 0);
    public static readonly Color GreenDark = Color.FromArgb(17, 55, 24);
    public static readonly Color GreenGlow = Color.FromArgb(185, 255, 120);
    public static readonly Color Magenta = Color.FromArgb(227, 0, 255);
    public static readonly Color MagentaDark = Color.FromArgb(54, 12, 60);
    public static readonly Color MagentaGlow = Color.FromArgb(255, 112, 255);
}

internal sealed class EmxHeaderPanel : Panel
{
    private readonly Image? _logo;

    public EmxHeaderPanel(Image? logo)
    {
        _logo = logo;
        DoubleBuffered = true;
        BackColor = EmxTheme.Panel;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var background = new System.Drawing.Drawing2D.LinearGradientBrush(
            ClientRectangle,
            Color.FromArgb(16, 32, 18),
            Color.FromArgb(34, 9, 40),
            25f);
        e.Graphics.FillRectangle(background, ClientRectangle);

        using var gridPen = new Pen(Color.FromArgb(28, EmxTheme.Green), 1);
        for (var x = 0; x < Width; x += 32)
        {
            e.Graphics.DrawLine(gridPen, x, 0, x, Height);
        }

        for (var y = 0; y < Height; y += 24)
        {
            e.Graphics.DrawLine(gridPen, 0, y, Width, y);
        }

        if (_logo is not null)
        {
            using var attributes = new System.Drawing.Imaging.ImageAttributes();
            var matrix = new System.Drawing.Imaging.ColorMatrix
            {
                Matrix33 = 0.16f
            };
            attributes.SetColorMatrix(matrix);

            var logoBounds = new Rectangle(Width - 260, -78, 330, 330);
            e.Graphics.DrawImage(_logo, logoBounds, 0, 0, _logo.Width, _logo.Height, GraphicsUnit.Pixel, attributes);
        }

        using var greenPen = new Pen(EmxTheme.Green, 2);
        using var magentaPen = new Pen(EmxTheme.Magenta, 2);
        e.Graphics.DrawLine(greenPen, 18, Height - 9, Width / 2, Height - 9);
        e.Graphics.DrawLine(magentaPen, Width / 2, Height - 9, Width - 18, Height - 9);
        base.OnPaint(e);
    }
}

internal sealed class EmxTitleBarPanel : Panel
{
    public EmxTitleBarPanel()
    {
        DoubleBuffered = true;
        BackColor = EmxTheme.Background;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var background = new System.Drawing.Drawing2D.LinearGradientBrush(
            ClientRectangle,
            Color.FromArgb(5, 13, 12),
            Color.FromArgb(30, 8, 33),
            0f);
        e.Graphics.FillRectangle(background, ClientRectangle);

        using var glassLine = new Pen(Color.FromArgb(22, Color.White), 1);
        e.Graphics.DrawLine(glassLine, 0, 1, Width, 1);

        using var green = new Pen(Color.FromArgb(90, EmxTheme.Green), 1);
        using var magenta = new Pen(Color.FromArgb(115, EmxTheme.Magenta), 1);
        e.Graphics.DrawLine(green, 0, Height - 2, Width / 2, Height - 2);
        e.Graphics.DrawLine(magenta, Width / 2, Height - 2, Width, Height - 2);

        base.OnPaint(e);
    }
}

internal sealed class EmxTabControl : TabControl
{
    public EmxTabControl()
    {
        DrawMode = TabDrawMode.OwnerDrawFixed;
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(132, 38);
        BackColor = EmxTheme.Panel;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var selected = SelectedIndex == e.Index;
        var bounds = e.Bounds;
        bounds.Inflate(-2, -3);

        using var back = new SolidBrush(selected ? EmxTheme.SurfaceAlt : EmxTheme.Background);
        using var border = new Pen(selected ? EmxTheme.Magenta : EmxTheme.Border);
        using var textBrush = new SolidBrush(selected ? EmxTheme.Text : EmxTheme.MutedText);

        e.Graphics.FillRectangle(back, bounds);
        e.Graphics.DrawRectangle(border, bounds);

        var text = TabPages[e.Index].Text;
        TextRenderer.DrawText(
            e.Graphics,
            text,
            new Font("Segoe UI", 9.5f, FontStyle.Bold),
            bounds,
            selected ? EmxTheme.Text : EmxTheme.MutedText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
