using System.Diagnostics;
using System.Collections.Specialized;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace EmxClips;

public sealed class TrayAppContext : ApplicationContext
{
    private const string DisplayCaptureInputName = "EMX Display Capture";

    private readonly AppSettings _settings;
    private readonly Control _uiInvoker;
    private readonly NotifyIcon _notifyIcon;
    private readonly HotkeyWindow _clipHotkeyWindow;
    private readonly HotkeyWindow _toggleHotkeyWindow;
    private readonly Icon _icon;
    private readonly System.Threading.Timer _bufferWatchdog;
    private ObsWebSocketClient? _obsClient;
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
        await TrySyncObsSettingsAsync(client);

        if (!await client.GetReplayBufferActiveAsync())
        {
            try
            {
                await client.StartReplayBufferAsync();
            }
            catch (ObsRequestException ex)
            {
                throw new InvalidOperationException(BuildStartBufferHelp(ex), ex);
            }
        }

        if (showNotification)
        {
            ShowBalloon("Replay buffer on", $"EMX is capturing the last {_settings.ReplayBufferSeconds} seconds in memory.", ToolTipIcon.Info);
        }

        SetDashboardStatus($"Replay buffer on. Press Save Clip once to save the last {_settings.ReplayBufferSeconds} seconds.");
    }

    private async Task RestartReplayBufferAsync()
    {
        var client = await GetObsClientAsync();
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
        if (!await client.GetReplayBufferActiveAsync())
        {
            await TrySyncObsSettingsAsync(client);
            try
            {
                await client.StartReplayBufferAsync();
            }
            catch (ObsRequestException ex)
            {
                throw new InvalidOperationException(BuildStartBufferHelp(ex), ex);
            }
            ShowBalloon("Replay buffer started", $"EMX started the background buffer. It needs up to {_settings.ReplayBufferSeconds} seconds to fill.", ToolTipIcon.Info);
            SetDashboardStatus($"Background buffer was off. EMX started it now; future hotkey presses save the past {_settings.ReplayBufferSeconds} seconds.");
            return;
        }

        try
        {
            await client.SaveReplayBufferAsync();
        }
        catch (ObsRequestException ex)
        {
            throw new InvalidOperationException(BuildSaveClipHelp(ex), ex);
        }
        ShowBalloon("Clip saved", $"Saved the last {_settings.ReplayBufferSeconds} seconds.", ToolTipIcon.Info);
        SetDashboardStatus($"Clip saved: last {_settings.ReplayBufferSeconds} seconds.");
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000);
            RefreshDashboardClips();
        });
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

    private async Task TrySyncObsSettingsAsync(ObsWebSocketClient client)
    {
        Directory.CreateDirectory(_settings.ClipsFolder);

        await TrySetProfileParameterAsync(client, "SimpleOutput", "FilePath", _settings.ClipsFolder);
        await TrySetProfileParameterAsync(client, "SimpleOutput", "RecRB", "true");
        await TrySetProfileParameterAsync(client, "SimpleOutput", "RecRBTime", _settings.ReplayBufferSeconds.ToString());
        await TrySetProfileParameterAsync(client, "SimpleOutput", "RecRBSize", _settings.ReplayBufferMemoryMb.ToString());
        await TrySetProfileParameterAsync(client, "AdvOut", "RecFilePath", _settings.ClipsFolder);
        await TrySetProfileParameterAsync(client, "AdvOut", "RecRB", "true");
        await TrySetProfileParameterAsync(client, "AdvOut", "RecRBTime", _settings.ReplayBufferSeconds.ToString());
        await TrySetProfileParameterAsync(client, "AdvOut", "RecRBSize", _settings.ReplayBufferMemoryMb.ToString());
        await TryRepairExistingDisplayCaptureAsync(client);
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

    private static async Task TryRepairExistingDisplayCaptureAsync(ObsWebSocketClient client)
    {
        try
        {
            if (!await client.InputExistsAsync(DisplayCaptureInputName))
            {
                return;
            }

            var monitorId = await TryGetLiveObsMonitorIdAsync(client, DisplayCaptureInputName) ?? FindKnownObsMonitorId();
            await client.SetInputSettingsAsync(DisplayCaptureInputName, BuildDisplayCaptureSettings(monitorId), overlay: false);
        }
        catch
        {
            // Replay buffer settings should still apply even if OBS refuses source repair.
        }
    }

    private async Task ApplySettingsAndStartBufferAsync()
    {
        TryLaunchObs();
        var client = await GetObsClientAsync();
        var wasActive = await client.GetReplayBufferActiveAsync();

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
            await TrySyncObsSettingsAsync(client);

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
        var sceneName = await client.GetCurrentProgramSceneAsync();
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            throw new InvalidOperationException("EMX could not find the active OBS scene. Open OBS once, select your gameplay scene, then try Auto Setup Capture again.");
        }

        var inputExists = await client.InputExistsAsync(DisplayCaptureInputName);

        if (!inputExists)
        {
            var inputKind = await ResolveDisplayCaptureKindAsync(client);
            await client.CreateInputAsync(sceneName, DisplayCaptureInputName, inputKind, BuildDisplayCaptureSettings(null));
        }

        var monitorId = await TryGetLiveObsMonitorIdAsync(client, DisplayCaptureInputName) ?? FindKnownObsMonitorId();
        await client.SetInputSettingsAsync(DisplayCaptureInputName, BuildDisplayCaptureSettings(monitorId), overlay: false);

        var hasMonitorId = !string.IsNullOrWhiteSpace(monitorId);

        await TrySyncObsSettingsAsync(client);
        if (!await client.GetReplayBufferActiveAsync())
        {
            await client.StartReplayBufferAsync();
        }

        ShowBalloon("Capture ready", $"Configured {DisplayCaptureInputName} in OBS scene '{sceneName}'.", ToolTipIcon.Info);
        SetDashboardStatus(hasMonitorId
            ? $"Capture ready: {DisplayCaptureInputName} is locked to your display in OBS scene '{sceneName}'. Wait {_settings.ReplayBufferSeconds} seconds, then press your hotkey."
            : $"Capture source added, but OBS did not expose a monitor id. If clips stay black, open OBS source properties for {DisplayCaptureInputName} and select your display.");
    }

    private async Task AutoSetupMicrophoneAsync()
    {
        TryLaunchObs();
        var client = await GetObsClientAsync();
        var inputName = await client.InputExistsAsync("Mic/Aux") ? "Mic/Aux" : "EMX Mic Capture";

        if (!await client.InputExistsAsync(inputName))
        {
            var sceneName = await client.GetCurrentProgramSceneAsync();
            await client.CreateInputAsync(sceneName, inputName, "wasapi_input_capture", new
            {
                device_id = "default"
            });
        }

        var deviceId = await TryGetPreferredMicDeviceIdAsync(client, inputName) ?? "default";
        await client.SetInputSettingsAsync(inputName, new
        {
            device_id = deviceId
        }, overlay: false);

        ShowBalloon("Mic ready", $"OBS mic capture is set to {inputName}.", ToolTipIcon.Info);
        SetDashboardStatus($"Mic ready: {inputName} is using your default input device for voice/keyboard sounds.");
    }

    private static async Task<string?> TryGetPreferredMicDeviceIdAsync(ObsWebSocketClient client, string inputName)
    {
        try
        {
            return await client.GetPreferredInputListPropertyValueAsync(inputName, "device_id");
        }
        catch (ObsRequestException)
        {
            return null;
        }
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
        return $"OBS rejected starting the replay buffer. In OBS, open Settings > Output and make sure Replay Buffer is enabled. Also make sure your current OBS scene has a Game Capture, Display Capture, or Window Capture source. EMX tried to enable a {_settings.ReplayBufferSeconds}-second buffer.\n\nOBS detail: {ex.Message}";
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

        var args = _settings.MinimizeObsToTray ? "--minimize-to-tray" : "";
        Process.Start(new ProcessStartInfo
        {
            FileName = obsPath,
            Arguments = args,
            WorkingDirectory = Path.GetDirectoryName(obsPath) ?? Environment.CurrentDirectory,
            UseShellExecute = false
        });

        Thread.Sleep(2500);
    }

    private void OpenClipsFolder()
    {
        Directory.CreateDirectory(_settings.ClipsFolder);
        Process.Start(new ProcessStartInfo
        {
            FileName = _settings.ClipsFolder,
            UseShellExecute = true
        });
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
        _dashboard.InstallObsRequested += (_, _) => RunUiTask(InstallObsAsync);
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
