using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicFX.Core;

namespace MicFX.ViewModels;

public partial class DeviceViewModel : ObservableObject, IDisposable
{
    private readonly AudioDeviceManager _deviceManager = new();

    public ObservableCollection<AudioDeviceInfo> InputDevices { get; } = new();
    public ObservableCollection<AudioDeviceInfo> OutputDevices { get; } = new();
    public ObservableCollection<AudioDeviceInfo> MonitorDevices { get; } = new();

    [ObservableProperty] private AudioDeviceInfo? _selectedInput;
    [ObservableProperty] private AudioDeviceInfo? _selectedOutput;
    [ObservableProperty] private AudioDeviceInfo? _selectedMonitor;

    [ObservableProperty] private float _monitorVolume = 0.8f;
    [ObservableProperty] private bool _monitorMuted = true;
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
        var renders = _deviceManager.GetRenderDevices();

        InputDevices.Clear();
        foreach (var d in inputs) InputDevices.Add(d);

        OutputDevices.Clear();
        MonitorDevices.Clear();
        foreach (var d in renders)
        {
            OutputDevices.Add(d);
            MonitorDevices.Add(d);
        }

        // Select defaults if nothing selected
        if (SelectedInput == null)
        {
            var def = _deviceManager.GetDefaultCapture();
            SelectedInput = inputs.FirstOrDefault(d => d.Id == def?.Id) ?? inputs.FirstOrDefault();
        }
        else
        {
            SelectedInput = inputs.FirstOrDefault(d => d.Id == SelectedInput.Id) ?? inputs.FirstOrDefault();
        }

        if (SelectedOutput == null)
        {
            // Prefer virtual cable for output
            SelectedOutput = renders.FirstOrDefault(d => d.IsVirtualCable) ?? renders.FirstOrDefault();
        }
        else
        {
            SelectedOutput = renders.FirstOrDefault(d => d.Id == SelectedOutput.Id) ?? renders.FirstOrDefault();
        }

        if (SelectedMonitor == null)
        {
            var def = _deviceManager.GetDefaultRender();
            SelectedMonitor = renders.FirstOrDefault(d => d.Id == def?.Id) ?? renders.FirstOrDefault();
        }
        else
        {
            SelectedMonitor = renders.FirstOrDefault(d => d.Id == SelectedMonitor.Id) ?? renders.FirstOrDefault();
        }

        NoVirtualCableWarning = !_deviceManager.HasVirtualCable();
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
}
