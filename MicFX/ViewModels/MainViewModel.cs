using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicFX.Core;
using MicFX.Models;

namespace MicFX.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly AudioDeviceManager _deviceManager = new();
    private readonly AudioEngine _engine;

    [ObservableProperty] private string _startStopLabel = "Start";
    [ObservableProperty] private string? _activePresetName;
    [ObservableProperty] private string? _statusMessage;

    public DeviceViewModel DeviceVM { get; }
    public EqViewModel EqVM { get; } = new();
    public MeterViewModel MeterVM { get; } = new();
    public ObservableCollection<string> PresetNames { get; } = new();

    public MainViewModel()
    {
        _engine = new AudioEngine(_deviceManager);
        DeviceVM = new DeviceViewModel();

        _engine.PipelineError += msg =>
            System.Windows.Application.Current?.Dispatcher.Invoke(() => StatusMessage = msg);

        LoadSettingsOnStartup();
        RefreshPresetList();
    }

    private void LoadSettingsOnStartup()
    {
        var settings = SettingsService.LoadSettings();

        // Restore EQ gains
        EqVM.SetGains(settings.EqGains);

        // Restore effects settings
        EqVM.GateThresholdDb = settings.NoiseGate.ThresholdDb;
        EqVM.GateAttackMs    = settings.NoiseGate.AttackMs;
        EqVM.GateHoldMs      = settings.NoiseGate.HoldMs;
        EqVM.GateReleaseMs   = settings.NoiseGate.ReleaseMs;

        EqVM.CompThresholdDb  = settings.Compressor.ThresholdDb;
        EqVM.CompRatio        = settings.Compressor.Ratio;
        EqVM.CompAttackMs     = settings.Compressor.AttackMs;
        EqVM.CompReleaseMs    = settings.Compressor.ReleaseMs;
        EqVM.CompMakeupGainDb = settings.Compressor.MakeupGainDb;

        // Restore monitor
        DeviceVM.MonitorVolume = settings.MonitorVolume;
        DeviceVM.MonitorMuted  = settings.MonitorMuted;

        ActivePresetName = settings.ActivePresetName;
    }

    private AppSettings BuildCurrentSettings() => new AppSettings
    {
        InputDeviceId    = DeviceVM.SelectedInput?.Id,
        OutputDeviceId   = DeviceVM.SelectedOutput?.Id,
        MonitorDeviceId  = DeviceVM.SelectedMonitor?.Id,
        MonitorVolume    = DeviceVM.MonitorVolume,
        MonitorMuted     = DeviceVM.MonitorMuted,
        EqGains          = EqVM.GetGains(),
        NoiseGate        = new NoiseGateSettings
        {
            ThresholdDb = EqVM.GateThresholdDb,
            AttackMs    = EqVM.GateAttackMs,
            HoldMs      = EqVM.GateHoldMs,
            ReleaseMs   = EqVM.GateReleaseMs,
        },
        Compressor = new CompressorSettings
        {
            ThresholdDb  = EqVM.CompThresholdDb,
            Ratio        = EqVM.CompRatio,
            AttackMs     = EqVM.CompAttackMs,
            ReleaseMs    = EqVM.CompReleaseMs,
            MakeupGainDb = EqVM.CompMakeupGainDb,
        },
        ActivePresetName = ActivePresetName,
    };

    [RelayCommand]
    private void StartStop()
    {
        if (_engine.IsRunning)
        {
            _engine.Stop();
            MeterVM.DetachEngine();
            StartStopLabel = "Start";
            StatusMessage = null;
        }
        else
        {
            var inputId = DeviceVM.SelectedInput?.Id;
            var monitorId = DeviceVM.SelectedMonitor?.Id;

            if (inputId == null || monitorId == null)
            {
                StatusMessage = "Select input and monitor devices first.";
                return;
            }

            // Only route to virtual cable if one is actually selected
            var cableId = DeviceVM.SelectedOutput?.IsVirtualCable == true
                ? DeviceVM.SelectedOutput.Id
                : null;
            _engine.Start(inputId, monitorId, DeviceVM.MonitorVolume, cableId);
            EqVM.AttachEngine(_engine);
            MeterVM.AttachEngine(_engine);
            StartStopLabel = "Stop";
        }
    }

    [RelayCommand]
    private void SavePreset()
    {
        var name = ActivePresetName;
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = "Enter a preset name first.";
            return;
        }

        var preset = new EqPreset
        {
            Name     = name,
            EqGains  = EqVM.GetGains(),
            NoiseGate = new NoiseGateSettings
            {
                ThresholdDb = EqVM.GateThresholdDb,
                AttackMs    = EqVM.GateAttackMs,
                HoldMs      = EqVM.GateHoldMs,
                ReleaseMs   = EqVM.GateReleaseMs,
            },
            Compressor = new CompressorSettings
            {
                ThresholdDb  = EqVM.CompThresholdDb,
                Ratio        = EqVM.CompRatio,
                AttackMs     = EqVM.CompAttackMs,
                ReleaseMs    = EqVM.CompReleaseMs,
                MakeupGainDb = EqVM.CompMakeupGainDb,
            }
        };

        SettingsService.SavePreset(preset);
        RefreshPresetList();
        SettingsService.SaveSettings(BuildCurrentSettings());
        StatusMessage = $"Preset '{name}' saved.";
    }

    [RelayCommand]
    private void LoadPreset()
    {
        if (string.IsNullOrWhiteSpace(ActivePresetName)) return;

        var preset = SettingsService.LoadPreset(ActivePresetName);
        if (preset == null)
        {
            StatusMessage = $"Preset '{ActivePresetName}' not found.";
            return;
        }

        EqVM.SetGains(preset.EqGains);
        EqVM.GateThresholdDb = preset.NoiseGate.ThresholdDb;
        EqVM.GateAttackMs    = preset.NoiseGate.AttackMs;
        EqVM.GateHoldMs      = preset.NoiseGate.HoldMs;
        EqVM.GateReleaseMs   = preset.NoiseGate.ReleaseMs;
        EqVM.CompThresholdDb  = preset.Compressor.ThresholdDb;
        EqVM.CompRatio        = preset.Compressor.Ratio;
        EqVM.CompAttackMs     = preset.Compressor.AttackMs;
        EqVM.CompReleaseMs    = preset.Compressor.ReleaseMs;
        EqVM.CompMakeupGainDb = preset.Compressor.MakeupGainDb;

        StatusMessage = null;
    }

    private void RefreshPresetList()
    {
        var names = SettingsService.ListPresetNames();
        PresetNames.Clear();
        foreach (var n in names) PresetNames.Add(n);
    }

    public void SaveSettingsOnExit() => SettingsService.SaveSettings(BuildCurrentSettings());

    public void Dispose()
    {
        SaveSettingsOnExit();
        _engine.Dispose();
        MeterVM.Dispose();
        DeviceVM.Dispose();
        _deviceManager.Dispose();
    }
}
