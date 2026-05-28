using System.Diagnostics;

namespace EmxClips;

public static class ClipExporter
{
    public static string SuggestedMp4Path(ClipFile clip)
    {
        var folder = Path.GetDirectoryName(clip.FullPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        var name = Path.GetFileNameWithoutExtension(clip.Name);
        return Path.Combine(folder, $"{name}.mp4");
    }

    public static async Task ExportMp4Async(ClipFile clip, string destinationPath, string? configuredObsPath = null, CancellationToken cancellationToken = default)
    {
        var ffmpeg = ObsTools.ResolveFfmpegPath(configuredObsPath);
        if (ffmpeg is null)
        {
            throw new InvalidOperationException("MP4 export needs FFmpeg. Install FFmpeg or use OBS File > Remux Recordings for now.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? Environment.CurrentDirectory);

        var copyResult = await RunFfmpegAsync(ffmpeg, $"-hide_banner -y -i {Quote(clip.FullPath)} -c copy -movflags +faststart {Quote(destinationPath)}", cancellationToken);
        if (copyResult.ExitCode == 0 && File.Exists(destinationPath))
        {
            return;
        }

        var encodeResult = await RunFfmpegAsync(
            ffmpeg,
            $"-hide_banner -y -i {Quote(clip.FullPath)} -c:v libx264 -preset veryfast -crf 18 -c:a aac -b:a 192k -movflags +faststart {Quote(destinationPath)}",
            cancellationToken);

        if (encodeResult.ExitCode != 0 || !File.Exists(destinationPath))
        {
            throw new InvalidOperationException($"MP4 export failed.\n\nFFmpeg detail: {encodeResult.ErrorText}");
        }
    }

    public static async Task ExportPhoneMp4Async(ClipFile clip, string destinationPath, string? configuredObsPath = null, CancellationToken cancellationToken = default)
    {
        var ffmpeg = ObsTools.ResolveFfmpegPath(configuredObsPath);
        if (ffmpeg is null)
        {
            throw new InvalidOperationException("Phone MP4 export needs FFmpeg from OBS or PATH.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? Environment.CurrentDirectory);

        var encodeResult = await RunFfmpegAsync(
            ffmpeg,
            $"-hide_banner -y -i {Quote(clip.FullPath)} -map 0:v:0 -map 0:a? -vf scale=trunc(iw/2)*2:trunc(ih/2)*2 -c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -profile:v high -level 4.2 -c:a aac -b:a 160k -movflags +faststart {Quote(destinationPath)}",
            cancellationToken);

        if (encodeResult.ExitCode != 0 || !File.Exists(destinationPath))
        {
            throw new InvalidOperationException($"Phone MP4 export failed.\n\nFFmpeg detail: {encodeResult.ErrorText}");
        }
    }

    private static async Task<(int ExitCode, string ErrorText)> RunFfmpegAsync(string ffmpegPath, string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            }
        };

        process.Start();
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (process.ExitCode, await errorTask.ConfigureAwait(false));
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
