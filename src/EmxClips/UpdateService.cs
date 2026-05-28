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
}

public static class UpdateService
{
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

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EMX-Clips-Updater");

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

        progress?.Report("Downloading update...");
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EMX-Clips-Updater");
        await using (var download = await client.GetStreamAsync(manifest.DownloadUrl, cancellationToken).ConfigureAwait(false))
        await using (var file = File.Create(destinationPath))
        {
            await download.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(manifest.Sha256) &&
            !manifest.Sha256.Contains("PUT_", StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report("Verifying update...");
            var hash = await ComputeSha256Async(destinationPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(hash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(destinationPath);
                throw new InvalidOperationException("Update verification failed. The downloaded file hash did not match the release manifest.");
            }
        }

        return destinationPath;
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

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static string NormalizeVersion(string value) =>
        value.Trim().TrimStart('v', 'V');
}
