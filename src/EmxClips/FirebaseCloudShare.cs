using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EmxClips;

public sealed record FirebaseCloudShareResult(string CompanionUrl, int UploadedClips, long UploadedBytes);

public static class FirebaseCloudShare
{
    private const int MaxCloudClips = 8;
    private const long MaxSingleClipBytes = 450L * 1024L * 1024L;
    private const string CompanionUrl = "https://emx-clips-companion.vercel.app/";

    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromMinutes(12)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static bool IsConfigured(AppSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.FirebaseApiKey) &&
        !string.IsNullOrWhiteSpace(settings.FirebaseStorageBucket);

    public static async Task<FirebaseCloudShareResult> PublishLatestAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured(settings))
        {
            throw new InvalidOperationException("Firebase Cloud Share needs a Firebase API key and Storage bucket in Settings.");
        }

        if (string.IsNullOrWhiteSpace(settings.FirebaseSessionId))
        {
            settings.FirebaseSessionId = Guid.NewGuid().ToString("N");
            settings.Save();
        }

        var clips = ClipLibrary.Load(settings.ClipsFolder)
            .Where(IsPhoneFriendly)
            .Where(clip => clip.SizeBytes <= MaxSingleClipBytes)
            .Take(MaxCloudClips)
            .ToList();

        if (clips.Count == 0)
        {
            throw new InvalidOperationException("No phone-ready MP4 clips found. Save a clip as MP4 first, then try Cloud Share again.");
        }

        var auth = await SignInAnonymouslyAsync(settings.FirebaseApiKey, cancellationToken).ConfigureAwait(false);
        var sessionRoot = $"emx-clips/{auth.LocalId}/{settings.FirebaseSessionId}";
        var uploaded = new List<CloudClipItem>();
        long totalBytes = 0;

        foreach (var clip in clips)
        {
            var safeName = SafeFileName(clip.Name);
            var objectPath = $"{sessionRoot}/clips/{safeName}";
            var contentType = ContentTypeFor(clip.FullPath);
            await UploadFileAsync(settings.FirebaseStorageBucket, objectPath, clip.FullPath, contentType, auth.IdToken, cancellationToken).ConfigureAwait(false);

            totalBytes += clip.SizeBytes;
            uploaded.Add(new CloudClipItem(
                clip.Name,
                clip.ModifiedAt.ToUniversalTime(),
                clip.SizeBytes,
                ClipLibrary.FormatSize(clip.SizeBytes),
                MediaUrl(settings.FirebaseStorageBucket, objectPath),
                contentType));
        }

        var indexPath = $"{sessionRoot}/index.json";
        var index = new CloudClipIndex(
            "EMX Clips Cloud Share",
            DateTime.UtcNow,
            uploaded);
        var indexJson = JsonSerializer.Serialize(index, JsonOptions);
        await UploadBytesAsync(settings.FirebaseStorageBucket, indexPath, Encoding.UTF8.GetBytes(indexJson), "application/json", auth.IdToken, cancellationToken).ConfigureAwait(false);

        var builder = new UriBuilder(CompanionUrl);
        builder.Query = "cloudIndex=" + Uri.EscapeDataString(MediaUrl(settings.FirebaseStorageBucket, indexPath));
        return new FirebaseCloudShareResult(builder.Uri.ToString(), uploaded.Count, totalBytes);
    }

    private static bool IsPhoneFriendly(ClipFile clip) =>
        string.Equals(Path.GetExtension(clip.FullPath), ".mp4", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Path.GetExtension(clip.FullPath), ".mov", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Path.GetExtension(clip.FullPath), ".m4v", StringComparison.OrdinalIgnoreCase);

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
            throw new InvalidOperationException($"Firebase auth failed: {FirebaseErrorText(text)}");
        }

        var auth = JsonSerializer.Deserialize<FirebaseAuthResult>(text, JsonOptions);
        if (auth is null || string.IsNullOrWhiteSpace(auth.IdToken) || string.IsNullOrWhiteSpace(auth.LocalId))
        {
            throw new InvalidOperationException("Firebase auth response did not include a usable token.");
        }

        return auth;
    }

    private static async Task UploadFileAsync(string bucket, string objectPath, string filePath, string contentType, string idToken, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        await UploadAsync(bucket, objectPath, content, idToken, cancellationToken).ConfigureAwait(false);
    }

    private static async Task UploadBytesAsync(string bucket, string objectPath, byte[] bytes, string contentType, string idToken, CancellationToken cancellationToken)
    {
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        await UploadAsync(bucket, objectPath, content, idToken, cancellationToken).ConfigureAwait(false);
    }

    private static async Task UploadAsync(string bucket, string objectPath, HttpContent content, string idToken, CancellationToken cancellationToken)
    {
        var url = $"https://firebasestorage.googleapis.com/v0/b/{Uri.EscapeDataString(bucket)}/o?uploadType=media&name={Uri.EscapeDataString(objectPath)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);

        using var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Firebase upload failed: {FirebaseErrorText(text)}");
        }
    }

    private static string MediaUrl(string bucket, string objectPath) =>
        $"https://firebasestorage.googleapis.com/v0/b/{Uri.EscapeDataString(bucket)}/o/{Uri.EscapeDataString(objectPath)}?alt=media";

    private static string ContentTypeFor(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mov" => "video/quicktime",
            ".m4v" => "video/x-m4v",
            _ => "video/mp4"
        };

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? $"clip-{DateTime.UtcNow:yyyyMMddHHmmss}.mp4" : cleaned;
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

    private sealed record FirebaseAuthResult(
        [property: JsonPropertyName("idToken")] string IdToken,
        [property: JsonPropertyName("localId")] string LocalId);

    private sealed record CloudClipIndex(string App, DateTime GeneratedAt, IReadOnlyList<CloudClipItem> Clips);

    private sealed record CloudClipItem(
        string Name,
        DateTime SavedAt,
        long SizeBytes,
        string Size,
        string Url,
        string ContentType);
}
