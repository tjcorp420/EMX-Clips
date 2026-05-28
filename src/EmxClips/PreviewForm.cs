using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms.Integration;
using WpfMediaElement = System.Windows.Controls.MediaElement;
using WpfMediaState = System.Windows.Controls.MediaState;
using WpfStretch = System.Windows.Media.Stretch;

namespace EmxClips;

public sealed class PreviewForm : Form
{
    private readonly ClipFile _clip;
    private readonly Image? _logo;
    private readonly WpfMediaElement _player = new();
    private readonly Label _status = new();
    private readonly Label _time = new();
    private readonly TrackBar _seek = new();
    private readonly System.Windows.Forms.Timer _positionTimer = new() { Interval = 250 };
    private TimeSpan _duration = TimeSpan.Zero;
    private bool _isPlaying;
    private bool _isSeeking;

    public PreviewForm(ClipFile clip, Image? logo, Icon? icon)
    {
        _clip = clip;
        _logo = logo;

        Text = $"Preview - {clip.Name}";
        if (icon is not null)
        {
            Icon = (Icon)icon.Clone();
        }

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 520);
        ClientSize = new Size(900, 600);
        BackColor = EmxTheme.Background;
        ForeColor = EmxTheme.Text;
        DoubleBuffered = true;

        Controls.Add(BuildLayout());
        _positionTimer.Tick += (_, _) => UpdateSeekFromPlayer();
        Load += (_, _) => StartPreview();
        FormClosed += (_, _) =>
        {
            _positionTimer.Stop();
            _player.Stop();
            _player.Source = null;
        };
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = EmxTheme.Background,
            Padding = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 102));

        root.Controls.Add(BuildTitleBar(), 0, 0);
        root.Controls.Add(BuildClipHeader(), 0, 1);
        root.Controls.Add(BuildPlayer(), 0, 2);
        root.Controls.Add(BuildControls(), 0, 3);
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
            ColumnCount = 4,
            BackColor = Color.Transparent
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
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
            Text = "EMX Clip Preview",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = EmxTheme.Text,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };
        grid.Controls.Add(title, 1, 0);

        grid.Controls.Add(ChromeButton("-", () => WindowState = FormWindowState.Minimized), 2, 0);
        grid.Controls.Add(ChromeButton("X", Close, close: true), 3, 0);

        bar.Controls.Add(grid);
        MakeDraggable(bar);
        MakeDraggable(title);
        return bar;
    }

    private Control BuildClipHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = EmxTheme.Panel,
            Padding = new Padding(18, 10, 18, 8),
            Margin = new Padding(16, 12, 16, 0)
        };

        var title = new Label
        {
            Text = _clip.Name,
            Dock = DockStyle.Fill,
            ForeColor = EmxTheme.Green,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        panel.Controls.Add(title);
        return panel;
    }

    private Control BuildPlayer()
    {
        var border = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = EmxTheme.Border,
            Padding = new Padding(1),
            Margin = new Padding(16, 12, 16, 0)
        };

        _player.LoadedBehavior = WpfMediaState.Manual;
        _player.UnloadedBehavior = WpfMediaState.Manual;
        _player.Stretch = WpfStretch.Uniform;
        _player.MediaOpened += (_, _) =>
        {
            _duration = _player.NaturalDuration.HasTimeSpan
                ? _player.NaturalDuration.TimeSpan
                : TimeSpan.Zero;
            _seek.Enabled = _duration > TimeSpan.Zero;
            _seek.Maximum = Math.Max(1, (int)Math.Min(int.MaxValue, _duration.TotalMilliseconds));
            _seek.Value = 0;
            _positionTimer.Start();
            _status.Text = "Preview playing";
            _isPlaying = true;
        };
        _player.MediaEnded += (_, _) =>
        {
            _player.Position = TimeSpan.Zero;
            _player.Play();
        };
        _player.MediaFailed += (_, e) =>
        {
            _status.Text = $"Preview failed: {e.ErrorException.Message}";
        };

        var host = new ElementHost
        {
            Dock = DockStyle.Fill,
            BackColor = EmxTheme.Surface,
            Child = _player
        };
        border.Controls.Add(host);
        return border;
    }

    private Control BuildControls()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = EmxTheme.Background,
            Padding = new Padding(16, 4, 16, 8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var seekRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = EmxTheme.Background
        };
        seekRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        seekRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));

        _seek.Dock = DockStyle.Fill;
        _seek.Enabled = false;
        _seek.TickStyle = TickStyle.None;
        _seek.Minimum = 0;
        _seek.Maximum = 1;
        _seek.SmallChange = 1000;
        _seek.LargeChange = 5000;
        _seek.BackColor = EmxTheme.Background;
        _seek.MouseDown += (_, _) => _isSeeking = true;
        _seek.MouseUp += (_, _) =>
        {
            SeekToSliderPosition();
            _isSeeking = false;
        };
        _seek.KeyUp += (_, _) => SeekToSliderPosition();
        _seek.Scroll += (_, _) =>
        {
            if (_isSeeking)
            {
                _time.Text = FormatTime(TimeSpan.FromMilliseconds(_seek.Value), _duration);
            }
        };
        seekRow.Controls.Add(_seek, 0, 0);

        _time.Dock = DockStyle.Fill;
        _time.ForeColor = EmxTheme.MutedText;
        _time.TextAlign = ContentAlignment.MiddleRight;
        _time.Text = "00:00 / 00:00";
        seekRow.Controls.Add(_time, 1, 0);
        root.Controls.Add(seekRow, 0, 0);

        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = EmxTheme.Background,
            Padding = new Padding(0, 4, 0, 0)
        };

        panel.Controls.Add(Button("Play/Pause", ButtonKind.Green, TogglePlayback));
        panel.Controls.Add(Button("Restart", ButtonKind.Secondary, Restart));
        panel.Controls.Add(Button("Open Location", ButtonKind.Secondary, OpenLocation));
        panel.Controls.Add(Button("Open External", ButtonKind.Secondary, OpenExternal));

        _status.Text = "Loading preview";
        _status.AutoSize = false;
        _status.Width = 360;
        _status.Height = 34;
        _status.Margin = new Padding(12, 10, 0, 0);
        _status.ForeColor = EmxTheme.MutedText;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(_status);

        root.Controls.Add(panel, 0, 1);
        return root;
    }

    private void StartPreview()
    {
        try
        {
            _player.Source = new Uri(_clip.FullPath);
            _player.Play();
            _isPlaying = true;
        }
        catch (Exception ex)
        {
            _status.Text = $"Preview failed: {ex.Message}";
        }
    }

    private void TogglePlayback()
    {
        if (_isPlaying)
        {
            _player.Pause();
            _status.Text = "Paused";
            _isPlaying = false;
            return;
        }

        _player.Play();
        _status.Text = "Preview playing";
        _isPlaying = true;
    }

    private void Restart()
    {
        _player.Position = TimeSpan.Zero;
        _player.Play();
        _isPlaying = true;
        _status.Text = "Restarted";
        UpdateSeekFromPlayer();
    }

    private void SeekToSliderPosition()
    {
        if (!_seek.Enabled)
        {
            return;
        }

        var position = TimeSpan.FromMilliseconds(_seek.Value);
        _player.Position = position;
        _time.Text = FormatTime(position, _duration);
    }

    private void UpdateSeekFromPlayer()
    {
        if (_isSeeking || !_seek.Enabled)
        {
            return;
        }

        var position = _player.Position;
        var value = (int)Math.Clamp(position.TotalMilliseconds, _seek.Minimum, _seek.Maximum);
        _seek.Value = value;
        _time.Text = FormatTime(position, _duration);
    }

    private static string FormatTime(TimeSpan position, TimeSpan duration) =>
        $"{FormatSingleTime(position)} / {FormatSingleTime(duration)}";

    private static string FormatSingleTime(TimeSpan value) =>
        value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss")
            : value.ToString(@"mm\:ss");

    private void OpenLocation()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{_clip.FullPath}\"",
            UseShellExecute = true
        });
    }

    private void OpenExternal()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = _clip.FullPath,
            UseShellExecute = true
        });
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

    private static Button Button(string text, ButtonKind kind, Action onClick)
    {
        var button = new Button
        {
            Text = text,
            Width = 128,
            Height = 34,
            Margin = new Padding(6, 4, 6, 4),
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

    private void MakeDraggable(Control control)
    {
        control.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            ReleaseCapture();
            SendMessage(Handle, 0x00A1, 2, 0);
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(EmxTheme.Magenta, 1);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _positionTimer.Dispose();
            Icon?.Dispose();
        }

        base.Dispose(disposing);
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hWnd, int msg, int wParam, int lParam);
}
