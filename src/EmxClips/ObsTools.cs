using System.Diagnostics;

namespace EmxClips;

public static class ObsTools
{
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
