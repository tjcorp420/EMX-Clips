using NAudio.CoreAudioApi;

namespace EmxClips;

public sealed record AudioInputDevice(string Id, string Name)
{
    public override string ToString() => Name;
}

public static class AudioDeviceService
{
    public static IReadOnlyList<AudioInputDevice> ListCaptureDevices()
    {
        var devices = new List<AudioInputDevice>
        {
            new("default", "Default Windows microphone")
        };

        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            devices.Add(new AudioInputDevice(device.ID, device.FriendlyName));
            device.Dispose();
        }

        return devices;
    }

    public static float GetPeakLevel(string deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = string.IsNullOrWhiteSpace(deviceId) || string.Equals(deviceId, "default", StringComparison.OrdinalIgnoreCase)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
            : enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .FirstOrDefault(endpoint => string.Equals(endpoint.ID, deviceId, StringComparison.OrdinalIgnoreCase));

        return device?.AudioMeterInformation.MasterPeakValue ?? 0f;
    }
}
