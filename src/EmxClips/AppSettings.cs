using System.Text.Json;
using System.Text.Json.Serialization;

namespace EmxClips;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
    NoRepeat = 0x4000
}

public sealed class AppSettings
{
    private const string DefaultUpdateManifestUrl = "https://github.com/tjcorp420/EMX-Clips/releases/latest/download/update-manifest.json";
    private const string DefaultFirebaseApiKey = "AIzaSyAxrbDRZWicKlAEHcMHkrJ5o7rT1lJlty0";
    private const string DefaultFirebaseStorageBucket = "emxclips-86f00.firebasestorage.app";
    private const string DefaultFirebaseDatabaseUrl = "https://emxclips-86f00-default-rtdb.firebaseio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string ObsWebSocketHost { get; set; } = "127.0.0.1";
    public int ObsWebSocketPort { get; set; } = 4455;
    public string ObsWebSocketPassword { get; set; } = "";
    public string ObsPath { get; set; } = "";
    public string ClipsFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "EMX Clips");
    public int ReplayBufferSeconds { get; set; } = 60;
    public int ReplayBufferMemoryMb { get; set; } = 2048;
    public bool AutoLaunchObs { get; set; } = true;
    public bool AutoStartReplayBuffer { get; set; } = true;
    public bool MinimizeObsToTray { get; set; } = true;
    public bool UseDedicatedObsWorkspace { get; set; } = true;
    public bool CaptureMicrophone { get; set; } = true;
    public string MicrophoneDeviceId { get; set; } = "default";
    public string MicrophoneDeviceName { get; set; } = "Default";
    public bool UseFirebaseCloudShare { get; set; } = true;
    public string FirebaseApiKey { get; set; } = DefaultFirebaseApiKey;
    public string FirebaseStorageBucket { get; set; } = DefaultFirebaseStorageBucket;
    public string FirebaseDatabaseUrl { get; set; } = DefaultFirebaseDatabaseUrl;
    public string FirebaseSessionId { get; set; } = "";
    public bool SetupCompleted { get; set; }
    public Keys HotkeyKey { get; set; } = Keys.F8;
    public HotkeyModifiers HotkeyModifiers { get; set; } = HotkeyModifiers.Control | HotkeyModifiers.Alt;
    public Keys ToggleDashboardHotkeyKey { get; set; } = Keys.H;
    public HotkeyModifiers ToggleDashboardHotkeyModifiers { get; set; } = HotkeyModifiers.Control | HotkeyModifiers.Alt;
    public string UpdateManifestUrl { get; set; } = DefaultUpdateManifestUrl;

    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EMX Clips");

    public static string SettingsPath => Path.Combine(ConfigDirectory, "settings.json");

    public static AppSettings Load()
    {
        Directory.CreateDirectory(ConfigDirectory);

        if (!File.Exists(SettingsPath))
        {
            var settings = new AppSettings();
            settings.Save();
            return settings;
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.Migrate();
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(ClipsFolder);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }

    private void Migrate()
    {
        if (string.IsNullOrWhiteSpace(FirebaseSessionId))
        {
            FirebaseSessionId = Guid.NewGuid().ToString("N");
        }

        if (string.IsNullOrWhiteSpace(FirebaseApiKey))
        {
            FirebaseApiKey = DefaultFirebaseApiKey;
        }

        if (string.IsNullOrWhiteSpace(FirebaseStorageBucket))
        {
            FirebaseStorageBucket = DefaultFirebaseStorageBucket;
        }

        if (string.IsNullOrWhiteSpace(FirebaseDatabaseUrl))
        {
            FirebaseDatabaseUrl = DefaultFirebaseDatabaseUrl;
        }

        if (string.IsNullOrWhiteSpace(UpdateManifestUrl) ||
            UpdateManifestUrl.Contains("YOURNAME", StringComparison.OrdinalIgnoreCase) ||
            UpdateManifestUrl.Contains("EMXTweaks/EMX-Clips", StringComparison.OrdinalIgnoreCase))
        {
            UpdateManifestUrl = DefaultUpdateManifestUrl;
        }
    }
}
