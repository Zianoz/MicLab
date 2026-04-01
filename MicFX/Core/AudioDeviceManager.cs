using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace MicFX.Core;

public class AudioDeviceInfo
{
    public string Id { get; init; } = "";
    public string FriendlyName { get; init; } = "";
    public bool IsVirtualCable { get; init; }
    public DeviceState State { get; init; }
    public bool IsAvailable => State == DeviceState.Active;
    public string DisplayName => IsAvailable ? FriendlyName : $"{FriendlyName} ({State})";

    public override string ToString() => DisplayName;
}

public class AudioDeviceManager : IDisposable, IMMNotificationClient
{
    private readonly MMDeviceEnumerator _enumerator = new();

    // Raised on the thread that triggered the device change
    public event Action? DevicesChanged;

    private static readonly string[] VirtualCablePatterns =
        ["VB-CABLE", "VoiceMeeter", "CABLE Input", "CABLE Output", "VBAudio"];

    public IReadOnlyList<AudioDeviceInfo> GetCaptureDevices()
        => GetDevices(DataFlow.Capture);

    public IReadOnlyList<AudioDeviceInfo> GetRenderDevices()
        => GetDevices(DataFlow.Render);

    private List<AudioDeviceInfo> GetDevices(DataFlow flow)
    {
        var list = new List<AudioDeviceInfo>();
        try
        {
            var devices = _enumerator.EnumerateAudioEndPoints(
                flow,
                DeviceState.Active | DeviceState.Disabled | DeviceState.Unplugged);
            foreach (var d in devices)
            {
                list.Add(new AudioDeviceInfo
                {
                    Id = d.ID,
                    FriendlyName = d.FriendlyName,
                    IsVirtualCable = IsVirtual(d.FriendlyName),
                    State = d.State
                });
            }
        }
        catch { /* device enumeration can fail transiently */ }
        return list
            .OrderByDescending(d => d.IsAvailable)
            .ThenByDescending(d => d.IsVirtualCable)
            .ThenBy(d => d.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public AudioDeviceInfo? GetDefaultCapture()
    {
        try
        {
            var d = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            return new AudioDeviceInfo { Id = d.ID, FriendlyName = d.FriendlyName, IsVirtualCable = IsVirtual(d.FriendlyName), State = d.State };
        }
        catch { return null; }
    }

    public AudioDeviceInfo? GetDefaultRender()
    {
        try
        {
            var d = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return new AudioDeviceInfo { Id = d.ID, FriendlyName = d.FriendlyName, IsVirtualCable = IsVirtual(d.FriendlyName), State = d.State };
        }
        catch { return null; }
    }

    public bool HasVirtualCable()
        => GetRenderDevices().Any(d => d.IsVirtualCable) || GetCaptureDevices().Any(d => d.IsVirtualCable);

    public MMDevice? OpenDevice(string deviceId)
    {
        try { return _enumerator.GetDevice(deviceId); }
        catch { return null; }
    }

    private static bool IsVirtual(string name)
        => VirtualCablePatterns.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase));

    public void RegisterNotifications() => _enumerator.RegisterEndpointNotificationCallback(this);
    public void UnregisterNotifications() => _enumerator.UnregisterEndpointNotificationCallback(this);

    // IMMNotificationClient
    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) => DevicesChanged?.Invoke();
    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) => DevicesChanged?.Invoke();
    void IMMNotificationClient.OnDeviceRemoved(string deviceId) => DevicesChanged?.Invoke();
    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) { }
    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        try { UnregisterNotifications(); } catch { }
        _enumerator.Dispose();
    }
}
