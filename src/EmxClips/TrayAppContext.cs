using System.Diagnostics;
using System.Collections.Specialized;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EmxClips;

internal sealed record DisplayCaptureTarget(string? MonitorId, int Width, int Height);

public sealed class TrayAppContext : ApplicationContext
{
    private const string DisplayCaptureInputName = "EMX Display Capture";
    private const string DefaultObsMicInputName = "Mic/Aux";
    private const string EmxMicInputName = "EMX Mic Capture";
    private const string ObsMicInputKind = "wasapi_input_capture";
    private const string EmxObsProfileName = "EMX Clips";
    private const string EmxObsProfileDirectoryName = "EMX_Clips";
    private const string EmxObsSceneCollectionName = "EMX Clips";
    private const string EmxObsSceneCollectionFileName = "EMX_Clips";
    private const int DefaultCaptureFps = 60;
    private const int ReplayImportTimeoutSeconds = 18;

    private readonly AppSettings _settings;
    private readonly Control _uiInvoker;
    private readonly NotifyIcon _notifyIcon;
    private readonly HotkeyWindow _clipHotkeyWindow;
    private readonly HotkeyWindow _toggleHotkeyWindow;
    private readonly Icon _icon;
    private readonly System.Threading.Timer _bufferWatchdog;
    private ObsWebSocketClient? _obsClient;
    private PhoneCompanionServer? _phoneCompanionServer;
    private PhoneCompanionForm? _phoneCompanionForm;
    private FirebaseRemoteShare? _firebaseRemoteShare;
    private const string HostedCompanionUrl = "https://emx-clips-companion.vercel.app/";
    private DashboardForm? _dashboard;
    private bool _busy;
    private bool _watchdogBusy;

