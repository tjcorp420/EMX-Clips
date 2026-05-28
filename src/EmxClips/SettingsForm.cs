namespace EmxClips;

public sealed class SettingsForm : Form
{
    private readonly TextBox _obsPath = new();
    private readonly TextBox _host = new();
    private readonly NumericUpDown _port = new();
    private readonly TextBox _password = new();
    private readonly TextBox _clipsFolder = new();
    private readonly CheckBox _autoLaunch = new();
    private readonly CheckBox _autoStart = new();
    private readonly CheckBox _minimizeObs = new();
    private readonly ComboBox _hotkey = new();
    private readonly CheckBox _ctrl = new();
    private readonly CheckBox _alt = new();
    private readonly CheckBox _shift = new();
    private readonly CheckBox _win = new();

    public SettingsForm(AppSettings settings)
    {
        Settings = settings;

        Text = "EMX Clips Settings";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(620, 410);

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 10,
            Padding = new Padding(14),
            AutoSize = false
        };

        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

        AddRow(table, 0, "OBS path", _obsPath, BrowseObsButton());
        AddRow(table, 1, "OBS host", _host, null);
        AddRow(table, 2, "OBS port", _port, null);
        AddRow(table, 3, "OBS password", _password, null);
        AddRow(table, 4, "Clips folder", _clipsFolder, BrowseFolderButton());

        _autoLaunch.Text = "Auto-launch OBS";
        _autoStart.Text = "Auto-start replay buffer";
        _minimizeObs.Text = "Launch OBS minimized";

        table.Controls.Add(new Label(), 0, 5);
        var startupOptions = Stack(_autoLaunch, _autoStart, _minimizeObs);
        table.Controls.Add(startupOptions, 1, 5);
        table.SetColumnSpan(startupOptions, 2);

        table.Controls.Add(Label("Save hotkey"), 0, 6);
        var hotkeyPanel = BuildHotkeyPanel();
        table.Controls.Add(hotkeyPanel, 1, 6);
        table.SetColumnSpan(hotkeyPanel, 2);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill
        };

        var save = new Button { Text = "Save", DialogResult = DialogResult.OK, Width = 90 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
        save.Click += (_, _) => SaveToSettings();
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);

        table.Controls.Add(buttons, 0, 9);
        table.SetColumnSpan(buttons, 3);

        Controls.Add(table);
        AcceptButton = save;
        CancelButton = cancel;

        LoadFromSettings(settings);
    }

    public AppSettings Settings { get; }

    private static void AddRow(TableLayoutPanel table, int row, string label, Control control, Control? button)
    {
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        table.Controls.Add(Label(label), 0, row);
        control.Dock = DockStyle.Fill;
        table.Controls.Add(control, 1, row);

        if (button is not null)
        {
            button.Dock = DockStyle.Fill;
            table.Controls.Add(button, 2, row);
        }
    }

    private static Label Label(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static FlowLayoutPanel Stack(params Control[] controls)
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        foreach (var control in controls)
        {
            control.AutoSize = true;
            control.Margin = new Padding(0, 4, 18, 0);
            panel.Controls.Add(control);
        }

        return panel;
    }

    private Button BrowseObsButton()
    {
        var button = new Button { Text = "Browse" };
        button.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "OBS executable|obs64.exe|Executable files|*.exe|All files|*.*",
                Title = "Select obs64.exe"
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _obsPath.Text = dialog.FileName;
            }
        };

        return button;
    }

    private Button BrowseFolderButton()
    {
        var button = new Button { Text = "Browse" };
        button.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select where EMX clips should be saved"
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _clipsFolder.Text = dialog.SelectedPath;
            }
        };

        return button;
    }

    private Control BuildHotkeyPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        _hotkey.DropDownStyle = ComboBoxStyle.DropDownList;
        _hotkey.Width = 100;

        foreach (var key in new[] { Keys.F6, Keys.F7, Keys.F8, Keys.F9, Keys.F10, Keys.F11, Keys.F12, Keys.Insert, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown })
        {
            _hotkey.Items.Add(key);
        }

        _ctrl.Text = "Ctrl";
        _alt.Text = "Alt";
        _shift.Text = "Shift";
        _win.Text = "Win";

        foreach (var control in new Control[] { _ctrl, _alt, _shift, _win, _hotkey })
        {
            control.Margin = new Padding(0, 4, 14, 0);
            panel.Controls.Add(control);
        }

        return panel;
    }

    private void LoadFromSettings(AppSettings settings)
    {
        _obsPath.Text = settings.ObsPath;
        _host.Text = settings.ObsWebSocketHost;
        _port.Minimum = 1;
        _port.Maximum = 65535;
        _port.Value = Math.Clamp(settings.ObsWebSocketPort, 1, 65535);
        _password.Text = settings.ObsWebSocketPassword;
        _password.UseSystemPasswordChar = true;
        _clipsFolder.Text = settings.ClipsFolder;
        _autoLaunch.Checked = settings.AutoLaunchObs;
        _autoStart.Checked = settings.AutoStartReplayBuffer;
        _minimizeObs.Checked = settings.MinimizeObsToTray;
        _ctrl.Checked = settings.HotkeyModifiers.HasFlag(HotkeyModifiers.Control);
        _alt.Checked = settings.HotkeyModifiers.HasFlag(HotkeyModifiers.Alt);
        _shift.Checked = settings.HotkeyModifiers.HasFlag(HotkeyModifiers.Shift);
        _win.Checked = settings.HotkeyModifiers.HasFlag(HotkeyModifiers.Win);
        _hotkey.SelectedItem = settings.HotkeyKey;

        if (_hotkey.SelectedIndex < 0)
        {
            _hotkey.SelectedItem = Keys.F8;
        }
    }

    private void SaveToSettings()
    {
        Settings.ObsPath = _obsPath.Text.Trim();
        Settings.ObsWebSocketHost = string.IsNullOrWhiteSpace(_host.Text) ? "127.0.0.1" : _host.Text.Trim();
        Settings.ObsWebSocketPort = (int)_port.Value;
        Settings.ObsWebSocketPassword = _password.Text;
        Settings.ClipsFolder = _clipsFolder.Text.Trim();
        Settings.AutoLaunchObs = _autoLaunch.Checked;
        Settings.AutoStartReplayBuffer = _autoStart.Checked;
        Settings.MinimizeObsToTray = _minimizeObs.Checked;
        Settings.HotkeyKey = _hotkey.SelectedItem is Keys key ? key : Keys.F8;

        var modifiers = HotkeyModifiers.None;
        if (_ctrl.Checked) modifiers |= HotkeyModifiers.Control;
        if (_alt.Checked) modifiers |= HotkeyModifiers.Alt;
        if (_shift.Checked) modifiers |= HotkeyModifiers.Shift;
        if (_win.Checked) modifiers |= HotkeyModifiers.Win;
        Settings.HotkeyModifiers = modifiers;

        Settings.Save();
    }
}
