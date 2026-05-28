using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EmxClips;

public sealed record UpdateCheckResult(
    Version CurrentVersion,
    Version LatestVersion,
    bool IsUpdateAvailable,
    UpdateManifest Manifest);

public sealed class UpdateManifest
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = "";

    [JsonPropertyName("releaseNotesUrl")]
    public string ReleaseNotesUrl { get; set; } = "";

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }
}

public static class UpdateService
{
    public const string LatestReleaseUrl = "https://github.com/tjcorp420/EMX-Clips/releases/latest";
    private const int MaxDownloadAttempts = 3;
    private const long MinimumExpectedExeBytes = 10 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public static async Task<UpdateCheckResult> CheckAsync(string manifestUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(manifestUrl))
        {
            throw new InvalidOperationException("No update manifest URL is configured.");
        }

        using var client = CreateHttpClient();

        string json;
        try
        {
            json = await client.GetStringAsync(manifestUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"No update manifest was found yet. Push EMX Clips to GitHub and create the first release tag, then Check Updates will use:\n\n{manifestUrl}", ex);
        }
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions)
            ?? throw new InvalidOperationException("Update manifest could not be read.");

        if (!Version.TryParse(NormalizeVersion(manifest.Version), out var latestVersion))
        {
            throw new InvalidOperationException($"Update manifest has an invalid version: {manifest.Version}");
        }

        return new UpdateCheckResult(
            CurrentVersion,
            latestVersion,
            latestVersion > CurrentVersion,
            manifest);
    }

    public static async Task<string> DownloadUpdateAsync(UpdateManifest manifest, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(manifest.DownloadUrl))
        {
            throw new InvalidOperationException("The update manifest does not include a download URL.");
        }

        var updatesDirectory = Path.Combine(AppSettings.ConfigDirectory, "Updates");
        Directory.CreateDirectory(updatesDirectory);

        var fileName = $"EMX Clips {manifest.Version}.exe";
        var destinationPath = Path.Combine(updatesDirectory, fileName);

        var tempPath = Path.Combine(updatesDirectory, $"download-{Guid.NewGuid():N}.tmp");
        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxDownloadAttempts; attempt++)
        {
            try
            {
                progress?.Report(attempt == 1
                    ? "Downloading update..."
                    : $"Download check failed. Retrying update ({attempt}/{MaxDownloadAttempts})...");

                await DownloadFileOnceAsync(manifest.DownloadUrl, tempPath, cancellationToken).ConfigureAwait(false);
                progress?.Report("Verifying update...");
                await ValidateDownloadedExecutableAsync(tempPath, manifest, cancellationToken).ConfigureAwait(false);

                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }

                File.Move(tempPath, destinationPath);
                return destinationPath;
            }
            catch (Exception ex) when (attempt < MaxDownloadAttempts)
            {
                lastError = ex;
                TryDelete(tempPath);
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }
        }

        throw new InvalidOperationException("Update download failed after multiple tries.", lastError);
    }

    private static async Task DownloadFileOnceAsync(string downloadUrl, string destinationPath, CancellationToken cancellationToken)
    {
        using var client = CreateHttpClient();
        using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var download = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var file = File.Create(destinationPath);
        await download.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ValidateDownloadedExecutableAsync(string destinationPath, UpdateManifest manifest, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(destinationPath);
        var fileLength = fileInfo.Exists ? fileInfo.Length : 0;
        if (!fileInfo.Exists || fileLength < MinimumExpectedExeBytes)
        {
            throw new InvalidOperationException($"Update download was incomplete ({fileLength:N0} bytes).");
        }

        if (manifest.SizeBytes > 0 && fileLength != manifest.SizeBytes)
        {
            throw new InvalidOperationException($"Update download size mismatch. Expected {manifest.SizeBytes:N0} bytes, got {fileLength:N0} bytes.");
        }

        await using (var header = File.OpenRead(destinationPath))
        {
            var mz = new byte[2];
            if (await header.ReadAsync(mz.AsMemory(0, 2), cancellationToken).ConfigureAwait(false) != 2 ||
                mz[0] != (byte)'M' ||
                mz[1] != (byte)'Z')
            {
                throw new InvalidOperationException("Update download was not a Windows EXE. GitHub may have returned an error page instead of the release file.");
            }
        }

        if (!string.IsNullOrWhiteSpace(manifest.Sha256) &&
            !manifest.Sha256.Contains("PUT_", StringComparison.OrdinalIgnoreCase))
        {
            var expectedHash = NormalizeHash(manifest.Sha256);
            var hash = await ComputeSha256Async(destinationPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Update verification failed. Expected SHA256 {expectedHash}, got {hash}. Download size was {fileLength:N0} bytes.");
            }
        }
    }

    public static void ApplyDownloadedUpdateAndRestart(string downloadedExePath)
    {
        var currentExePath = Application.ExecutablePath;
        var scriptPath = Path.Combine(AppSettings.ConfigDirectory, "Updates", "apply-emx-update.cmd");
        var currentProcessId = Environment.ProcessId;

        var script = $"""
@echo off
setlocal
set "NEW_EXE={downloadedExePath}"
set "CURRENT_EXE={currentExePath}"
:wait
tasklist /FI "PID eq {currentProcessId}" | find "{currentProcessId}" >nul
if not errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto wait
)
copy /Y "%NEW_EXE%" "%CURRENT_EXE%" >nul
start "" "%CURRENT_EXE%"
del "%~f0"
""";

        File.WriteAllText(scriptPath, script);
        Process.Start(new ProcessStartInfo
        {
            FileName = scriptPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(currentExePath) ?? Environment.CurrentDirectory
        });

        Application.Exit();
    }

    public static void OpenReleaseNotes(UpdateManifest manifest)
    {
        var url = string.IsNullOrWhiteSpace(manifest.ReleaseNotesUrl)
            ? manifest.DownloadUrl
            : manifest.ReleaseNotesUrl;

        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    public static void OpenLatestReleasePage()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = LatestReleaseUrl,
            UseShellExecute = true
        });
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("EMX-Clips-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/octet-stream");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.CacheControl = new()
        {
            NoCache = true,
            NoStore = true
        };
        client.DefaultRequestHeaders.Pragma.ParseAdd("no-cache");
        return client;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup only; the next attempt writes a fresh temp file.
        }
    }

    private static string NormalizeVersion(string value) =>
        value.Trim().TrimStart('v', 'V');

    private static string NormalizeHash(string value) =>
        value.Trim().Replace("sha256:", "", StringComparison.OrdinalIgnoreCase);
}
