using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicFX.Core;

namespace MicFX.ViewModels;

public partial class DeviceViewModel : ObservableObject, IDisposable
{
    private readonly AudioDeviceManager _deviceManager = new();
    private string? _preferredInputId;
    private string? _preferredInputName;
    private string? _preferredOutputId;
    private string? _preferredOutputName;
    private string? _preferredMonitorId;
    private string? _preferredMonitorName;

    public ObservableCollection<AudioDeviceInfo> InputDevices { get; } = new();
    public ObservableCollection<AudioDeviceInfo> OutputDevices { get; } = new();
    public ObservableCollection<AudioDeviceInfo> MonitorDevices { get; } = new();

    [ObservableProperty] private AudioDeviceInfo? _selectedInput;
    [ObservableProperty] private AudioDeviceInfo? _selectedOutput;
    [ObservableProperty] private AudioDeviceInfo? _selectedMonitor;

    [ObservableProperty] private float _monitorVolume = 0.8f;
    [ObservableProperty] private bool _monitorMuted = false;
    [ObservableProperty] private bool _noVirtualCableWarning;

    public DeviceViewModel()
    {
        _deviceManager.DevicesChanged += OnDevicesChanged;
        _deviceManager.RegisterNotifications();
        RefreshDevices();
    }

    [RelayCommand]
    public void RefreshDevices()
    {
        var inputs = _deviceManager.GetCaptureDevices();
        var inputSources = inputs
            .Where(d => !d.IsVirtualCable)
            .OrderByDescending(d => d.IsAvailable)
            .ThenBy(d => d.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var renders = _deviceManager.GetRenderDevices();
        var routeTargets = renders
            .Where(IsRouteTarget)
            .OrderByDescending(d => d.IsAvailable)
            .ThenByDescending(IsPreferredCableRender)
            .ThenBy(d => d.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        InputDevices.Clear();
        foreach (var d in inputSources) InputDevices.Add(d);

        OutputDevices.Clear();
        MonitorDevices.Clear();
        foreach (var d in routeTargets) OutputDevices.Add(d);
        foreach (var d in renders) MonitorDevices.Add(d);

        if (!string.IsNullOrWhiteSpace(_preferredInputId))
        {
            SelectedInput = ResolvePreferred(inputSources, _preferredInputId, _preferredInputName)
                ?? inputSources.FirstOrDefault();
        }
        else if (SelectedInput == null)
        {
            var def = _deviceManager.GetDefaultCapture();
            SelectedInput = inputSources.FirstOrDefault(d => d.Id == def?.Id) ?? inputSources.FirstOrDefault();
        }
        else
        {
            SelectedInput = inputSources.FirstOrDefault(d => d.Id == SelectedInput.Id)
                ?? inputSources.FirstOrDefault(d => string.Equals(d.FriendlyName, SelectedInput.FriendlyName, StringComparison.OrdinalIgnoreCase))
                ?? inputSources.FirstOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(_preferredOutputId))
        {
            SelectedOutput = ResolvePreferred(routeTargets, _preferredOutputId, _preferredOutputName)
                ?? routeTargets.FirstOrDefault();
        }
        else if (SelectedOutput == null)
        {
            // Prefer virtual cable for output
            SelectedOutput = routeTargets.FirstOrDefault();
        }
        else
        {
            SelectedOutput = routeTargets.FirstOrDefault(d => d.Id == SelectedOutput.Id)
                ?? routeTargets.FirstOrDefault(d => string.Equals(d.FriendlyName, SelectedOutput.FriendlyName, StringComparison.OrdinalIgnoreCase))
                ?? routeTargets.FirstOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(_preferredMonitorId))
        {
            SelectedMonitor = ResolvePreferred(renders, _preferredMonitorId, _preferredMonitorName)
                ?? renders.FirstOrDefault();
        }
        else if (SelectedMonitor == null)
        {
            var def = _deviceManager.GetDefaultRender();
            SelectedMonitor = renders.FirstOrDefault(d => d.Id == def?.Id) ?? renders.FirstOrDefault();
        }
        else
        {
            SelectedMonitor = renders.FirstOrDefault(d => d.Id == SelectedMonitor.Id) ?? renders.FirstOrDefault();
        }

        NoVirtualCableWarning = routeTargets.Count == 0;
    }

    public void RestoreSelections(
        string? inputDeviceId,
        string? inputDeviceName,
        string? outputDeviceId,
        string? outputDeviceName,
        string? monitorDeviceId,
        string? monitorDeviceName)
    {
        _preferredInputId = inputDeviceId;
        _preferredInputName = inputDeviceName;
        _preferredOutputId = outputDeviceId;
        _preferredOutputName = outputDeviceName;
        _preferredMonitorId = monitorDeviceId;
        _preferredMonitorName = monitorDeviceName;
        RefreshDevices();
    }

    partial void OnSelectedInputChanged(AudioDeviceInfo? value)
    {
        _preferredInputId = value?.Id;
        _preferredInputName = value?.FriendlyName;
    }

    partial void OnSelectedOutputChanged(AudioDeviceInfo? value)
    {
        _preferredOutputId = value?.Id;
        _preferredOutputName = value?.FriendlyName;
    }

    partial void OnSelectedMonitorChanged(AudioDeviceInfo? value)
    {
        _preferredMonitorId = value?.Id;
        _preferredMonitorName = value?.FriendlyName;
    }

    [RelayCommand]
    private void ToggleMute() => MonitorMuted = !MonitorMuted;

    private void OnDevicesChanged()
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(RefreshDevices);
    }

    public void Dispose()
    {
        _deviceManager.Dispose();
    }

    private static AudioDeviceInfo? ResolvePreferred(
        IReadOnlyList<AudioDeviceInfo> devices,
        string? preferredId,
        string? preferredName)
    {
        return devices.FirstOrDefault(d => d.Id == preferredId)
            ?? devices.FirstOrDefault(d => d.IsAvailable && string.Equals(d.FriendlyName, preferredName, StringComparison.OrdinalIgnoreCase))
            ?? devices.FirstOrDefault(d => string.Equals(d.FriendlyName, preferredName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRouteTarget(AudioDeviceInfo device)
        => device.IsVirtualCable;

    private static bool IsPreferredCableRender(AudioDeviceInfo device)
        => device.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase)
           || device.FriendlyName.Contains("CABLE In", StringComparison.OrdinalIgnoreCase)
           || device.FriendlyName.Contains("VoiceMeeter Input", StringComparison.OrdinalIgnoreCase);
}
