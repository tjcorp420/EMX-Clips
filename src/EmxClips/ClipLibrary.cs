namespace EmxClips;

public sealed record ClipFile(string FullPath, string Name, DateTime ModifiedAt, long SizeBytes);

public static class ClipLibrary
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".mkv",
        ".mov",
        ".m4v",
        ".webm",
        ".flv",
        ".ts"
    };

    public static IReadOnlyList<ClipFile> Load(string folder)
    {
        Directory.CreateDirectory(folder);

        return Directory.EnumerateFiles(folder)
            .Where(path => VideoExtensions.Contains(Path.GetExtension(path)))
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new ClipFile(info.FullName, info.Name, info.LastWriteTime, info.Length);
            })
            .OrderByDescending(clip => clip.ModifiedAt)
            .ToList();
    }

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.#} {units[unit]}";
    }
}

