using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EmxClips;

public sealed record ObsPropertyListItem(string Name, string Value, bool Enabled);
public sealed record ObsNameList(string CurrentName, IReadOnlyList<string> Names);

public sealed class ObsWebSocketClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null
    };

    private readonly string _host;
    private readonly int _port;
    private readonly string _password;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private ClientWebSocket? _socket;

    public ObsWebSocketClient(string host, int port, string password)
    {
        _host = host;
        _port = port;
        _password = password;
    }

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            return;
        }

        await DisposeSocketAsync().ConfigureAwait(false);

        _socket = new ClientWebSocket();
        await _socket.ConnectAsync(new Uri($"ws://{_host}:{_port}"), cancellationToken).ConfigureAwait(false);

        using var hello = await ReceiveJsonAsync(cancellationToken).ConfigureAwait(false);
        var root = hello.RootElement;
        var op = root.GetProperty("op").GetInt32();
        if (op != 0)
        {
            throw new InvalidOperationException("OBS did not send the expected websocket hello message.");
        }

        object identifyPayload;
        if (TryGetAuthentication(root, out var salt, out var challenge))
        {
            if (string.IsNullOrWhiteSpace(_password))
            {
                throw new InvalidOperationException("OBS websocket requires a password. Add it in EMX Clips settings.");
            }

            identifyPayload = new
            {
                op = 1,
                d = new
                {
                    rpcVersion = 1,
                    authentication = ComputeAuthentication(_password, salt, challenge)
                }
            };
        }
        else
        {
            identifyPayload = new
            {
                op = 1,
                d = new
                {
                    rpcVersion = 1
                }
            };
        }

        await SendJsonAsync(identifyPayload, cancellationToken).ConfigureAwait(false);

        using var identified = await ReceiveJsonAsync(cancellationToken).ConfigureAwait(false);
        if (identified.RootElement.GetProperty("op").GetInt32() != 2)
        {
            throw new InvalidOperationException("OBS websocket did not accept the connection.");
        }
    }

    public async Task<bool> GetReplayBufferActiveAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("GetReplayBufferStatus", null, cancellationToken).ConfigureAwait(false);
        if (response.TryGetProperty("responseData", out var responseData) &&
            responseData.TryGetProperty("outputActive", out var outputActive))
        {
            return outputActive.GetBoolean();
        }

        return false;
    }

    public Task<bool> GetStreamActiveAsync(CancellationToken cancellationToken = default) =>
        GetOutputActiveAsync("GetStreamStatus", cancellationToken);

    public Task<bool> GetRecordActiveAsync(CancellationToken cancellationToken = default) =>
        GetOutputActiveAsync("GetRecordStatus", cancellationToken);

    private async Task<bool> GetOutputActiveAsync(string requestType, CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(requestType, null, cancellationToken).ConfigureAwait(false);
        if (response.TryGetProperty("responseData", out var responseData) &&
            responseData.TryGetProperty("outputActive", out var outputActive))
        {
            return outputActive.GetBoolean();
        }

        return false;
    }

    public Task StartReplayBufferAsync(CancellationToken cancellationToken = default) =>
        SendRequestNoDataAsync("StartReplayBuffer", cancellationToken);

    public Task StopReplayBufferAsync(CancellationToken cancellationToken = default) =>
        SendRequestNoDataAsync("StopReplayBuffer", cancellationToken);

    public Task SaveReplayBufferAsync(CancellationToken cancellationToken = default) =>
        SendRequestNoDataAsync("SaveReplayBuffer", cancellationToken);

    public Task SetProfileParameterAsync(string category, string name, string value, CancellationToken cancellationToken = default) =>
        SendRequestNoDataAsync("SetProfileParameter", new
        {
            parameterCategory = category,
            parameterName = name,
            parameterValue = value
        }, cancellationToken);

    public async Task<string?> GetProfileParameterAsync(string category, string name, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("GetProfileParameter", new
        {
            parameterCategory = category,
            parameterName = name
        }, cancellationToken).ConfigureAwait(false);

        if (!response.TryGetProperty("responseData", out var responseData) ||
            !responseData.TryGetProperty("parameterValue", out var parameterValue))
        {
            return null;
        }

        return parameterValue.ValueKind == JsonValueKind.String
            ? parameterValue.GetString()
            : parameterValue.ToString();
    }

    public async Task<ObsNameList> GetProfileListAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("GetProfileList", null, cancellationToken).ConfigureAwait(false);
        if (!response.TryGetProperty("responseData", out var responseData))
        {
            return new ObsNameList("", Array.Empty<string>());
        }

        var currentName = responseData.TryGetProperty("currentProfileName", out var current)
            ? current.GetString() ?? ""
            : "";
        var names = responseData.TryGetProperty("profiles", out var profiles)
            ? ReadStringArray(profiles)
            : Array.Empty<string>();

        return new ObsNameList(currentName, names);
    }

    public Task CreateProfileAsync(string profileName, CancellationToken cancellationToken = default) =>
        SendRequestNoDataAsync("CreateProfile", new
        {
            profileName
        }, cancellationToken);

    public Task SetCurrentProfileAsync(string profileName, CancellationToken cancellationToken = default) =>
        SendRequestNoDataAsync("SetCurrentProfile", new
        {
            profileName
        }, cancellationToken);

    public async Task<ObsNameList> GetSceneCollectionListAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("GetSceneCollectionList", null, cancellationToken).ConfigureAwait(false);
        if (!response.TryGetProperty("responseData", out var responseData))
        {
            return new ObsNameList("", Array.Empty<string>());
        }

        var currentName = responseData.TryGetProperty("currentSceneCollectionName", out var current)
            ? current.GetString() ?? ""
            : "";
        var names = responseData.TryGetProperty("sceneCollections", out var collections)
            ? ReadStringArray(collections)
            : Array.Empty<string>();

        return new ObsNameList(currentName, names);
    }

    public Task CreateSceneCollectionAsync(string sceneCollectionName, CancellationToken cancellationToken = default) =>
        SendRequestNoDataAsync("CreateSceneCollection", new
        {
            sceneCollectionName
        }, cancellationToken);

    public Task SetCurrentSceneCollectionAsync(string sceneCollectionName, CancellationToken cancellationToken = default) =>
        SendRequestNoDataAsync("SetCurrentSceneCollection", new
        {
            sceneCollectionName
        }, cancellationToken);

    public async Task<string> GetCurrentProgramSceneAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("GetCurrentProgramScene", null, cancellationToken).ConfigureAwait(false);
        if (response.TryGetProperty("responseData", out var responseData) &&
            responseData.TryGetProperty("currentProgramSceneName", out var sceneName))
        {
            return sceneName.GetString() ?? "";
        }

        return "";
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return array.EnumerateArray()
            .Select(item => item.GetString())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetInputKindListAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("GetInputKindList", null, cancellationToken).ConfigureAwait(false);
        if (!response.TryGetProperty("responseData", out var responseData) ||
            !responseData.TryGetProperty("inputKinds", out var inputKinds))
        {
            return Array.Empty<string>();
        }

        return inputKinds.EnumerateArray()
            .Select(kind => kind.GetString())
            .Where(kind => !string.IsNullOrWhiteSpace(kind))
            .Select(kind => kind!)
            .ToList();
    }

    public async Task<bool> InputExistsAsync(string inputName, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("GetInputList", null, cancellationToken).ConfigureAwait(false);
        if (!response.TryGetProperty("responseData", out var responseData) ||
            !responseData.TryGetProperty("inputs", out var inputs))
        {
            return false;
        }

        return inputs.EnumerateArray().Any(input =>
            input.TryGetProperty("inputName", out var name) &&
            string.Equals(name.GetString(), inputName, StringComparison.OrdinalIgnoreCase));
    }

    public Task CreateInputAsync(string sceneName, string inputName, string inputKind, object inputSettings, CancellationToken cancellationToken = default) =>
        SendRequestNoDataAsync("CreateInput", new
        {
            sceneName,
            inputName,
            inputKind,
            inputSettings,
            sceneItemEnabled = true
        }, cancellationToken);

    public Task SetInputSettingsAsync(string inputName, object inputSettings, bool overlay, CancellationToken cancellationToken = default) =>
        SendRequestNoDataAsync("SetInputSettings", new
        {
            inputName,
            inputSettings,
            overlay
        }, cancellationToken);

    public Task SetInputMuteAsync(string inputName, bool inputMuted, CancellationToken cancellationToken = default) =>
        SendRequestNoDataAsync("SetInputMute", new
        {
            inputName,
            inputMuted
        }, cancellationToken);

    public Task SetInputVolumeAsync(string inputName, double inputVolumeMul, CancellationToken cancellationToken = default) =>
        SendRequestNoDataAsync("SetInputVolume", new
        {
            inputName,
            inputVolumeMul
        }, cancellationToken);

    public Task SetInputAudioTracksAsync(string inputName, IReadOnlyDictionary<string, bool> inputAudioTracks, CancellationToken cancellationToken = default) =>
        SendRequestNoDataAsync("SetInputAudioTracks", new
        {
            inputName,
            inputAudioTracks
        }, cancellationToken);

    public Task SetVideoSettingsAsync(int baseWidth, int baseHeight, int outputWidth, int outputHeight, int fpsNumerator, int fpsDenominator, CancellationToken cancellationToken = default) =>
        SendRequestNoDataAsync("SetVideoSettings", new
        {
            baseWidth,
            baseHeight,
            outputWidth,
            outputHeight,
            fpsNumerator,
            fpsDenominator
        }, cancellationToken);

    public async Task<int?> GetSceneItemIdAsync(string sceneName, string sourceName, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("GetSceneItemList", new
        {
            sceneName
        }, cancellationToken).ConfigureAwait(false);

        if (!response.TryGetProperty("responseData", out var responseData) ||
            !responseData.TryGetProperty("sceneItems", out var sceneItems))
        {
            return null;
        }

        foreach (var item in sceneItems.EnumerateArray())
        {
            if (!item.TryGetProperty("sourceName", out var sourceNameElement) ||
                !string.Equals(sourceNameElement.GetString(), sourceName, StringComparison.OrdinalIgnoreCase) ||
                !item.TryGetProperty("sceneItemId", out var sceneItemId))
            {
                continue;
            }

            return sceneItemId.GetInt32();
        }

        return null;
    }

    public async Task<bool> SceneItemExistsAsync(string sceneName, string sourceName, CancellationToken cancellationToken = default) =>
        await GetSceneItemIdAsync(sceneName, sourceName, cancellationToken).ConfigureAwait(false) is not null;

    public Task CreateSceneItemAsync(string sceneName, string sourceName, CancellationToken cancellationToken = default) =>
        SendRequestNoDataAsync("CreateSceneItem", new
        {
            sceneName,
            sourceName,
            sceneItemEnabled = true
        }, cancellationToken);

    public Task SetSceneItemEnabledAsync(string sceneName, int sceneItemId, bool sceneItemEnabled, CancellationToken cancellationToken = default) =>
        SendRequestNoDataAsync("SetSceneItemEnabled", new
        {
            sceneName,
            sceneItemId,
            sceneItemEnabled
        }, cancellationToken);

    public Task SetSceneItemTransformAsync(string sceneName, int sceneItemId, object sceneItemTransform, CancellationToken cancellationToken = default) =>
        SendRequestNoDataAsync("SetSceneItemTransform", new
        {
            sceneName,
            sceneItemId,
            sceneItemTransform
        }, cancellationToken);

    public async Task<IReadOnlyList<ObsPropertyListItem>> GetInputListPropertyItemsAsync(string inputName, string propertyName, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("GetInputPropertiesListPropertyItems", new
        {
            inputName,
            propertyName
        }, cancellationToken).ConfigureAwait(false);

        if (!response.TryGetProperty("responseData", out var responseData) ||
            !responseData.TryGetProperty("propertyItems", out var propertyItems))
        {
            return Array.Empty<ObsPropertyListItem>();
        }

        var items = new List<ObsPropertyListItem>();
        foreach (var item in propertyItems.EnumerateArray())
        {
            if (!item.TryGetProperty("itemValue", out var value))
            {
                continue;
            }

            var itemValue = value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : value.ToString();

            if (string.IsNullOrWhiteSpace(itemValue))
            {
                continue;
            }

            var itemName = item.TryGetProperty("itemName", out var name) && name.ValueKind == JsonValueKind.String
                ? name.GetString() ?? itemValue
                : itemValue;

            var enabled = !item.TryGetProperty("itemEnabled", out var enabledElement) ||
                enabledElement.ValueKind != JsonValueKind.False;

            items.Add(new ObsPropertyListItem(itemName, itemValue, enabled));
        }

        return items;
    }

    public async Task<string?> GetPreferredInputListPropertyValueAsync(string inputName, string propertyName, CancellationToken cancellationToken = default)
    {
        var propertyItems = await GetInputListPropertyItemsAsync(inputName, propertyName, cancellationToken).ConfigureAwait(false);
        string? firstAvailable = null;

        foreach (var item in propertyItems)
        {
            if (!item.Enabled)
            {
                continue;
            }

            firstAvailable ??= item.Value;
            if (item.Name.Contains("Primary", StringComparison.OrdinalIgnoreCase))
            {
                return item.Value;
            }
        }

        return firstAvailable;
    }

    private async Task SendRequestNoDataAsync(string requestType, CancellationToken cancellationToken)
    {
        _ = await SendRequestAsync(requestType, null, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendRequestNoDataAsync(string requestType, object requestData, CancellationToken cancellationToken)
    {
        _ = await SendRequestAsync(requestType, requestData, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonElement> SendRequestAsync(string requestType, object? requestData, CancellationToken cancellationToken)
    {
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ConnectAsync(cancellationToken).ConfigureAwait(false);

            var requestId = Guid.NewGuid().ToString("N");
            var payload = new
            {
                op = 6,
                d = new
                {
                    requestType,
                    requestId,
                    requestData = requestData ?? new { }
                }
            };

            await SendJsonAsync(payload, cancellationToken).ConfigureAwait(false);

            while (true)
            {
                using var response = await ReceiveJsonAsync(cancellationToken).ConfigureAwait(false);
                var root = response.RootElement;
                if (root.GetProperty("op").GetInt32() != 7)
                {
                    continue;
                }

                var data = root.GetProperty("d");
                if (!string.Equals(data.GetProperty("requestId").GetString(), requestId, StringComparison.Ordinal))
                {
                    continue;
                }

                var status = data.GetProperty("requestStatus");
                var result = status.TryGetProperty("result", out var resultElement) && resultElement.GetBoolean();
                if (!result)
                {
                    var comment = status.TryGetProperty("comment", out var commentElement)
                        ? commentElement.GetString()
                        : null;
                    var code = status.TryGetProperty("code", out var codeElement)
                        ? codeElement.GetInt32()
                        : 0;
                    throw new ObsRequestException(requestType, code, comment);
                }

                return data.Clone();
            }
        }
        catch
        {
            await DisposeSocketAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private async Task SendJsonAsync(object payload, CancellationToken cancellationToken)
    {
        if (_socket is null)
        {
            throw new InvalidOperationException("OBS websocket is not connected.");
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        await _socket.SendAsync(json, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonDocument> ReceiveJsonAsync(CancellationToken cancellationToken)
    {
        if (_socket is null)
        {
            throw new InvalidOperationException("OBS websocket is not connected.");
        }

        using var ms = new MemoryStream();
        var buffer = new byte[8192];

        while (true)
        {
            var result = await _socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new WebSocketException("OBS websocket closed the connection.");
            }

            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                break;
            }
        }

        ms.Position = 0;
        return await JsonDocument.ParseAsync(ms, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static bool TryGetAuthentication(JsonElement hello, out string salt, out string challenge)
    {
        salt = "";
        challenge = "";

        if (!hello.GetProperty("d").TryGetProperty("authentication", out var authentication))
        {
            return false;
        }

        salt = authentication.GetProperty("salt").GetString() ?? "";
        challenge = authentication.GetProperty("challenge").GetString() ?? "";
        return salt.Length > 0 && challenge.Length > 0;
    }

    private static string ComputeAuthentication(string password, string salt, string challenge)
    {
        var secretHash = SHA256.HashData(Encoding.UTF8.GetBytes(password + salt));
        var secret = Convert.ToBase64String(secretHash);
        var authHash = SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge));
        return Convert.ToBase64String(authHash);
    }

    private async ValueTask DisposeSocketAsync()
    {
        if (_socket is null)
        {
            return;
        }

        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
            // Best effort close; the next request will reconnect.
        }
        finally
        {
            _socket.Dispose();
            _socket = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeSocketAsync().ConfigureAwait(false);
        _ioLock.Dispose();
    }
}

public sealed class ObsRequestException : InvalidOperationException
{
    public ObsRequestException(string requestType, int code, string? comment)
        : base(BuildMessage(requestType, code, comment))
    {
        RequestType = requestType;
        Code = code;
        Comment = comment;
    }

    public string RequestType { get; }
    public int Code { get; }
    public string? Comment { get; }

    private static string BuildMessage(string requestType, int code, string? comment)
    {
        var detail = string.IsNullOrWhiteSpace(comment) ? "OBS did not include a reason." : comment;
        return $"OBS rejected {requestType} (code {code}). {detail}";
    }
}
