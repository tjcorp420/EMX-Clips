using System.Diagnostics;

namespace EmxClips;

public sealed record ObsCrashHint(string CrashFile, string PluginName, DateTime CrashTime);

public static class ObsTools
{
    private static readonly (string Module, string Name)[] ThirdPartyCrashModules =
    [
        ("aitum-multistream.dll", "Aitum Multistream"),
        ("logi_obs_plugin_x64.dll", "Logitech OBS plugin")
    ];

    public static string? ResolveObsPath(string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "obs-studio", "bin", "64bit", "obs64.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "obs-studio", "bin", "64bit", "obs64.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    public static async Task InstallObsAsync(CancellationToken cancellationToken = default)
    {
        var winget = ResolveExecutable("winget.exe");
        if (winget is null)
        {
            OpenObsDownloadPage();
            return;
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = winget,
            Arguments = "install --id OBSProject.OBSStudio -e --accept-source-agreements --accept-package-agreements",
            UseShellExecute = false,
            CreateNoWindow = true
        });

        if (process is null)
        {
            OpenObsDownloadPage();
            return;
        }

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            OpenObsDownloadPage();
        }
    }

    public static void OpenObsDownloadPage()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://obsproject.com/download",
            UseShellExecute = true
        });
    }

    public static string? ResolveFfmpegPath(string? configuredObsPath = null)
    {
        var ffmpeg = ResolveExecutable("ffmpeg.exe");
        if (ffmpeg is not null)
        {
            return ffmpeg;
        }

        var obsPath = ResolveObsPath(configuredObsPath);
        if (obsPath is null)
        {
            return null;
        }

        var obsDirectory = Path.GetDirectoryName(obsPath);
        if (obsDirectory is null)
        {
            return null;
        }

        var localCandidate = Path.Combine(obsDirectory, "ffmpeg.exe");
        return File.Exists(localCandidate) ? localCandidate : null;
    }

    public static ObsCrashHint? GetRecentCrashHint(TimeSpan maxAge)
    {
        try
        {
            var crashDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "obs-studio",
                "crashes");

            if (!Directory.Exists(crashDirectory))
            {
                return null;
            }

            var latestCrash = Directory.EnumerateFiles(crashDirectory, "Crash*.txt")
                .Select(path => new FileInfo(path))
                .Where(info => info.Exists)
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .FirstOrDefault();

            if (latestCrash is null || DateTime.UtcNow - latestCrash.LastWriteTimeUtc > maxAge)
            {
                return null;
            }

            var text = File.ReadAllText(latestCrash.FullName);
            var crashedThread = ExtractCrashedThread(text);
            var primarySearchText = string.IsNullOrWhiteSpace(crashedThread) ? text : crashedThread;

            foreach (var (module, name) in ThirdPartyCrashModules)
            {
                if (primarySearchText.Contains(module, StringComparison.OrdinalIgnoreCase))
                {
                    return new ObsCrashHint(latestCrash.FullName, name, latestCrash.LastWriteTime);
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractCrashedThread(string text)
    {
        var crashIndex = text.IndexOf("(Crashed)", StringComparison.OrdinalIgnoreCase);
        if (crashIndex < 0)
        {
            return "";
        }

        var nextThreadIndex = text.IndexOf("\nThread ", crashIndex + 1, StringComparison.OrdinalIgnoreCase);
        if (nextThreadIndex < 0)
        {
            nextThreadIndex = Math.Min(text.Length, crashIndex + 5000);
        }

        return text[crashIndex..nextThreadIndex];
    }

    private static string? ResolveExecutable(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            var candidate = Path.Combine(directory.Trim(), name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
