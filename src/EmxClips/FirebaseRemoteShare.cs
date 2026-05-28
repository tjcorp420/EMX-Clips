using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace EmxClips;

public sealed record FirebaseRemoteShareResult(string CompanionUrl, string TunnelUrl, int ClipCount);

public sealed class FirebaseRemoteShare : IDisposable
{
    private const string CompanionUrl = "https://emx-clips-companion.vercel.app/";
    private const string CloudflaredDownloadUrl = "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe";

    private static readonly Regex TryCloudflareUrlPattern = new(@"https://[a-z0-9-]+\.trycloudflare\.com", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly AppSettings _settings;
    private readonly PhoneCompanionServer _server;
    private Process? _cloudflared;
    private string _tunnelUrl = "";

    public FirebaseRemoteShare(AppSettings settings, PhoneCompanionServer server)
    {
        _settings = settings;
        _server = server;
    }

    public static bool IsConfigured(AppSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.FirebaseApiKey) &&
        !string.IsNullOrWhiteSpace(settings.FirebaseDatabaseUrl);

    public async Task<FirebaseRemoteShareResult> StartAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured(_settings))
        {
            throw new InvalidOperationException("Firebase Remote Share needs a Firebase API key and Realtime Database URL in Settings.");
        }

        if (string.IsNullOrWhiteSpace(_settings.FirebaseSessionId))
        {
            _settings.FirebaseSessionId = Guid.NewGuid().ToString("N");
            _settings.Save();
        }

        var localUrl = _server.Start();
        var tunnelUrl = await EnsureTunnelAsync(cancellationToken).ConfigureAwait(false);
        var auth = await SignInAnonymouslyAsync(_settings.FirebaseApiKey, cancellationToken).ConfigureAwait(false);
        var clipCount = ClipLibrary.Load(_settings.ClipsFolder).Count;
        try
        {
            await PublishSessionAsync(auth, tunnelUrl, localUrl, clipCount, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (IsFirebasePermissionError(ex))
        {
            _settings.FirebaseSessionId = Guid.NewGuid().ToString("N");
            _settings.Save();
            await PublishSessionAsync(auth, tunnelUrl, localUrl, clipCount, cancellationToken).ConfigureAwait(false);
        }

        return new FirebaseRemoteShareResult(BuildCompanionUrl(), tunnelUrl, clipCount);
    }

    private async Task<string> EnsureTunnelAsync(CancellationToken cancellationToken)
    {
        if (_cloudflared is not null && !_cloudflared.HasExited && !string.IsNullOrWhiteSpace(_tunnelUrl))
        {
            return _tunnelUrl;
        }

        DisposeTunnel();
        var cloudflaredPath = await EnsureCloudflaredAsync(cancellationToken).ConfigureAwait(false);
        var localUrl = $"http://127.0.0.1:{_server.Port}";
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = cloudflaredPath,
                Arguments = $"tunnel --url {localUrl} --no-autoupdate",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            },
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, args) => TryCaptureTunnelUrl(args.Data, ready);
        process.ErrorDataReceived += (_, args) => TryCaptureTunnelUrl(args.Data, ready);
        process.Exited += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_tunnelUrl))
            {
                ready.TrySetException(new InvalidOperationException("Cloudflare tunnel closed before it returned a public URL."));
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start Cloudflare tunnel.");
        }

        _cloudflared = process;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        await using var _ = linked.Token.Register(() => ready.TrySetException(new TimeoutException("Cloudflare tunnel did not become ready in time.")));
        _tunnelUrl = (await ready.Task.ConfigureAwait(false)).TrimEnd('/') + "/";
        return _tunnelUrl;
    }

    private static void TryCaptureTunnelUrl(string? line, TaskCompletionSource<string> ready)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var match = TryCloudflareUrlPattern.Match(line);
        if (match.Success)
        {
            ready.TrySetResult(match.Value);
        }
    }

    private static async Task<string> EnsureCloudflaredAsync(CancellationToken cancellationToken)
    {
        var toolsDir = Path.Combine(AppSettings.ConfigDirectory, "tools");
        Directory.CreateDirectory(toolsDir);
        var cloudflaredPath = Path.Combine(toolsDir, "cloudflared.exe");
        if (File.Exists(cloudflaredPath) && new FileInfo(cloudflaredPath).Length > 10 * 1024 * 1024)
        {
            return cloudflaredPath;
        }

        var tempPath = cloudflaredPath + ".download";
        await using (var input = await Client.GetStreamAsync(CloudflaredDownloadUrl, cancellationToken).ConfigureAwait(false))
        await using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, cloudflaredPath, overwrite: true);
        return cloudflaredPath;
    }

    private static async Task<FirebaseAuthResult> SignInAnonymouslyAsync(string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={Uri.EscapeDataString(apiKey)}")
        {
            Content = new StringContent("{\"returnSecureToken\":true}", Encoding.UTF8, "application/json")
        };

        using var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Firebase anonymous auth failed: {FirebaseErrorText(text)}");
        }

        var auth = JsonSerializer.Deserialize<FirebaseAuthResult>(text, JsonOptions);
        if (auth is null || string.IsNullOrWhiteSpace(auth.IdToken) || string.IsNullOrWhiteSpace(auth.LocalId))
        {
            throw new InvalidOperationException("Firebase anonymous auth response did not include a usable token.");
        }

        return auth;
    }

    private async Task PublishSessionAsync(FirebaseAuthResult auth, string tunnelUrl, string localUrl, int clipCount, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var body = new
        {
            app = "EMX Clips Remote Share",
            sessionId = _settings.FirebaseSessionId,
            owner = auth.LocalId,
            pcName = Environment.MachineName,
            portalUrl = tunnelUrl.TrimEnd('/'),
            localUrl,
            clipCount,
            updatedAt = now,
            expiresAt = now.AddHours(8)
        };

        var url = $"{SessionRestUrl()}?auth={Uri.EscapeDataString(auth.IdToken)}";
        using var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
        };

        using var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Realtime Database session publish failed: {FirebaseErrorText(text)}");
        }
    }

    private string BuildCompanionUrl()
    {
        var builder = new UriBuilder(CompanionUrl);
        var sessionUrl = SessionRestUrl();
        builder.Query =
            "remoteSession=" + Uri.EscapeDataString(sessionUrl) +
            "&apiKey=" + Uri.EscapeDataString(_settings.FirebaseApiKey);
        return builder.Uri.ToString();
    }

    private string SessionRestUrl()
    {
        var database = _settings.FirebaseDatabaseUrl.Trim().TrimEnd('/');
        return $"{database}/emxClipSessions/{Uri.EscapeDataString(_settings.FirebaseSessionId)}.json";
    }

    private static string FirebaseErrorText(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? text;
            }
        }
        catch
        {
            // Fall through to raw text.
        }

        return string.IsNullOrWhiteSpace(text) ? "unknown Firebase error" : text;
    }

    private static bool IsFirebasePermissionError(Exception ex) =>
        ex.Message.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        DisposeTunnel();
        GC.SuppressFinalize(this);
    }

    private void DisposeTunnel()
    {
        if (_cloudflared is null)
        {
            return;
        }

        try
        {
            if (!_cloudflared.HasExited)
            {
                _cloudflared.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort on app exit.
        }
        finally
        {
            _cloudflared.Dispose();
            _cloudflared = null;
            _tunnelUrl = "";
        }
    }

    private sealed record FirebaseAuthResult(
        [property: JsonPropertyName("idToken")] string IdToken,
        [property: JsonPropertyName("localId")] string LocalId);
}
