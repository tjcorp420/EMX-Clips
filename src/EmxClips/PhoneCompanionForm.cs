using System.Diagnostics;
using QRCoder;

namespace EmxClips;

public sealed class PhoneCompanionForm : Form
{
    private readonly string _companionUrl;
    private readonly string _localPortalUrl;
    private readonly PictureBox _qr = new();
    private readonly TextBox _urlBox = new();

    public PhoneCompanionForm(string companionUrl, string localPortalUrl, Icon icon)
    {
        _companionUrl = companionUrl;
        _localPortalUrl = localPortalUrl;
        Icon = (Icon)icon.Clone();
        Text = "EMX Phone Companion";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 680);
        BackColor = EmxTheme.Background;
        ForeColor = EmxTheme.Text;
        Font = new Font("Segoe UI", 9.5f);

        Controls.Add(BuildLayout());
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(20),
            BackColor = EmxTheme.Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 330));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));

        var header = new Label
        {
            Dock = DockStyle.Fill,
            Text = "PHONE COMPANION\nScan this Vercel link on your phone. It opens the EMX page first, then connects to this PC.",
            ForeColor = EmxTheme.Text,
            BackColor = EmxTheme.Background,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(header, 0, 0);

        _qr.Dock = DockStyle.Fill;
        _qr.SizeMode = PictureBoxSizeMode.Zoom;
        _qr.BackColor = Color.White;
        _qr.Image = CreateQrImage(_companionUrl);
        root.Controls.Add(Wrap(_qr, Color.White), 0, 1);

        _urlBox.Dock = DockStyle.Fill;
        _urlBox.ReadOnly = true;
        _urlBox.Text = _companionUrl;
        _urlBox.BackColor = EmxTheme.Surface;
        _urlBox.ForeColor = EmxTheme.GreenGlow;
        _urlBox.BorderStyle = BorderStyle.FixedSingle;
        _urlBox.Font = new Font("Consolas", 11f, FontStyle.Bold);
        root.Controls.Add(_urlBox, 0, 2);

        var notes = new Label
        {
            Dock = DockStyle.Fill,
            Text = $"The QR opens the hosted EMX Companion, then its Open PC Clip Portal button uses this PC link: {_localPortalUrl}\n\nYour phone can preview clips, open videos, download files, and use the phone share sheet. On iPhone, tap Open Video, then Share, then Save Video to put it in Photos. If Windows Firewall asks, allow EMX Clips on Private networks.",
            ForeColor = EmxTheme.MutedText,
            BackColor = EmxTheme.Background,
            Font = new Font("Segoe UI", 10f),
            TextAlign = ContentAlignment.TopLeft
        };
        root.Controls.Add(notes, 0, 3);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = EmxTheme.Background
        };
        buttons.Controls.Add(MakeButton("Close", Close));
        buttons.Controls.Add(MakeButton("Open", () => Process.Start(new ProcessStartInfo { FileName = _companionUrl, UseShellExecute = true })));
        buttons.Controls.Add(MakeButton("Copy Link", () =>
        {
            Clipboard.SetText(_companionUrl);
            MessageBox.Show(this, "Hosted phone companion link copied.", "EMX Clips", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }));
        root.Controls.Add(buttons, 0, 4);

        return root;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Icon?.Dispose();
            _qr.Image?.Dispose();
        }

        base.Dispose(disposing);
    }

    private static Panel Wrap(Control child, Color backColor)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            BackColor = backColor
        };
        panel.Controls.Add(child);
        return panel;
    }

    private static Button MakeButton(string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            Width = 118,
            Height = 38,
            Margin = new Padding(8, 10, 0, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = text == "Copy Link" ? EmxTheme.Green : EmxTheme.SurfaceAlt,
            ForeColor = text == "Copy Link" ? Color.Black : EmxTheme.Text,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };
        button.FlatAppearance.BorderColor = text == "Copy Link" ? EmxTheme.GreenGlow : EmxTheme.Border;
        button.Click += (_, _) => action();
        return button;
    }

    private static Image CreateQrImage(string value)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(value, QRCodeGenerator.ECCLevel.Q);
        var qr = new PngByteQRCode(data);
        var bytes = qr.GetGraphic(12);
        using var stream = new MemoryStream(bytes);
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }
}