    public TrayAppContext()
    {
        _uiInvoker = new Control();
        _ = _uiInvoker.Handle;
        var firstRun = !File.Exists(AppSettings.SettingsPath);
        _settings = AppSettings.Load();
        Directory.CreateDirectory(_settings.ClipsFolder);

        _icon = CreateEmxIcon();
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "EMX Clips",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        _notifyIcon.DoubleClick += (_, _) => OpenDashboard();

        _clipHotkeyWindow = new HotkeyWindow(0x454D5801);
        _clipHotkeyWindow.HotkeyPressed += (_, _) => SaveClipFromUi();
        _toggleHotkeyWindow = new HotkeyWindow(0x454D5802);
        _toggleHotkeyWindow.HotkeyPressed += (_, _) => ToggleDashboard();
        RegisterHotkeys();

        _bufferWatchdog = new System.Threading.Timer(
            _ => _ = EnsureBufferRunningInBackgroundAsync(),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);

        if (firstRun)
        {
            _uiInvoker.BeginInvoke(() =>
            {
                OpenDashboard();
                _ = StartupAsync();
            });
        }
        else
        {
            _ = StartupAsync();
        }

        _bufferWatchdog.Change(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(30));
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open EMX Clips", null, (_, _) => OpenDashboard());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Save clip", null, (_, _) => SaveClipFromUi());
        menu.Items.Add("Copy latest clip", null, (_, _) => CopyLatestClip());
        menu.Items.Add("Open latest clip", null, (_, _) => OpenLatestClip());
        menu.Items.Add("Phone companion", null, (_, _) => OpenPhoneCompanion());
        menu.Items.Add("Restart replay buffer", null, (_, _) => RunUiTask(RestartReplayBufferAsync));
        menu.Items.Add("Pause replay buffer", null, (_, _) => RunUiTask(StopReplayBufferAsync));
        menu.Items.Add("Check updates", null, (_, _) => RunUiTask(CheckForUpdatesAsync));
        menu.Items.Add("Open release page", null, (_, _) => UpdateService.OpenLatestReleasePage());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open clips folder", null, (_, _) => OpenClipsFolder());
        menu.Items.Add("Settings", null, (_, _) => OpenDashboard());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        return menu;
    }

    private async Task StartupAsync()
    {
        try
        {
            if (!_settings.SetupCompleted)
            {
                OpenDashboard();
                SetDashboardStatus("Setup needed: open OBS in Normal Mode, enable WebSockets, then save EMX settings.");
                return;
            }

            if (_settings.AutoLaunchObs)
            {
                TryLaunchObs();
            }

            if (_settings.AutoStartReplayBuffer)
            {
                await StartReplayBufferAsync(showNotification: false);
            }
        }
        catch (Exception ex)
        {
            ShowBalloon("Setup needed", ex.Message, ToolTipIcon.Warning);
            SetDashboardStatus(ex.Message);
        }
    }

    private void RegisterHotkeys()
    {
        if (!_clipHotkeyWindow.Register(_settings.HotkeyKey, _settings.HotkeyModifiers))
        {
            ShowBalloon("Hotkey unavailable", "Change the save clip hotkey in EMX Clips settings.", ToolTipIcon.Warning);
        }

        if (!_toggleHotkeyWindow.Register(_settings.ToggleDashboardHotkeyKey, _settings.ToggleDashboardHotkeyModifiers))
        {
            ShowBalloon("Hotkey unavailable", "Change the show/hide hotkey in EMX Clips settings.", ToolTipIcon.Warning);
        }
    }

    private void SaveClipFromUi() => RunUiTask(SaveClipAsync);

    private void RunUiTask(Func<Task> action)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _ = Task.Run(async () =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                ShowBalloon("EMX Clips", ex.Message, ToolTipIcon.Warning);
                SetDashboardStatus(ex.Message);
            }
            finally
            {
                _busy = false;
            }
        });
    }

    private async Task StartReplayBufferAsync(bool showNotification = true)
    {
        TryLaunchObs();
        var client = await GetObsClientAsync();
        var liveOutputActive = await IsStreamingOrRecordingAsync(client);
        if (!liveOutputActive)
        {
            await TrySyncObsSettingsAsync(client);
        }

        if (!await client.GetReplayBufferActiveAsync())
        {
            try
            {
                await client.StartReplayBufferAsync();
            }
            catch (ObsRequestException ex)
            {
                throw new InvalidOperationException(liveOutputActive ? BuildLiveStartBufferHelp(ex) : BuildStartBufferHelp(ex), ex);
            }
        }

        if (showNotification)
        {
            ShowBalloon("Replay buffer on", liveOutputActive
                ? "Live Safe Mode is on. EMX is using OBS replay buffer without changing your stream setup."
                : $"EMX is capturing the last {_settings.ReplayBufferSeconds} seconds in memory.", ToolTipIcon.Info);
        }

        SetDashboardStatus(liveOutputActive
            ? $"Live Safe Mode: replay buffer on. EMX will save clips without changing the live OBS scene/profile."
            : $"Replay buffer on. Press Save Clip once to save the last {_settings.ReplayBufferSeconds} seconds.");
    }

    private async Task RestartReplayBufferAsync()
    {
        var client = await GetObsClientAsync();
        if (await IsStreamingOrRecordingAsync(client))
        {
            if (!await client.GetReplayBufferActiveAsync())
            {
                await StartReplayBufferAsync();
                return;
            }

            SetDashboardStatus("Live Safe Mode: OBS is live, so EMX did not restart outputs. Replay buffer is already running.");
            ShowBalloon("Live Safe Mode", "OBS is live, so EMX left stream outputs alone.", ToolTipIcon.Info);
            return;
        }

        if (await client.GetReplayBufferActiveAsync())
        {
            try
            {
                await client.StopReplayBufferAsync();
            }
            catch (ObsRequestException)
            {
                // Best effort; the follow-up start will restore the desired state.
            }
        }

        await StartReplayBufferAsync();
    }

    private async Task StopReplayBufferAsync()
    {
        var client = await GetObsClientAsync();
        if (await client.GetReplayBufferActiveAsync())
        {
            await client.StopReplayBufferAsync();
        }

        ShowBalloon("Replay buffer off", "EMX Clips stopped the replay buffer.", ToolTipIcon.Info);
        SetDashboardStatus("Replay buffer paused. Turn Auto-start replay buffer back on or click Restart Buffer for Medal-style clipping.");
    }

    private async Task SaveClipAsync()
    {
        TryLaunchObs();
        var client = await GetObsClientAsync();
        var liveOutputActive = await IsStreamingOrRecordingAsync(client);
        if (!await client.GetReplayBufferActiveAsync())
        {
            if (!liveOutputActive)
            {
                await TrySyncObsSettingsAsync(client);
            }

            try
            {
                await client.StartReplayBufferAsync();
            }
            catch (ObsRequestException ex)
            {
                throw new InvalidOperationException(liveOutputActive ? BuildLiveStartBufferHelp(ex) : BuildStartBufferHelp(ex), ex);
            }
            ShowBalloon("Replay buffer started", liveOutputActive
                ? "Live Safe Mode started OBS replay buffer without changing stream setup. Wait for the buffer to fill."
                : $"EMX started the background buffer. It needs up to {_settings.ReplayBufferSeconds} seconds to fill.", ToolTipIcon.Info);
            SetDashboardStatus(liveOutputActive
                ? $"Live Safe Mode: replay buffer was off, so EMX started it without changing OBS settings. Wait {_settings.ReplayBufferSeconds} seconds, then clip again."
                : $"Background buffer was off. EMX started it now; future hotkey presses save the past {_settings.ReplayBufferSeconds} seconds.");
            return;
        }

        var replaySearchFolders = await GetReplaySearchFoldersAsync(client);
        var beforeSave = SnapshotVideoFiles(replaySearchFolders);
        var saveStartedAt = DateTime.Now;

        try
        {
            await client.SaveReplayBufferAsync();
        }
        catch (ObsRequestException ex)
        {
            throw new InvalidOperationException(BuildSaveClipHelp(ex), ex);
        }

        ShowBalloon("Clip saved", $"Saved the last {_settings.ReplayBufferSeconds} seconds.", ToolTipIcon.Info);
        SetDashboardStatus($"Clip saved. Waiting for OBS to finish writing it into EMX Clips...");
        _ = Task.Run(async () =>
        {
            try
            {
                var imported = await ImportSavedReplayIfNeededAsync(replaySearchFolders, beforeSave, saveStartedAt);
                if (imported is null)
                {
                    SetDashboardStatus("OBS said the clip saved, but EMX could not find the new file yet. Check OBS Settings > Output > Recording Path, then click Refresh Clips.");
                    return;
                }

                var fileName = Path.GetFileName(imported.Value.Path);
                SetDashboardStatus(imported.Value.Imported
                    ? $"Clip saved and imported into EMX Clips: {fileName}"
                    : $"Clip saved: {fileName}");
            }
            catch (Exception ex)
            {
                SetDashboardStatus($"Clip saved, but EMX could not import it into the Clips tab: {ex.Message}");
            }
            finally
            {
                RefreshDashboardClips();
            }
        });
    }

    private async Task<IReadOnlyList<string>> GetReplaySearchFoldersAsync(ObsWebSocketClient client)
    {
        var folders = new List<string>
        {
            _settings.ClipsFolder,
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };

        await AddProfileFolderAsync(client, folders, "SimpleOutput", "FilePath");
        await AddProfileFolderAsync(client, folders, "AdvOut", "RecFilePath");

        return folders
            .Select(NormalizeFolderPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task AddProfileFolderAsync(ObsWebSocketClient client, List<string> folders, string category, string name)
    {
        try
        {
            var folder = await client.GetProfileParameterAsync(category, name);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                folders.Add(folder);
            }
        }
        catch
        {
            // Some OBS output modes do not expose every profile parameter.
        }
    }

    private static string? NormalizeFolderPath(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(folder.Trim()));
        }
        catch
        {
            return null;
        }
    }

    private static HashSet<string> SnapshotVideoFiles(IEnumerable<string> folders)
    {
        var snapshot = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            foreach (var path in EnumerateVideoFilesSafe(folder))
            {
                snapshot.Add(Path.GetFullPath(path));
            }
        }

        return snapshot;
    }

    private async Task<(string Path, bool Imported)?> ImportSavedReplayIfNeededAsync(
        IReadOnlyList<string> replaySearchFolders,
        HashSet<string> beforeSave,
        DateTime saveStartedAt)
    {
        var sourcePath = await WaitForSavedReplayAsync(replaySearchFolders, beforeSave, saveStartedAt);
        if (sourcePath is null)
        {
            return null;
        }

        Directory.CreateDirectory(_settings.ClipsFolder);
        if (SamePath(Path.GetDirectoryName(sourcePath) ?? "", _settings.ClipsFolder))
        {
            return (sourcePath, false);
        }

        var destinationPath = ChooseImportedClipPath(sourcePath);
        await CopyFileWithRetryAsync(sourcePath, destinationPath);
        return (destinationPath, true);
    }

    private static async Task<string?> WaitForSavedReplayAsync(
        IReadOnlyList<string> folders,
        HashSet<string> beforeSave,
        DateTime saveStartedAt)
    {
        var until = DateTime.UtcNow.AddSeconds(ReplayImportTimeoutSeconds);
        while (DateTime.UtcNow < until)
        {
            var newest = FindNewestReplayFile(folders, beforeSave, saveStartedAt);
            if (newest is not null)
            {
                await WaitForFileToSettleAsync(newest);
                return newest;
            }

            await Task.Delay(500);
        }

        return null;
    }

    private static string? FindNewestReplayFile(
        IEnumerable<string> folders,
        HashSet<string> beforeSave,
        DateTime saveStartedAt)
    {
        var threshold = saveStartedAt.AddSeconds(-3);
        return folders
            .SelectMany(EnumerateVideoFilesSafe)
            .Select(path => new FileInfo(path))
            .Where(info => info.Exists)
            .Where(info => !beforeSave.Contains(info.FullName))
            .Where(info => info.LastWriteTime >= threshold)
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .Select(info => info.FullName)
            .FirstOrDefault();
    }

    private static IEnumerable<string> EnumerateVideoFilesSafe(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return Array.Empty<string>();
        }

        try
        {
            return Directory.EnumerateFiles(folder)
                .Where(ClipLibrary.IsVideoFile)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private string ChooseImportedClipPath(string sourcePath)
    {
        var fileName = Path.GetFileName(sourcePath);
        var destinationPath = Path.Combine(_settings.ClipsFolder, fileName);
        if (!File.Exists(destinationPath))
        {
            return destinationPath;
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 1; index < 1000; index++)
        {
            var candidate = Path.Combine(_settings.ClipsFolder, $"{name} EMX import {index}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(_settings.ClipsFolder, $"{name} EMX import {DateTime.Now:HHmmss}{extension}");
    }

    private static async Task WaitForFileToSettleAsync(string path)
    {
        long previousLength = -1;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length > 0 && info.Length == previousLength)
                {
                    return;
                }

                previousLength = info.Exists ? info.Length : -1;
            }
            catch
            {
                // OBS may still be finalizing the file.
            }

            await Task.Delay(300);
        }
    }

    private static async Task CopyFileWithRetryAsync(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? ".");
        for (var attempt = 0; attempt < 12; attempt++)
        {
            try
            {
                File.Copy(sourcePath, destinationPath, overwrite: false);
                return;
            }
            catch (IOException) when (attempt < 11)
            {
                await Task.Delay(350);
            }
            catch (UnauthorizedAccessException) when (attempt < 11)
            {
                await Task.Delay(350);
            }
        }

        File.Copy(sourcePath, destinationPath, overwrite: false);
    }

    private async Task<ObsWebSocketClient> GetObsClientAsync()
    {
        _obsClient ??= new ObsWebSocketClient(
            _settings.ObsWebSocketHost,
            _settings.ObsWebSocketPort,
            _settings.ObsWebSocketPassword);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            await _obsClient.ConnectAsync(timeout.Token);
            return _obsClient;
        }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
        {
            ResetObsClient();
            throw new InvalidOperationException(BuildObsConnectionHelp(), ex);
        }
        catch (Exception ex) when (ex is WebSocketException or SocketException or InvalidOperationException)
        {
            ResetObsClient();
            throw new InvalidOperationException(BuildObsConnectionHelp(ex.Message), ex);
        }
    }

    private void ResetObsClient()
    {
        _obsClient?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _obsClient = null;
    }

    private string BuildObsConnectionHelp(string? detail = null)
    {
        var message = $"Could not connect to OBS at {_settings.ObsWebSocketHost}:{_settings.ObsWebSocketPort}. Open OBS in Normal Mode, enable Tools > WebSocket Server Settings, and make sure EMX host/port/password exactly match OBS.";
        return string.IsNullOrWhiteSpace(detail) ? message : $"{message}\n\nOBS detail: {detail}";
    }

    private async Task<bool> TrySyncObsSettingsAsync(ObsWebSocketClient client)
    {
        Directory.CreateDirectory(_settings.ClipsFolder);

        var streamOrRecordActive = await IsStreamingOrRecordingAsync(client);
        if (streamOrRecordActive)
        {
            return false;
        }

        await EnsureDedicatedObsWorkspaceAsync(client);
        var replayWasActive = await client.GetReplayBufferActiveAsync();
        if (replayWasActive)
        {
            await client.StopReplayBufferAsync();
        }

        var syncChanged = false;
        try
        {
            await TrySetProfileParameterAsync(client, "SimpleOutput", "FilePath", _settings.ClipsFolder);
            await TrySetProfileParameterAsync(client, "SimpleOutput", "RecRB", "true");
            await TrySetProfileParameterAsync(client, "SimpleOutput", "RecRBTime", _settings.ReplayBufferSeconds.ToString());
            await TrySetProfileParameterAsync(client, "SimpleOutput", "RecRBSize", _settings.ReplayBufferMemoryMb.ToString());
            await TrySetProfileParameterAsync(client, "AdvOut", "RecFilePath", _settings.ClipsFolder);
            await TrySetProfileParameterAsync(client, "AdvOut", "RecRB", "true");
            await TrySetProfileParameterAsync(client, "AdvOut", "RecRBTime", _settings.ReplayBufferSeconds.ToString());
            await TrySetProfileParameterAsync(client, "AdvOut", "RecRBSize", _settings.ReplayBufferMemoryMb.ToString());
            syncChanged = await EnsureDisplayCaptureAsync(client, createIfMissing: true, allowVideoSettingsChange: true);
            await TryApplyMicrophoneSettingsAsync(client);
        }
        finally
        {
            if (replayWasActive)
            {
                try
                {
                    if (!await client.GetReplayBufferActiveAsync())
                    {
                        await client.StartReplayBufferAsync();
                    }
                }
                catch
                {
                    // Manual restart and the watchdog can recover if OBS refuses to restart immediately.
                }
            }
        }

        return syncChanged || replayWasActive;
    }

    private async Task EnsureDedicatedObsWorkspaceAsync(ObsWebSocketClient client)
    {
        if (!_settings.UseDedicatedObsWorkspace)
        {
            return;
        }

        var profile = await client.GetProfileListAsync();
        var sceneCollection = await client.GetSceneCollectionListAsync();
        if (string.Equals(profile.CurrentName, EmxObsProfileName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(sceneCollection.CurrentName, EmxObsSceneCollectionName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (await IsStreamingOrRecordingAsync(client))
        {
            throw new InvalidOperationException("Streamer safe setup stopped: OBS is currently streaming or recording, so EMX did not change profiles, scenes, or output settings. Stop the stream/recording first, or turn off Streamer safe OBS workspace in EMX settings if you want EMX to use the current OBS setup.");
        }

        throw new InvalidOperationException(
            $"Streamer safe setup needs OBS opened in the EMX Clips workspace. Close OBS, fully exit EMX Clips, then open EMX Clips again so it can launch OBS into its own profile and scene collection. EMX no longer forces profile switches while OBS is already open. Current OBS profile: {profile.CurrentName}. Current scene collection: {sceneCollection.CurrentName}. You can also turn off Streamer safe OBS workspace if you want EMX to use the current OBS setup.");
    }

    private static async Task<bool> IsStreamingOrRecordingAsync(ObsWebSocketClient client)
    {
        try
        {
            return await client.GetStreamActiveAsync() || await client.GetRecordActiveAsync();
        }
        catch
        {
            // If OBS cannot answer, prefer continuing for normal users instead of blocking setup.
            return false;
        }
    }

    private static async Task TrySetProfileParameterAsync(ObsWebSocketClient client, string category, string name, string value)
    {
        try
        {
            await client.SetProfileParameterAsync(category, name, value);
        }
        catch
        {
            // Some OBS profiles reject specific parameters; keep applying the rest.
        }
    }

    private static async Task<bool> EnsureDisplayCaptureAsync(ObsWebSocketClient client, bool createIfMissing, bool allowVideoSettingsChange)
    {
        var sceneName = await client.GetCurrentProgramSceneAsync();
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            throw new InvalidOperationException("EMX could not find the active OBS scene. Open OBS once, select your gameplay scene, then click Restart Buffer.");
        }

        var changed = false;
        var inputExists = await client.InputExistsAsync(DisplayCaptureInputName);
        if (!inputExists)
        {
            if (!createIfMissing)
            {
                return false;
            }

            var inputKind = await ResolveDisplayCaptureKindAsync(client);
            await client.CreateInputAsync(sceneName, DisplayCaptureInputName, inputKind, BuildDisplayCaptureSettings(null));
            changed = true;
        }
        else if (!await client.SceneItemExistsAsync(sceneName, DisplayCaptureInputName))
        {
            await client.CreateSceneItemAsync(sceneName, DisplayCaptureInputName);
            changed = true;
        }

        var target = await ResolveDisplayCaptureTargetAsync(client, DisplayCaptureInputName);
        await client.SetInputSettingsAsync(DisplayCaptureInputName, BuildDisplayCaptureSettings(target.MonitorId), overlay: false);
        await ConfigureFullscreenCaptureAsync(client, sceneName, DisplayCaptureInputName, target, allowVideoSettingsChange);
        return changed;
    }

    private async Task ApplySettingsAndStartBufferAsync()
    {
        TryLaunchObs();
        var client = await GetObsClientAsync();
        var liveOutputActive = await IsStreamingOrRecordingAsync(client);
        var wasActive = await client.GetReplayBufferActiveAsync();

        if (liveOutputActive)
        {
            if ((_settings.AutoStartReplayBuffer || wasActive) && !wasActive)
            {
                try
                {
                    await client.StartReplayBufferAsync();
                }
                catch (ObsRequestException ex)
                {
                    throw new InvalidOperationException(BuildLiveStartBufferHelp(ex), ex);
                }
            }

            SetDashboardStatus("Live Safe Mode: settings saved locally. EMX did not change the live OBS profile, scene, capture source, or video settings.");
            return;
        }

        if (wasActive)
        {
            await client.StopReplayBufferAsync();
        }

        await TrySyncObsSettingsAsync(client);

        if (_settings.AutoStartReplayBuffer || wasActive)
        {
            try
            {
                await client.StartReplayBufferAsync();
            }
            catch (ObsRequestException ex)
            {
                throw new InvalidOperationException(BuildStartBufferHelp(ex), ex);
            }

            SetDashboardStatus($"Settings saved. EMX is now buffering {_settings.ReplayBufferSeconds}-second clips in the background.");
        }
        else
        {
            SetDashboardStatus("Settings saved. Auto-start is off, so EMX will not capture past gameplay until the buffer is started.");
        }
    }

    private async Task EnsureBufferRunningInBackgroundAsync()
    {
        if (_watchdogBusy || !_settings.SetupCompleted || !_settings.AutoStartReplayBuffer)
        {
            return;
        }

        _watchdogBusy = true;
        try
        {
            TryLaunchObs();
            var client = await GetObsClientAsync();
            var captureChanged = await TrySyncObsSettingsAsync(client);

            if (captureChanged)
            {
                SetDashboardStatus("EMX refreshed OBS capture settings. Wait one clip length, then save a fresh clip.");
            }

            if (!await client.GetReplayBufferActiveAsync())
            {
                await client.StartReplayBufferAsync();
                SetDashboardStatus($"EMX restored background buffering for {_settings.ReplayBufferSeconds}-second clips.");
            }
        }
        catch
        {
            // Keep this quiet; the dashboard and manual actions show setup errors.
        }
        finally
        {
            _watchdogBusy = false;
        }
    }

    private async Task AutoSetupCaptureAsync()
    {
        TryLaunchObs();
        var client = await GetObsClientAsync();
        if (await IsStreamingOrRecordingAsync(client))
        {
            throw new InvalidOperationException("Live Safe Mode: Auto Setup Capture is locked while OBS is live. EMX can save clips from an already-running OBS replay buffer, but capture setup must be done after stream.");
        }

        var sceneName = await client.GetCurrentProgramSceneAsync();
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            throw new InvalidOperationException("EMX could not find the active OBS scene. Open OBS once, select your gameplay scene, then try Auto Setup Capture again.");
        }

        var wasActive = await client.GetReplayBufferActiveAsync();
        if (wasActive)
        {
            await client.StopReplayBufferAsync();
        }

        await EnsureDisplayCaptureAsync(client, createIfMissing: true, allowVideoSettingsChange: true);
        var target = await ResolveDisplayCaptureTargetAsync(client, DisplayCaptureInputName);

        var hasMonitorId = !string.IsNullOrWhiteSpace(target.MonitorId);

        await TrySyncObsSettingsAsync(client);
        if (_settings.AutoStartReplayBuffer || wasActive || !await client.GetReplayBufferActiveAsync())
        {
            await client.StartReplayBufferAsync();
        }

        ShowBalloon("Capture ready", $"Configured {DisplayCaptureInputName} in OBS scene '{sceneName}'.", ToolTipIcon.Info);
        SetDashboardStatus(hasMonitorId
            ? $"Capture ready: {target.Width}x{target.Height} full-screen canvas is set in OBS. Wait {_settings.ReplayBufferSeconds} seconds, then save a fresh clip."
            : $"Capture source added, but OBS did not expose a monitor id. If clips stay black, open OBS source properties for {DisplayCaptureInputName} and select your display.");
    }

    private async Task AutoSetupMicrophoneAsync()
    {
        TryLaunchObs();
        var client = await GetObsClientAsync();
        if (await IsStreamingOrRecordingAsync(client))
        {
            throw new InvalidOperationException("Live Safe Mode: Auto Setup Mic is locked while OBS is live so EMX does not change live audio sources. Set up mic capture after stream.");
        }

        await ApplyMicrophoneSettingsAsync(client, createIfMissing: true);

        if (_settings.CaptureMicrophone)
        {
            var wasActive = await client.GetReplayBufferActiveAsync();
            if (wasActive)
            {
                await client.StopReplayBufferAsync();
                await client.StartReplayBufferAsync();
            }
            else if (_settings.AutoStartReplayBuffer)
            {
                await client.StartReplayBufferAsync();
            }
        }

        var deviceName = string.IsNullOrWhiteSpace(_settings.MicrophoneDeviceName)
            ? "selected microphone"
            : _settings.MicrophoneDeviceName;
        var state = _settings.CaptureMicrophone ? "included in" : "muted for";
        ShowBalloon("Mic ready", $"OBS mic capture is set to {deviceName}.", ToolTipIcon.Info);
        SetDashboardStatus($"Mic ready: {deviceName} is {state} new clips. Wait {_settings.ReplayBufferSeconds} seconds, then save a fresh test clip.");
    }

    private async Task TryApplyMicrophoneSettingsAsync(ObsWebSocketClient client)
    {
        try
        {
            await ApplyMicrophoneSettingsAsync(client, createIfMissing: _settings.CaptureMicrophone);
        }
        catch
        {
            // Mic setup should not stop the replay buffer from starting.
        }
    }

    private async Task ApplyMicrophoneSettingsAsync(ObsWebSocketClient client, bool createIfMissing)
    {
        if (!_settings.CaptureMicrophone)
        {
            var mutedInput = await ResolveMicrophoneInputNameAsync(client);
            if (mutedInput is not null)
            {
                await client.SetInputMuteAsync(mutedInput, inputMuted: true);
            }

            return;
        }

        var inputName = await ResolveEmxMicrophoneInputAsync(client, createIfMissing);
        if (inputName is null)
        {
            return;
        }

        var deviceId = await ResolveObsMicrophoneDeviceIdAsync(client, inputName);
        await client.SetInputSettingsAsync(inputName, new
        {
            device_id = deviceId
        }, overlay: false);

        await client.SetInputVolumeAsync(inputName, inputVolumeMul: 1.0);
        await client.SetInputAudioTracksAsync(inputName, BuildEnabledAudioTracks());
        await client.SetInputMuteAsync(inputName, inputMuted: false);
    }

    private async Task<string?> ResolveEmxMicrophoneInputAsync(ObsWebSocketClient client, bool createIfMissing)
    {
        if (await client.InputExistsAsync(EmxMicInputName))
        {
            if (createIfMissing)
            {
                await EnsureSourceInCurrentSceneAsync(client, EmxMicInputName);
            }

            return EmxMicInputName;
        }

        if (!createIfMissing)
        {
            return await ResolveMicrophoneInputNameAsync(client);
        }

        var sceneName = await client.GetCurrentProgramSceneAsync();
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            throw new InvalidOperationException("EMX could not find the active OBS scene for mic setup.");
        }

        await client.CreateInputAsync(sceneName, EmxMicInputName, ObsMicInputKind, new
        {
            device_id = "default"
        });

        await EnsureSourceInCurrentSceneAsync(client, EmxMicInputName);
        return EmxMicInputName;
    }

    private static async Task EnsureSourceInCurrentSceneAsync(ObsWebSocketClient client, string sourceName)
    {
        var sceneName = await client.GetCurrentProgramSceneAsync();
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            return;
        }

        var sceneItemId = await client.GetSceneItemIdAsync(sceneName, sourceName);
        if (sceneItemId is null)
        {
            await client.CreateSceneItemAsync(sceneName, sourceName);
            sceneItemId = await client.GetSceneItemIdAsync(sceneName, sourceName);
        }

        if (sceneItemId is not null)
        {
            await client.SetSceneItemEnabledAsync(sceneName, sceneItemId.Value, sceneItemEnabled: true);
        }
    }

    private async Task<string?> ResolveMicrophoneInputNameAsync(ObsWebSocketClient client)
    {
        if (await client.InputExistsAsync(EmxMicInputName))
        {
            return EmxMicInputName;
        }

        if (await client.InputExistsAsync(DefaultObsMicInputName))
        {
            return DefaultObsMicInputName;
        }

        return null;
    }

    private string ResolveMicrophoneDeviceId()
    {
        if (string.IsNullOrWhiteSpace(_settings.MicrophoneDeviceId))
        {
            return "default";
        }

        return _settings.MicrophoneDeviceId;
    }

    private async Task<string> ResolveObsMicrophoneDeviceIdAsync(ObsWebSocketClient client, string inputName)
    {
        var requestedId = ResolveMicrophoneDeviceId();
        try
        {
            var items = await client.GetInputListPropertyItemsAsync(inputName, "device_id");
            var exact = items.FirstOrDefault(item =>
                item.Enabled &&
                string.Equals(item.Value, requestedId, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return exact.Value;
            }

            var requestedName = NormalizeDeviceName(_settings.MicrophoneDeviceName);
            if (!string.IsNullOrWhiteSpace(requestedName))
            {
                var byName = items.FirstOrDefault(item =>
                    item.Enabled &&
                    NormalizeDeviceName(item.Name).Contains(requestedName, StringComparison.OrdinalIgnoreCase));
                if (byName is not null)
                {
                    return byName.Value;
                }
            }

            var firstEnabled = items.FirstOrDefault(item => item.Enabled);
            if (string.Equals(requestedId, "default", StringComparison.OrdinalIgnoreCase) && firstEnabled is not null)
            {
                return firstEnabled.Value;
            }
        }
        catch
        {
            // If OBS refuses the property list, try the Windows endpoint id directly.
        }

        return requestedId;
    }

    private static IReadOnlyDictionary<string, bool> BuildEnabledAudioTracks() => new Dictionary<string, bool>
    {
        ["1"] = true,
        ["2"] = true,
        ["3"] = true,
        ["4"] = true,
        ["5"] = true,
        ["6"] = true
    };

    private static string NormalizeDeviceName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var chars = value
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray();
        return new string(chars);
    }

    private static Dictionary<string, object> BuildDisplayCaptureSettings(string? monitorId)
    {
        var settings = new Dictionary<string, object>
        {
            ["capture_cursor"] = true,
            ["cursor"] = true
        };

        if (!string.IsNullOrWhiteSpace(monitorId))
        {
            settings["monitor_id"] = monitorId;
        }

        return settings;
    }

    private static async Task<string?> TryGetLiveObsMonitorIdAsync(ObsWebSocketClient client, string inputName)
    {
        try
        {
            return await client.GetPreferredInputListPropertyValueAsync(inputName, "monitor_id");
        }
        catch (ObsRequestException)
        {
            return null;
        }
    }

    private static async Task<DisplayCaptureTarget> ResolveDisplayCaptureTargetAsync(ObsWebSocketClient client, string inputName)
    {
        try
        {
            var monitors = await client.GetInputListPropertyItemsAsync(inputName, "monitor_id");
            var preferred = monitors.FirstOrDefault(monitor =>
                    monitor.Enabled &&
                    monitor.Name.Contains("Primary", StringComparison.OrdinalIgnoreCase)) ??
                monitors.FirstOrDefault(monitor => monitor.Enabled);

            if (preferred is not null)
            {
                var size = TryParseMonitorSize(preferred.Name) ?? GetPrimaryScreenSize();
                return new DisplayCaptureTarget(preferred.Value, size.Width, size.Height);
            }
        }
        catch (ObsRequestException)
        {
            // Older OBS builds can refuse this property list until the source exists.
        }

        var fallbackSize = GetPrimaryScreenSize();
        return new DisplayCaptureTarget(FindKnownObsMonitorId(), fallbackSize.Width, fallbackSize.Height);
    }

    private static async Task ConfigureFullscreenCaptureAsync(ObsWebSocketClient client, string sceneName, string sourceName, DisplayCaptureTarget target, bool allowVideoSettingsChange)
    {
        if (allowVideoSettingsChange)
        {
            await client.SetVideoSettingsAsync(
                target.Width,
                target.Height,
                target.Width,
                target.Height,
                DefaultCaptureFps,
                1);
        }

        var sceneItemId = await client.GetSceneItemIdAsync(sceneName, sourceName);
        if (sceneItemId is null)
        {
            await client.CreateSceneItemAsync(sceneName, sourceName);
            sceneItemId = await client.GetSceneItemIdAsync(sceneName, sourceName);
        }

        if (sceneItemId is null)
        {
            return;
        }

        await client.SetSceneItemEnabledAsync(sceneName, sceneItemId.Value, sceneItemEnabled: true);
        await client.SetSceneItemTransformAsync(sceneName, sceneItemId.Value, new
        {
            alignment = 5,
            boundsAlignment = 5,
            boundsType = "OBS_BOUNDS_STRETCH",
            boundsWidth = target.Width,
            boundsHeight = target.Height,
            cropBottom = 0,
            cropLeft = 0,
            cropRight = 0,
            cropTop = 0,
            positionX = 0.0,
            positionY = 0.0,
            rotation = 0.0,
            scaleX = 1.0,
            scaleY = 1.0
        });
    }

    private static Size? TryParseMonitorSize(string text)
    {
        var match = Regex.Match(text, @"(?<width>\d{3,5})\s*x\s*(?<height>\d{3,5})");
        if (!match.Success ||
            !int.TryParse(match.Groups["width"].Value, out var width) ||
            !int.TryParse(match.Groups["height"].Value, out var height))
        {
            return null;
        }

        return NormalizeCaptureSize(width, height);
    }

    private static Size GetPrimaryScreenSize()
    {
        var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        return NormalizeCaptureSize(bounds.Width, bounds.Height);
    }

    private static Size NormalizeCaptureSize(int width, int height)
    {
        const int maxObsDimension = 4096;
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        if (width <= maxObsDimension && height <= maxObsDimension)
        {
            return new Size(width, height);
        }

        var scale = Math.Min(maxObsDimension / (double)width, maxObsDimension / (double)height);
        return new Size(
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static string? FindKnownObsMonitorId()
    {
        var scenesDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "obs-studio",
            "basic",
            "scenes");

        if (!Directory.Exists(scenesDirectory))
        {
            return null;
        }

        foreach (var path in Directory.EnumerateFiles(scenesDirectory, "*.json*", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var document = JsonDocument.Parse(stream);
                var monitorId = FindMonitorId(document.RootElement);
                if (!string.IsNullOrWhiteSpace(monitorId))
                {
                    return monitorId;
                }
            }
            catch
            {
                // OBS can leave temporary backup files around; ignore files that are not valid scene JSON.
            }
        }

        return null;
    }

    private static string? FindMonitorId(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (IsMonitorCaptureSource(element) &&
                element.TryGetProperty("settings", out var settings) &&
                settings.TryGetProperty("monitor_id", out var monitorId) &&
                monitorId.ValueKind == JsonValueKind.String)
            {
                return monitorId.GetString();
            }

            foreach (var property in element.EnumerateObject())
            {
                var result = FindMonitorId(property.Value);
                if (!string.IsNullOrWhiteSpace(result))
                {
                    return result;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var result = FindMonitorId(item);
                if (!string.IsNullOrWhiteSpace(result))
                {
                    return result;
                }
            }
        }

        return null;
    }

    private static bool IsMonitorCaptureSource(JsonElement element) =>
        HasStringProperty(element, "id", "monitor_capture") ||
        HasStringProperty(element, "versioned_id", "monitor_capture");

    private static bool HasStringProperty(JsonElement element, string propertyName, string expectedValue) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String &&
        string.Equals(property.GetString(), expectedValue, StringComparison.OrdinalIgnoreCase);

    private static async Task<string> ResolveDisplayCaptureKindAsync(ObsWebSocketClient client)
    {
        var kinds = await client.GetInputKindListAsync();
        var preferred = new[] { "monitor_capture", "display_capture" };
        var match = preferred.FirstOrDefault(kind => kinds.Contains(kind, StringComparer.OrdinalIgnoreCase));

        if (match is not null)
        {
            return match;
        }

        var displayLike = kinds.FirstOrDefault(kind =>
            kind.Contains("monitor", StringComparison.OrdinalIgnoreCase) ||
            kind.Contains("display", StringComparison.OrdinalIgnoreCase));

        if (displayLike is not null)
        {
            return displayLike;
        }

        throw new InvalidOperationException("OBS did not report a Display Capture source type. Add a Display Capture source manually in OBS, or send me this error and I will add a game-capture fallback.");
    }

    private string BuildStartBufferHelp(ObsRequestException ex)
    {
        return $"OBS rejected starting the replay buffer. Close OBS and reopen EMX Clips so EMX can launch OBS into the cloned EMX Clips workspace. If OBS still shows Starting the output failed, open OBS Settings > Output and choose a working recording encoder, or update your GPU drivers for NVENC/AMD. EMX tried to enable a {_settings.ReplayBufferSeconds}-second buffer.\n\nOBS detail: {ex.Message}";
    }

    private string BuildLiveStartBufferHelp(ObsRequestException ex)
    {
        return $"OBS is live, so EMX stayed in Live Safe Mode and did not change the stream profile, scene, capture source, encoder, or video settings. OBS rejected starting replay buffer with its current live setup. Have your friend enable and test Replay Buffer in OBS before going live, then EMX can save clips while live.\n\nOBS detail: {ex.Message}";
    }

    private string BuildSaveClipHelp(ObsRequestException ex)
    {
        return $"OBS rejected saving the clip. If EMX just started, wait up to {_settings.ReplayBufferSeconds} seconds for the buffer to fill, then press the hotkey once. If this keeps happening, click Restart Buffer.\n\nOBS detail: {ex.Message}";
    }

    private void TryLaunchObs()
    {
        var obsPath = ObsTools.ResolveObsPath(_settings.ObsPath);
        if (obsPath is null || Process.GetProcessesByName("obs64").Any())
        {
            return;
        }

        var args = BuildObsLaunchArguments();
        Process.Start(new ProcessStartInfo
        {
            FileName = obsPath,
            Arguments = args,
            WorkingDirectory = Path.GetDirectoryName(obsPath) ?? Environment.CurrentDirectory,
            UseShellExecute = false
        });

        Thread.Sleep(2500);
    }

    private string BuildObsLaunchArguments()
    {
        var args = new List<string>();
        if (_settings.MinimizeObsToTray)
        {
            args.Add("--minimize-to-tray");
        }

        if (!_settings.UseDedicatedObsWorkspace)
        {
            return string.Join(' ', args);
        }

        var workspace = TryPrepareDedicatedObsWorkspace();
        if (workspace.Prepared)
        {
            args.Add($"--profile {QuoteCommandArgument(EmxObsProfileName)}");
            args.Add($"--collection {QuoteCommandArgument(EmxObsSceneCollectionName)}");
            SetDashboardStatus("OBS launching in the EMX Clips workspace so stream scenes stay separate.");
        }
        else if (!string.IsNullOrWhiteSpace(workspace.Message))
        {
            SetDashboardStatus(workspace.Message);
        }

        return string.Join(' ', args);
    }

    private (bool Prepared, string? Message) TryPrepareDedicatedObsWorkspace()
    {
        try
        {
            var obsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio");
            var globalIniPath = Path.Combine(obsRoot, "global.ini");
            if (!File.Exists(globalIniPath))
            {
                return (false, "OBS first-time setup is not finished yet. Open OBS once, then reopen EMX Clips.");
            }

            var basic = ReadIniSection(globalIniPath, "Basic");
            if (!basic.TryGetValue("ProfileDir", out var sourceProfileDirectory) ||
                string.IsNullOrWhiteSpace(sourceProfileDirectory))
            {
                return (false, "EMX could not read the current OBS profile folder. Open OBS once, close it, then reopen EMX Clips.");
            }

            if (!basic.TryGetValue("SceneCollectionFile", out var sourceSceneCollectionFile) ||
                string.IsNullOrWhiteSpace(sourceSceneCollectionFile))
            {
                return (false, "EMX could not read the current OBS scene collection. Open OBS once, close it, then reopen EMX Clips.");
            }

            var profilesRoot = Path.Combine(obsRoot, "basic", "profiles");
            var sourceProfilePath = Path.Combine(profilesRoot, sourceProfileDirectory);
            var targetProfilePath = Path.Combine(profilesRoot, EmxObsProfileDirectoryName);
            if (!Directory.Exists(sourceProfilePath) && !Directory.Exists(targetProfilePath))
            {
                return (false, "EMX could not find an OBS profile to clone. Create or finish an OBS profile first, then reopen EMX Clips.");
            }

            if (Directory.Exists(sourceProfilePath) && !SamePath(sourceProfilePath, targetProfilePath))
            {
                CopyDirectory(sourceProfilePath, targetProfilePath, overwrite: true);
            }
            else
            {
                Directory.CreateDirectory(targetProfilePath);
            }

            UpsertIniValue(Path.Combine(targetProfilePath, "basic.ini"), "General", "Name", EmxObsProfileName);

            var scenesRoot = Path.Combine(obsRoot, "basic", "scenes");
            var sourceScenePath = Path.Combine(scenesRoot, $"{sourceSceneCollectionFile}.json");
            var targetScenePath = Path.Combine(scenesRoot, $"{EmxObsSceneCollectionFileName}.json");
            if (File.Exists(sourceScenePath) && !SamePath(sourceScenePath, targetScenePath))
            {
                Directory.CreateDirectory(scenesRoot);
                File.Copy(sourceScenePath, targetScenePath, overwrite: true);
            }
            else if (!File.Exists(targetScenePath))
            {
                return (false, "EMX could not find an OBS scene collection to clone. Create one in OBS first, then reopen EMX Clips.");
            }

            UpdateSceneCollectionName(targetScenePath, EmxObsSceneCollectionName);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"EMX could not prepare the OBS workspace: {ex.Message}");
        }
    }

    private static Dictionary<string, string> ReadIniSection(string path, string sectionName)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inSection = false;
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inSection = string.Equals(line[1..^1], sectionName, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inSection)
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return values;
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory, bool overwrite)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
            var targetPath = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? targetDirectory);
            File.Copy(sourcePath, targetPath, overwrite);
        }
    }

    private static void UpsertIniValue(string path, string sectionName, string key, string value)
    {
        var lines = File.Exists(path)
            ? File.ReadAllLines(path).ToList()
            : new List<string>();
        var sectionLine = -1;
        var insertLine = lines.Count;
        var inSection = false;

        for (var index = 0; index < lines.Count; index++)
        {
            var trimmed = lines[index].Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                if (inSection)
                {
                    insertLine = index;
                    break;
                }

                inSection = string.Equals(trimmed[1..^1], sectionName, StringComparison.OrdinalIgnoreCase);
                if (inSection)
                {
                    sectionLine = index;
                    insertLine = index + 1;
                }

                continue;
            }

            if (!inSection)
            {
                continue;
            }

            var separator = trimmed.IndexOf('=');
            if (separator > 0 && string.Equals(trimmed[..separator].Trim(), key, StringComparison.OrdinalIgnoreCase))
            {
                lines[index] = $"{key}={value}";
                File.WriteAllLines(path, lines);
                return;
            }

            insertLine = index + 1;
        }

        if (sectionLine < 0)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
            {
                lines.Add("");
            }

            lines.Add($"[{sectionName}]");
            lines.Add($"{key}={value}");
        }
        else
        {
            lines.Insert(insertLine, $"{key}={value}");
        }

        File.WriteAllLines(path, lines);
    }

    private static void UpdateSceneCollectionName(string path, string name)
    {
        var json = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        if (json is null)
        {
            return;
        }

        json["name"] = name;
        File.WriteAllText(path, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static bool SamePath(string left, string right)
    {
        var normalizedLeft = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRight = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static string QuoteCommandArgument(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private void OpenClipsFolder()
    {
        Directory.CreateDirectory(_settings.ClipsFolder);
        Process.Start(new ProcessStartInfo
        {
            FileName = _settings.ClipsFolder,
            UseShellExecute = true
        });
    }

    private async void OpenPhoneCompanion()
    {
        try
        {
            if (FirebaseRemoteShare.IsConfigured(_settings))
            {
                if (!_settings.UseFirebaseCloudShare)
                {
                    _settings.UseFirebaseCloudShare = true;
                    _settings.Save();
                }

                await OpenFirebaseCloudCompanionAsync().ConfigureAwait(true);
                return;
            }

            _phoneCompanionServer ??= new PhoneCompanionServer(_settings);
            var localPortalUrl = _phoneCompanionServer.Start();
            var companionUrl = BuildHostedCompanionUrl(localPortalUrl);

            if (_phoneCompanionForm is null || _phoneCompanionForm.IsDisposed)
            {
                _phoneCompanionForm = new PhoneCompanionForm(companionUrl, localPortalUrl, _icon);
                _phoneCompanionForm.FormClosed += (_, _) => _phoneCompanionForm = null;
            }

            _phoneCompanionForm.Show();
            _phoneCompanionForm.Activate();
            Clipboard.SetText(companionUrl);
            ShowBalloon("Phone companion ready", "Local fallback link copied. Firebase Remote Share is not configured.", ToolTipIcon.Info);
            SetDashboardStatus("Phone companion local fallback ready. Add Firebase API key and Realtime Database URL to use in-app Vercel clip viewing.");
        }
        catch (Exception ex)
        {
            var message = $"Could not start phone companion: {ex.Message}";
            ShowBalloon("EMX Clips", message, ToolTipIcon.Error);
            SetDashboardStatus(message);
        }
    }

    private async Task OpenFirebaseCloudCompanionAsync()
    {
        if (!FirebaseRemoteShare.IsConfigured(_settings))
        {
            var setupMessage = "Firebase Remote Share needs API key and Realtime Database URL in Settings.";
            ShowBalloon("EMX Clips", setupMessage, ToolTipIcon.Warning);
            SetDashboardStatus(setupMessage);
            OpenDashboard();
            return;
        }

        SetDashboardStatus("Starting EMX Remote Share tunnel and publishing Firebase session...");
        ShowBalloon("Remote Share", "Starting secure tunnel. Keep EMX Clips open while your phone views clips.", ToolTipIcon.Info);

        _phoneCompanionServer ??= new PhoneCompanionServer(_settings);
        _firebaseRemoteShare ??= new FirebaseRemoteShare(_settings, _phoneCompanionServer);
        var result = await _firebaseRemoteShare.StartAsync().ConfigureAwait(true);
        if (_phoneCompanionForm is null || _phoneCompanionForm.IsDisposed)
        {
            _phoneCompanionForm = new PhoneCompanionForm(result.CompanionUrl, result.TunnelUrl, _icon);
            _phoneCompanionForm.FormClosed += (_, _) => _phoneCompanionForm = null;
        }

        _phoneCompanionForm.Show();
        _phoneCompanionForm.Activate();
        Clipboard.SetText(result.CompanionUrl);
        ShowBalloon("Remote Share ready", $"Session live with {result.ClipCount} clip(s). Scan QR to view clips in the Vercel app.", ToolTipIcon.Info);
        SetDashboardStatus($"Firebase Remote Share ready: {result.ClipCount} clip(s) available through {result.TunnelUrl}. QR link copied.");
    }

    private static string BuildHostedCompanionUrl(string localPortalUrl)
    {
        var builder = new UriBuilder(HostedCompanionUrl);
        builder.Query = "portal=" + Uri.EscapeDataString(localPortalUrl);
        return builder.Uri.ToString();
    }

    private void OpenLatestClip()
    {
        var clip = LatestClip();
        if (clip is null)
        {
            ShowBalloon("EMX Clips", "No clips saved yet.", ToolTipIcon.Info);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = clip.FullPath,
            UseShellExecute = true
        });
    }

    private void CopyLatestClip()
    {
        var clip = LatestClip();
        if (clip is null)
        {
            ShowBalloon("EMX Clips", "No clips saved yet.", ToolTipIcon.Info);
            return;
        }

        var files = new StringCollection { clip.FullPath };
        Clipboard.SetFileDropList(files);
        ShowBalloon("Clip copied", "Paste it into Discord, folders, or an editor import window.", ToolTipIcon.Info);
    }

    private ClipFile? LatestClip() =>
        ClipLibrary.Load(_settings.ClipsFolder).FirstOrDefault();

    private void OpenDashboard()
    {
        if (_dashboard is { IsDisposed: false })
        {
            _dashboard.ShowInTaskbar = true;
            if (_dashboard.WindowState == FormWindowState.Minimized)
            {
                _dashboard.WindowState = FormWindowState.Normal;
            }

            _dashboard.Show();
            _dashboard.Activate();
            return;
        }

        _dashboard = new DashboardForm(_settings, _icon);
        _dashboard.SaveClipRequested += (_, _) => SaveClipFromUi();
        _dashboard.StartReplayBufferRequested += (_, _) => RunUiTask(RestartReplayBufferAsync);
        _dashboard.StopReplayBufferRequested += (_, _) => RunUiTask(StopReplayBufferAsync);
        _dashboard.AutoSetupCaptureRequested += (_, _) => RunUiTask(AutoSetupCaptureAsync);
        _dashboard.AutoSetupMicRequested += (_, _) => RunUiTask(AutoSetupMicrophoneAsync);
        _dashboard.PhoneCompanionRequested += (_, _) => OpenPhoneCompanion();
        _dashboard.InstallObsRequested += (_, _) => RunUiTask(InstallObsAsync);
        _dashboard.CheckObsStatusRequested += (_, _) => RunUiTask(CheckObsStatusAsync);
        _dashboard.CheckUpdatesRequested += (_, _) => RunUiTask(CheckForUpdatesAsync);
        _dashboard.HideToTrayRequested += (_, _) => HideDashboardToTray();
        _dashboard.SettingsSaved += (_, _) =>
        {
            ResetObsClient();
            RegisterHotkeys();
            RunUiTask(ApplySettingsAndStartBufferAsync);
            ShowBalloon("Settings saved", "EMX Clips settings were updated.", ToolTipIcon.Info);
        };
        _dashboard.FormClosed += (_, _) => _dashboard = null;
        _dashboard.Show();
    }

    private async Task InstallObsAsync()
    {
        SetDashboardStatus("Installing OBS...");
        await ObsTools.InstallObsAsync();
        SetDashboardStatus(ObsTools.ResolveObsPath(_settings.ObsPath) is null
            ? "OBS installer opened. Finish installing OBS, then save EMX settings."
            : "OBS available. Save EMX settings, then Auto Setup Capture.");
    }

    private async Task CheckObsStatusAsync()
    {
        if (ObsTools.ResolveObsPath(_settings.ObsPath) is null)
        {
            SetDashboardObsStatus(
                "OBS not installed",
                "Install OBS first, then open OBS once in Normal Mode and enable Tools > WebSocket Server Settings.",
                EmxTheme.MagentaGlow);
            return;
        }

        try
        {
            var client = await GetObsClientAsync();
            var streamActive = await client.GetStreamActiveAsync();
            var recordActive = await client.GetRecordActiveAsync();
            var replayActive = await client.GetReplayBufferActiveAsync();
            var liveSafeMode = streamActive || recordActive;
            var sceneName = await client.GetCurrentProgramSceneAsync();
            var hasCapture = !string.IsNullOrWhiteSpace(sceneName) &&
                await client.InputExistsAsync(DisplayCaptureInputName) &&
                await client.SceneItemExistsAsync(sceneName, DisplayCaptureInputName);

            if (liveSafeMode && replayActive)
            {
                SetDashboardObsStatus(
                    "Live Safe ready",
                    "OBS is live/recording and replay buffer is active. EMX can save clips without changing the live profile, scene, capture source, or video settings.",
                    EmxTheme.GreenGlow);
                return;
            }

            if (liveSafeMode)
            {
                SetDashboardObsStatus(
                    "Live Safe: replay off",
                    "OBS is live/recording but replay buffer is off. Click Restart Buffer once. If OBS rejects it, wait until stream ends, then enable Replay Buffer in OBS Settings > Output and run Auto Setup Capture.",
                    EmxTheme.MagentaGlow);
                return;
            }

            if (replayActive && hasCapture)
            {
                SetDashboardObsStatus(
                    "Ready to clip",
                    "OBS is connected, replay buffer is active, and EMX Display Capture is in the active scene. Minimize to tray and press your clip hotkey.",
                    EmxTheme.GreenGlow);
                return;
            }

            if (replayActive)
            {
                SetDashboardObsStatus(
                    "Buffer on, setup needed",
                    "Replay buffer is active, but EMX Display Capture is not in the active scene. Click Auto Setup Capture while not live, then wait one clip length before testing.",
                    EmxTheme.MagentaGlow);
                return;
            }

            SetDashboardObsStatus(
                "Replay buffer off",
                hasCapture
                    ? "Capture is set up, but replay buffer is off. Click Restart Buffer, wait one clip length, then press the clip hotkey."
                    : "Click Auto Setup Capture while not live, then click Restart Buffer. For live users, set this up before going live.",
                EmxTheme.MagentaGlow);
        }
        catch (Exception ex)
        {
            SetDashboardObsStatus(
                "OBS not connected",
                $"{BuildObsConnectionHelp(ex.Message)} After it connects, click Check OBS again.",
                EmxTheme.MagentaGlow);
        }
    }

    private void SetDashboardObsStatus(string headline, string detail, Color color)
    {
        if (_dashboard is null || _dashboard.IsDisposed)
        {
            return;
        }

        _dashboard.SetObsStatus(headline, detail, color);
    }

    private async Task CheckForUpdatesAsync()
    {
        SetDashboardStatus("Checking for EMX Clips updates...");
        var result = await UpdateService.CheckAsync(_settings.UpdateManifestUrl);
        if (!result.IsUpdateAvailable)
        {
            ShowBalloon("EMX Clips", $"You are up to date on v{result.CurrentVersion}.", ToolTipIcon.Info);
            SetDashboardStatus($"EMX Clips is up to date: v{result.CurrentVersion}.");
            return;
        }

        var message = $"EMX Clips v{result.LatestVersion} is available. Download and install it now?";
        var install = MessageBox.Show(message, "EMX Clips Update", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (install != DialogResult.Yes)
        {
            UpdateService.OpenReleaseNotes(result.Manifest);
            SetDashboardStatus($"Update available: v{result.LatestVersion}. Release page opened.");
            return;
        }

        var progress = new Progress<string>(SetDashboardStatus);
        var downloadedPath = await UpdateService.DownloadUpdateAsync(result.Manifest, progress);
        SetDashboardStatus("Installing update and restarting EMX Clips...");
        UpdateService.ApplyDownloadedUpdateAndRestart(downloadedPath);
    }

    private void ToggleDashboard()
    {
        if (_dashboard is null || _dashboard.IsDisposed || !_dashboard.Visible || _dashboard.WindowState == FormWindowState.Minimized)
        {
            OpenDashboard();
            return;
        }

        HideDashboardToTray();
    }

    private void HideDashboardToTray()
    {
        if (_dashboard is null || _dashboard.IsDisposed)
        {
            return;
        }

        _dashboard.Hide();
        _dashboard.ShowInTaskbar = false;
        ShowBalloon("EMX Clips is running", $"Press {HotkeyText.Format(_settings.ToggleDashboardHotkeyKey, _settings.ToggleDashboardHotkeyModifiers)} to show/hide the GUI.", ToolTipIcon.Info);
    }

    private void SetDashboardStatus(string message)
    {
        if (_dashboard is null || _dashboard.IsDisposed)
        {
            return;
        }

        _dashboard.SetStatus(message);
    }

    private void RefreshDashboardClips()
    {
        if (_dashboard is null || _dashboard.IsDisposed)
        {
            return;
        }

        if (_dashboard.InvokeRequired)
        {
            _dashboard.BeginInvoke(() => _dashboard.RefreshClips());
            return;
        }

        _dashboard.RefreshClips();
    }

    private void ShowBalloon(string title, string message, ToolTipIcon icon)
    {
        if (_uiInvoker.InvokeRequired)
        {
            _uiInvoker.BeginInvoke(() => ShowBalloon(title, message, icon));
            return;
        }

        try
        {
            _notifyIcon.ShowBalloonTip(3500, title, message, icon);
        }
        catch
        {
            // Ignore notification failures from shell restarts or disabled notifications.
        }
    }

    protected override void ExitThreadCore()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _bufferWatchdog.Dispose();
        _uiInvoker.Dispose();
        _clipHotkeyWindow.Dispose();
        _toggleHotkeyWindow.Dispose();
        _icon.Dispose();
        _phoneCompanionForm?.Dispose();
        _firebaseRemoteShare?.Dispose();
        _phoneCompanionServer?.Dispose();
        _obsClient?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.ExitThreadCore();
    }

    private static Icon CreateEmxIcon()
    {
        try
        {
            var associatedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (associatedIcon is not null)
            {
                return associatedIcon;
            }
        }
        catch
        {
            // Fall back to a generated icon when running under unusual hosts.
        }

        using var bitmap = new Bitmap(64, 64);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(13, 16, 20));
        using var border = new Pen(Color.FromArgb(50, 220, 130), 4);
        graphics.DrawRectangle(border, 5, 5, 54, 54);

        using var font = new Font("Segoe UI", 17, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.White);
        var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        graphics.DrawString("EMX", font, brush, new RectangleF(0, 0, 64, 64), format);

        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
