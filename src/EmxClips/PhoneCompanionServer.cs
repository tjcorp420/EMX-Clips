using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace EmxClips;

public sealed class PhoneCompanionServer : IDisposable
{
    private const int DefaultPort = 4788;
    private const int MaxHeaderBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly AppSettings _settings;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;

    public PhoneCompanionServer(AppSettings settings)
    {
        _settings = settings;
    }

    public string LocalUrl { get; private set; } = "";
    public int Port { get; private set; }
    public bool IsRunning => _listener is not null;

    public string Start()
    {
        if (_listener is not null)
        {
            return LocalUrl;
        }

        for (var port = DefaultPort; port < DefaultPort + 30; port++)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
                Port = port;
                LocalUrl = $"http://{GetBestLanAddress()}:{port}/";
                _cts = new CancellationTokenSource();
                _serverTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
                return LocalUrl;
            }
            catch (SocketException)
            {
                _listener = null;
            }
        }

        throw new InvalidOperationException("EMX could not start the phone companion server. Ports 4788-4817 are already in use.");
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _listener = null;
        _cts?.Dispose();
        _cts = null;
        _serverTask = null;
    }

    public void Dispose() => Stop();

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), CancellationToken.None);
            }
            catch when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            client.ReceiveTimeout = 15000;
            client.SendTimeout = 30000;
            await using var stream = client.GetStream();

            var headerText = await ReadHeaderAsync(stream, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(headerText))
            {
                return;
            }

            var request = ParseRequest(headerText);
            if (request is null)
            {
                await SendTextAsync(stream, 400, "Bad Request", "Bad request", "text/plain; charset=utf-8", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (string.Equals(request.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                await WriteHeadersAsync(stream, 204, "No Content", new Dictionary<string, string>(), 0, cancellationToken).ConfigureAwait(false);
                return;
            }

            try
            {
                await RouteAsync(stream, request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await SendJsonAsync(stream, 500, "Internal Server Error", new
                {
                    error = ex.Message
                }, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task RouteAsync(NetworkStream stream, CompanionRequest request, CancellationToken cancellationToken)
    {
        var path = request.Path.TrimEnd('/');
        if (path.Length == 0)
        {
            path = "/";
        }

        switch (path)
        {
            case "/":
            case "/index.html":
                await SendTextAsync(stream, 200, "OK", CompanionPageHtml, "text/html; charset=utf-8", cancellationToken).ConfigureAwait(false);
                return;
            case "/styles.css":
                await SendTextAsync(stream, 200, "OK", CompanionCss, "text/css; charset=utf-8", cancellationToken).ConfigureAwait(false);
                return;
            case "/app.js":
                await SendTextAsync(stream, 200, "OK", CompanionJavaScript, "text/javascript; charset=utf-8", cancellationToken).ConfigureAwait(false);
                return;
            case "/manifest.webmanifest":
                await SendTextAsync(stream, 200, "OK", CompanionManifest, "application/manifest+json; charset=utf-8", cancellationToken).ConfigureAwait(false);
                return;
            case "/sw.js":
                await SendTextAsync(stream, 200, "OK", CompanionServiceWorker, "text/javascript; charset=utf-8", cancellationToken).ConfigureAwait(false);
                return;
            case "/icons/icon.svg":
                await SendTextAsync(stream, 200, "OK", CompanionIconSvg, "image/svg+xml", cancellationToken).ConfigureAwait(false);
                return;
            case "/api/status":
                await SendStatusAsync(stream, cancellationToken).ConfigureAwait(false);
                return;
            case "/api/clips":
                await SendClipListAsync(stream, cancellationToken).ConfigureAwait(false);
                return;
        }

        if (path.StartsWith("/clips/", StringComparison.OrdinalIgnoreCase))
        {
            await SendClipFileAsync(stream, request, path["/clips/".Length..], inline: true, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (path.StartsWith("/download/", StringComparison.OrdinalIgnoreCase))
        {
            await SendClipFileAsync(stream, request, path["/download/".Length..], inline: false, cancellationToken).ConfigureAwait(false);
            return;
        }

        await SendTextAsync(stream, 404, "Not Found", "Not found", "text/plain; charset=utf-8", cancellationToken).ConfigureAwait(false);
    }

    private async Task SendStatusAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var clips = ClipLibrary.Load(_settings.ClipsFolder);
        await SendJsonAsync(stream, 200, "OK", new
        {
            app = "EMX Clips Companion",
            pcName = Environment.MachineName,
            clipsFolder = _settings.ClipsFolder,
            count = clips.Count,
            url = LocalUrl,
            refreshedAt = DateTimeOffset.Now
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendClipListAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var clips = ClipLibrary.Load(_settings.ClipsFolder)
            .Take(80)
            .Select(clip =>
            {
                var id = EncodeId(clip.Name);
                var isMp4 = string.Equals(Path.GetExtension(clip.Name), ".mp4", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetExtension(clip.Name), ".m4v", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetExtension(clip.Name), ".mov", StringComparison.OrdinalIgnoreCase);
                return new
                {
                    id,
                    clip.Name,
                    clip.SizeBytes,
                    size = ClipLibrary.FormatSize(clip.SizeBytes),
                    modifiedAt = clip.ModifiedAt,
                    extension = Path.GetExtension(clip.Name).TrimStart('.').ToUpperInvariant(),
                    isPhoneFriendly = isMp4,
                    streamUrl = $"/clips/{id}",
                    downloadUrl = $"/download/{id}"
                };
            })
            .ToList();

        await SendJsonAsync(stream, 200, "OK", new
        {
            clips,
            count = clips.Count
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendClipFileAsync(NetworkStream stream, CompanionRequest request, string id, bool inline, CancellationToken cancellationToken)
    {
        var path = ResolveClipPath(id);
        if (path is null)
        {
            await SendTextAsync(stream, 404, "Not Found", "Clip not found", "text/plain; charset=utf-8", cancellationToken).ConfigureAwait(false);
            return;
        }

        var info = new FileInfo(path);
        var fileLength = info.Length;
        var start = 0L;
        var end = fileLength - 1;
        var status = 200;
        var reason = "OK";

        if (request.Headers.TryGetValue("Range", out var rangeHeader) &&
            TryParseRange(rangeHeader, fileLength, out var rangeStart, out var rangeEnd))
        {
            start = rangeStart;
            end = rangeEnd;
            status = 206;
            reason = "Partial Content";
        }

        var contentLength = Math.Max(0, end - start + 1);
        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = GetContentType(path),
            ["Accept-Ranges"] = "bytes",
            ["Content-Disposition"] = BuildContentDisposition(info.Name, inline)
        };

        if (status == 206)
        {
            headers["Content-Range"] = $"bytes {start}-{end}/{fileLength}";
        }

        await WriteHeadersAsync(stream, status, reason, headers, contentLength, cancellationToken).ConfigureAwait(false);
        if (string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        file.Position = start;
        var remaining = contentLength;
        var buffer = new byte[128 * 1024];

        while (remaining > 0)
        {
            var read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            remaining -= read;
        }
    }

    private string? ResolveClipPath(string id)
    {
        var fileName = DecodeId(id);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        fileName = Path.GetFileName(fileName);
        var clipsRoot = Path.GetFullPath(_settings.ClipsFolder);
        var path = Path.GetFullPath(Path.Combine(clipsRoot, fileName));
        if (!path.StartsWith(clipsRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(path) ||
            !ClipLibrary.IsVideoFile(path))
        {
            return null;
        }

        return path;
    }

    private static async Task<string> ReadHeaderAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>(4096);
        var buffer = new byte[1024];

        while (bytes.Count < MaxHeaderBytes)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            for (var i = 0; i < read; i++)
            {
                bytes.Add(buffer[i]);
            }

            if (bytes.Count >= 4 &&
                bytes[^4] == '\r' &&
                bytes[^3] == '\n' &&
                bytes[^2] == '\r' &&
                bytes[^1] == '\n')
            {
                break;
            }
        }

        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private static CompanionRequest? ParseRequest(string headerText)
    {
        var lines = headerText.Split(["\r\n"], StringSplitOptions.None);
        var requestParts = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (requestParts.Length < 2)
        {
            return null;
        }

        var path = requestParts[1];
        var queryIndex = path.IndexOf('?');
        if (queryIndex >= 0)
        {
            path = path[..queryIndex];
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                break;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return new CompanionRequest(requestParts[0], Uri.UnescapeDataString(path), headers);
    }

    private static async Task SendJsonAsync(NetworkStream stream, int status, string reason, object body, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        await SendTextAsync(stream, status, reason, json, "application/json; charset=utf-8", cancellationToken).ConfigureAwait(false);
    }

    private static async Task SendTextAsync(NetworkStream stream, int status, string reason, string body, string contentType, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        await WriteHeadersAsync(stream, status, reason, new Dictionary<string, string>
        {
            ["Content-Type"] = contentType
        }, bytes.Length, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteHeadersAsync(NetworkStream stream, int status, string reason, Dictionary<string, string> headers, long contentLength, CancellationToken cancellationToken)
    {
        headers["Content-Length"] = contentLength.ToString();
        headers["Connection"] = "close";
        headers["Access-Control-Allow-Origin"] = "*";
        headers["Access-Control-Allow-Headers"] = "Range, Content-Type";
        headers["Access-Control-Allow-Methods"] = "GET, HEAD, OPTIONS";
        headers["Cache-Control"] = "no-store";
        headers["Server"] = "EMX-Clips-Companion";

        var builder = new StringBuilder();
        builder.Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n");
        foreach (var (key, value) in headers)
        {
            builder.Append(key).Append(": ").Append(value).Append("\r\n");
        }

        builder.Append("\r\n");
        var bytes = Encoding.ASCII.GetBytes(builder.ToString());
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static bool TryParseRange(string value, long fileLength, out long start, out long end)
    {
        start = 0;
        end = fileLength - 1;

        if (!value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var range = value["bytes=".Length..].Split('-', 2);
        if (range.Length != 2)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(range[0]) && !long.TryParse(range[0], out start))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(range[1]) && !long.TryParse(range[1], out end))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(range[0]))
        {
            var suffixLength = end;
            start = Math.Max(0, fileLength - suffixLength);
            end = fileLength - 1;
        }

        start = Math.Clamp(start, 0, Math.Max(0, fileLength - 1));
        end = Math.Clamp(end, start, Math.Max(0, fileLength - 1));
        return fileLength > 0;
    }

    private static string GetContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp4" => "video/mp4",
            ".m4v" => "video/mp4",
            ".mov" => "video/quicktime",
            ".webm" => "video/webm",
            ".mkv" => "video/x-matroska",
            ".flv" => "video/x-flv",
            ".ts" => "video/mp2t",
            _ => "application/octet-stream"
        };

    private static string BuildContentDisposition(string fileName, bool inline)
    {
        var safeName = fileName.Replace("\"", "'");
        var encoded = Uri.EscapeDataString(fileName);
        return $"{(inline ? "inline" : "attachment")}; filename=\"{safeName}\"; filename*=UTF-8''{encoded}";
    }

    private static string EncodeId(string value)
    {
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        return base64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string DecodeId(string value)
    {
        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch
        {
            return "";
        }
    }

    private static string GetBestLanAddress()
    {
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (var address in networkInterface.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address.Address) &&
                    !address.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                {
                    return address.Address.ToString();
                }
            }
        }

        return "127.0.0.1";
    }

    private sealed record CompanionRequest(string Method, string Path, Dictionary<string, string> Headers);

    private const string CompanionManifest = """
{
  "name": "EMX Clips Phone Companion",
  "short_name": "EMX Clips",
  "start_url": "/",
  "scope": "/",
  "display": "standalone",
  "orientation": "portrait",
  "background_color": "#020607",
  "theme_color": "#72ff00",
  "icons": [
    {
      "src": "/icons/icon.svg",
      "sizes": "any",
      "type": "image/svg+xml",
      "purpose": "any maskable"
    }
  ]
}
""";

    private const string CompanionServiceWorker = """
const CACHE_NAME = "emx-phone-companion-v1";
self.addEventListener("install", event => self.skipWaiting());
self.addEventListener("activate", event => self.clients.claim());
""";

    private const string CompanionIconSvg = """
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">
  <defs><linearGradient id="g" x1="0" x2="1" y1="0" y2="1"><stop stop-color="#72ff00"/><stop offset=".55" stop-color="#fff"/><stop offset="1" stop-color="#ec19ff"/></linearGradient></defs>
  <rect width="512" height="512" rx="96" fill="#020607"/>
  <rect x="62" y="84" width="388" height="344" fill="#081011" stroke="#72ff00" stroke-width="10"/>
  <path d="M128 152h119l-19 44h-67l-8 22h62l-19 44h-62l-10 27h82l-21 47H55l73-184zm129 0h70l24 86 99-86h68l-72 184h-71l34-88-76 62h-32l-22-64-35 90h-60l73-184z" fill="url(#g)"/>
  <path d="M370 152h73l-78 82 19 102h-72l-8-50-47 50h-72l98-102-17-82h70l8 39 26-39z" fill="#ec19ff"/>
</svg>
""";

    private const string CompanionPageHtml = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
  <meta name="theme-color" content="#72ff00">
  <meta name="apple-mobile-web-app-capable" content="yes">
  <meta name="apple-mobile-web-app-title" content="EMX Clips">
  <meta name="apple-mobile-web-app-status-bar-style" content="black-translucent">
  <title>EMX Clips Phone Companion</title>
  <link rel="manifest" href="/manifest.webmanifest">
  <link rel="icon" href="/icons/icon.svg" type="image/svg+xml">
  <link rel="stylesheet" href="/styles.css">
</head>
<body>
  <main class="shell">
    <section class="hero">
      <img class="mark" src="/icons/icon.svg" alt="EMX Clips">
      <div>
        <p class="eyebrow">EMX phone companion</p>
        <h1>Your PC clips, on your phone.</h1>
        <p class="copy" id="statusText">Connected to EMX Clips on your PC.</p>
      </div>
    </section>

    <section class="toolbar">
      <button class="button primary" id="refreshButton" type="button">Refresh Clips</button>
      <button class="button" id="installButton" type="button">Install</button>
    </section>

    <section class="notice">
      <strong>iPhone Photos:</strong> tap Open Video, then use the iOS share button and choose Save Video. Web apps cannot silently write into Photos.
    </section>

    <section class="clips" id="clips"></section>
  </main>
  <script src="/app.js" type="module"></script>
</body>
</html>
""";

    private const string CompanionCss = """
:root {
  color-scheme: dark;
  --bg: #020607;
  --panel: rgba(6, 12, 13, .86);
  --green: #72ff00;
  --green-soft: #c5ff86;
  --magenta: #ec19ff;
  --text: #f7fff7;
  --muted: #aec4b5;
  --line: rgba(114, 255, 0, .45);
  --line-magenta: rgba(236, 25, 255, .48);
}
* { box-sizing: border-box; }
body {
  margin: 0;
  min-height: 100svh;
  color: var(--text);
  font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
  background:
    radial-gradient(circle at 18% 8%, rgba(114, 255, 0, .25), transparent 26rem),
    radial-gradient(circle at 90% 20%, rgba(236, 25, 255, .24), transparent 28rem),
    linear-gradient(135deg, #020607, #06100c 48%, #17031a);
}
body::before {
  content: "";
  position: fixed;
  inset: 0;
  pointer-events: none;
  background:
    linear-gradient(rgba(114, 255, 0, .09) 1px, transparent 1px),
    linear-gradient(90deg, rgba(236, 25, 255, .08) 1px, transparent 1px);
  background-size: 38px 38px;
  mask-image: linear-gradient(to bottom, #000, transparent);
}
.shell {
  position: relative;
  width: min(100%, 1040px);
  margin: 0 auto;
  padding: max(18px, env(safe-area-inset-top)) 14px 28px;
}
.hero, .clip, .notice {
  border: 1px solid var(--line);
  background: linear-gradient(145deg, rgba(6, 12, 13, .92), rgba(15, 6, 20, .75));
  box-shadow: 0 22px 64px rgba(0, 0, 0, .42), inset 0 0 32px rgba(255,255,255,.035);
  backdrop-filter: blur(18px);
}
.hero {
  display: grid;
  grid-template-columns: 82px 1fr;
  align-items: center;
  gap: 16px;
  min-height: 28svh;
  padding: 22px;
  clip-path: polygon(0 0, 94% 0, 100% 12%, 100% 100%, 6% 100%, 0 88%);
}
.mark { width: 82px; height: 82px; filter: drop-shadow(0 0 18px rgba(114,255,0,.55)); }
.eyebrow {
  margin: 0 0 8px;
  color: var(--green);
  font-size: .74rem;
  font-weight: 900;
  letter-spacing: .12em;
  text-transform: uppercase;
}
h1 { margin: 0 0 10px; font-size: clamp(2.1rem, 10vw, 4.8rem); line-height: .92; letter-spacing: 0; text-transform: uppercase; }
h2 { margin: 0; font-size: 1.04rem; letter-spacing: 0; overflow-wrap: anywhere; }
.copy, .meta, .notice { color: var(--muted); line-height: 1.45; }
.toolbar { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; margin: 14px 0; }
.button {
  min-height: 48px;
  border: 1px solid var(--line-magenta);
  background: rgba(8, 17, 18, .88);
  color: var(--text);
  display: inline-grid;
  place-items: center;
  padding: 0 16px;
  text-decoration: none;
  font-weight: 900;
  cursor: pointer;
  clip-path: polygon(8% 0, 100% 0, 92% 100%, 0 100%);
}
.button.primary { border-color: var(--green); background: var(--green); color: #040604; box-shadow: 0 0 24px rgba(114,255,0,.32); }
.button:disabled { opacity: .55; cursor: default; }
.notice { margin: 14px 0; padding: 14px 16px; border-color: var(--line-magenta); }
.clips { display: grid; gap: 16px; }
.clip { padding: 12px; }
.clip-head { display: flex; justify-content: space-between; gap: 12px; align-items: start; margin-bottom: 10px; }
.badge { color: var(--green-soft); border: 1px solid var(--line); padding: 6px 8px; font-size: .72rem; font-weight: 900; text-transform: uppercase; }
.meta { font-size: .88rem; margin-top: 4px; }
video {
  width: 100%;
  aspect-ratio: 16 / 9;
  background: #000;
  border: 1px solid rgba(114,255,0,.3);
}
.clip-actions { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; margin-top: 10px; }
.empty { margin-top: 18px; padding: 28px; text-align: center; color: var(--muted); border: 1px dashed var(--line); }
@media (min-width: 860px) {
  .clips { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}
""";

    private const string CompanionJavaScript = """
const clipsEl = document.querySelector("#clips");
const refreshButton = document.querySelector("#refreshButton");
const installButton = document.querySelector("#installButton");
const statusText = document.querySelector("#statusText");
let installPrompt = null;

if ("serviceWorker" in navigator && location.protocol === "https:") {
  navigator.serviceWorker.register("/sw.js").catch(() => {});
}

window.addEventListener("beforeinstallprompt", event => {
  event.preventDefault();
  installPrompt = event;
  installButton.textContent = "Install App";
});

installButton.addEventListener("click", async () => {
  if (installPrompt) {
    installPrompt.prompt();
    await installPrompt.userChoice.catch(() => null);
    installPrompt = null;
    return;
  }

  const ios = /iphone|ipad|ipod/i.test(navigator.userAgent);
  alert(ios
    ? "On iPhone: open this page in Safari, tap Share, then Add to Home Screen."
    : "Use your browser menu and choose Install app or Add to Home screen.");
});

refreshButton.addEventListener("click", loadClips);
setInterval(loadClips, 10000);
loadClips();

async function loadClips() {
  refreshButton.disabled = true;
  try {
    const [status, data] = await Promise.all([
      fetch("/api/status").then(response => response.json()),
      fetch("/api/clips").then(response => response.json())
    ]);
    statusText.textContent = `${status.count} clips from ${status.pcName}`;
    renderClips(data.clips || []);
  } catch (error) {
    clipsEl.innerHTML = `<div class="empty">Could not load clips. Keep EMX Clips open on your PC and stay on the same Wi-Fi.</div>`;
  } finally {
    refreshButton.disabled = false;
  }
}

function renderClips(clips) {
  if (!clips.length) {
    clipsEl.innerHTML = `<div class="empty">No clips found yet. Save a clip on PC, then refresh.</div>`;
    return;
  }

  clipsEl.innerHTML = "";
  for (const clip of clips) {
    const card = document.createElement("article");
    card.className = "clip";
    const streamUrl = new URL(clip.streamUrl, location.href).href;
    const downloadUrl = new URL(clip.downloadUrl, location.href).href;
    card.innerHTML = `
      <div class="clip-head">
        <div>
          <h2>${escapeHtml(clip.name)}</h2>
          <div class="meta">${clip.size} • ${new Date(clip.modifiedAt).toLocaleString()}</div>
        </div>
        <span class="badge">${clip.extension}</span>
      </div>
      <video controls playsinline preload="metadata" src="${streamUrl}"></video>
      ${clip.isPhoneFriendly ? "" : `<p class="meta">For iPhone playback, export this clip as MP4 in EMX Clips first.</p>`}
      <div class="clip-actions">
        <a class="button primary" href="${streamUrl}" target="_blank" rel="noreferrer">Open Video</a>
        <a class="button" href="${downloadUrl}" download>Download</a>
        <button class="button" type="button" data-share="${clip.id}">Share / Save</button>
        <button class="button" type="button" data-copy="${clip.id}">Copy Link</button>
      </div>
    `;
    clipsEl.append(card);
    card.querySelector("[data-share]")?.addEventListener("click", () => shareClip(clip, streamUrl));
    card.querySelector("[data-copy]")?.addEventListener("click", event => copyLink(event.currentTarget, streamUrl));
  }
}

async function shareClip(clip, streamUrl) {
  try {
    if (navigator.canShare && navigator.share && window.isSecureContext && clip.isPhoneFriendly) {
      const response = await fetch(streamUrl);
      const blob = await response.blob();
      const file = new File([blob], clip.name, { type: blob.type || "video/mp4" });
      if (navigator.canShare({ files: [file] })) {
        await navigator.share({ files: [file], title: clip.name, text: "EMX Clips" });
        return;
      }
    }

    if (navigator.share) {
      await navigator.share({ title: clip.name, text: "EMX Clips", url: streamUrl });
      return;
    }
  } catch {
  }

  window.open(streamUrl, "_blank", "noopener,noreferrer");
}

async function copyLink(button, streamUrl) {
  await navigator.clipboard?.writeText(streamUrl).catch(() => null);
  const old = button.textContent;
  button.textContent = "Copied";
  setTimeout(() => button.textContent = old, 1600);
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}
""";
}
