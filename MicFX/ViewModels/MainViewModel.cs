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

        DeviceVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DeviceViewModel.MonitorVolume) or nameof(DeviceViewModel.MonitorMuted))
                _engine.SetMonitorVolume(DeviceVM.MonitorMuted ? 0f : DeviceVM.MonitorVolume);

            if (e.PropertyName is nameof(DeviceViewModel.SelectedInput)
                or nameof(DeviceViewModel.SelectedOutput)
                or nameof(DeviceViewModel.SelectedMonitor)
                or nameof(DeviceViewModel.MonitorVolume)
                or nameof(DeviceViewModel.MonitorMuted))
            {
                SettingsService.SaveSettings(BuildCurrentSettings());
            }
        };

        LoadSettingsOnStartup();
        RefreshPresetList();
    }

    private void LoadSettingsOnStartup()
    {
        var settings = SettingsService.LoadSettings();
        MigrateLegacyAudioDefaults(settings);

        DeviceVM.RestoreSelections(
            settings.InputDeviceId,
            settings.InputDeviceName,
            settings.OutputDeviceId,
            settings.OutputDeviceName,
            settings.MonitorDeviceId,
            settings.MonitorDeviceName);

        // Restore EQ gains
        EqVM.SetGains(settings.EqGains);

        // Restore effects settings
        EqVM.GateEnabled       = settings.NoiseGate.Enabled;
        EqVM.SpeechThreshold   = ResolveSpeechThreshold(settings.NoiseGate);
        EqVM.CloseVoiceBias    = settings.NoiseGate.CloseVoiceBias;
        EqVM.FloorAttenuationDb = settings.NoiseGate.FloorAttenuationDb;
        EqVM.GateAttackMs      = settings.NoiseGate.AttackMs;
        EqVM.GateHoldMs        = settings.NoiseGate.HoldMs;
        EqVM.GateReleaseMs     = settings.NoiseGate.ReleaseMs;

        EqVM.CompressorEnabled = settings.Compressor.Enabled;
        EqVM.CompThresholdDb  = settings.Compressor.ThresholdDb;
        EqVM.CompRatio        = settings.Compressor.Ratio;
        EqVM.CompAttackMs     = settings.Compressor.AttackMs;
        EqVM.CompReleaseMs    = settings.Compressor.ReleaseMs;
        EqVM.CompMakeupGainDb = settings.Compressor.MakeupGainDb;

        // Restore filters
        EqVM.HpfEnabled  = settings.Filters.HpfEnabled;
        EqVM.HpfCutoffHz = settings.Filters.HpfCutoffHz;
        EqVM.LpfEnabled  = settings.Filters.LpfEnabled;
        EqVM.LpfCutoffHz = settings.Filters.LpfCutoffHz;

        // Restore noise suppressor
        EqVM.NoiseSuppressorEnabled  = settings.NoiseSuppressorEnabled;
        EqVM.NoiseSuppressorStrength = settings.NoiseSuppressorStrength;

        // Restore monitor
        DeviceVM.MonitorVolume = settings.MonitorVolume;
        DeviceVM.MonitorMuted  = settings.MonitorMuted;

        ActivePresetName = settings.ActivePresetName;
    }

    private static void MigrateLegacyAudioDefaults(AppSettings settings)
    {
        if (Math.Abs(settings.NoiseSuppressorStrength - 0.85f) < 0.001f)
            settings.NoiseSuppressorStrength = 0.45f;

        bool gateLooksUntouched =
            settings.NoiseGate.Enabled &&
            Math.Abs(settings.NoiseGate.SpeechThreshold - 0.55f) < 0.001f &&
            Math.Abs(settings.NoiseGate.CloseVoiceBias - 0.65f) < 0.001f &&
            Math.Abs(settings.NoiseGate.FloorAttenuationDb - 18f) < 0.001f &&
            Math.Abs(settings.NoiseGate.AttackMs - 8f) < 0.001f &&
            Math.Abs(settings.NoiseGate.HoldMs - 140f) < 0.001f &&
            Math.Abs(settings.NoiseGate.ReleaseMs - 180f) < 0.001f;

        if (gateLooksUntouched)
            settings.NoiseGate.Enabled = false;
    }

    private AppSettings BuildCurrentSettings() => new AppSettings
    {
        InputDeviceId    = DeviceVM.SelectedInput?.Id,
        InputDeviceName  = DeviceVM.SelectedInput?.FriendlyName,
        OutputDeviceId   = DeviceVM.SelectedOutput?.Id,
        OutputDeviceName = DeviceVM.SelectedOutput?.FriendlyName,
        MonitorDeviceId  = DeviceVM.SelectedMonitor?.Id,
        MonitorDeviceName = DeviceVM.SelectedMonitor?.FriendlyName,
        MonitorVolume    = DeviceVM.MonitorVolume,
        MonitorMuted     = DeviceVM.MonitorMuted,
        NoiseSuppressorEnabled  = EqVM.NoiseSuppressorEnabled,
        NoiseSuppressorStrength = EqVM.NoiseSuppressorStrength,
        Filters = new FilterSettings
        {
            HpfEnabled  = EqVM.HpfEnabled,
            HpfCutoffHz = EqVM.HpfCutoffHz,
            LpfEnabled  = EqVM.LpfEnabled,
            LpfCutoffHz = EqVM.LpfCutoffHz,
        },
        EqGains          = EqVM.GetGains(),
        NoiseGate        = new NoiseGateSettings
        {
            Enabled           = EqVM.GateEnabled,
            ThresholdDb       = EqVM.SpeechThreshold * 70f - 70f,
            SpeechThreshold   = EqVM.SpeechThreshold,
            CloseVoiceBias    = EqVM.CloseVoiceBias,
            FloorAttenuationDb = EqVM.FloorAttenuationDb,
            AttackMs          = EqVM.GateAttackMs,
            HoldMs            = EqVM.GateHoldMs,
            ReleaseMs         = EqVM.GateReleaseMs,
        },
        Compressor = new CompressorSettings
        {
            Enabled      = EqVM.CompressorEnabled,
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
        try
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

                var cableId = DeviceVM.SelectedOutput?.IsVirtualCable == true
                    ? DeviceVM.SelectedOutput.Id
                    : null;

                _engine.Start(inputId, monitorId, DeviceVM.MonitorMuted ? 0f : DeviceVM.MonitorVolume, cableId);
                EqVM.AttachEngine(_engine);
                MeterVM.AttachEngine(_engine);
                StartStopLabel = "Stop";
            }
        }
        catch (Exception ex)
        {
            _engine.Stop();
            MeterVM.DetachEngine();
            StartStopLabel = "Start";
            StatusMessage = $"Failed to start audio: {ex.Message}";
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
                Enabled           = EqVM.GateEnabled,
                ThresholdDb       = EqVM.SpeechThreshold * 70f - 70f,
                SpeechThreshold   = EqVM.SpeechThreshold,
                CloseVoiceBias    = EqVM.CloseVoiceBias,
                FloorAttenuationDb = EqVM.FloorAttenuationDb,
                AttackMs          = EqVM.GateAttackMs,
                HoldMs            = EqVM.GateHoldMs,
                ReleaseMs         = EqVM.GateReleaseMs,
            },
            Compressor = new CompressorSettings
            {
                Enabled      = EqVM.CompressorEnabled,
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
        EqVM.GateEnabled       = preset.NoiseGate.Enabled;
        EqVM.SpeechThreshold   = ResolveSpeechThreshold(preset.NoiseGate);
        EqVM.CloseVoiceBias    = preset.NoiseGate.CloseVoiceBias;
        EqVM.FloorAttenuationDb = preset.NoiseGate.FloorAttenuationDb;
        EqVM.GateAttackMs      = preset.NoiseGate.AttackMs;
        EqVM.GateHoldMs        = preset.NoiseGate.HoldMs;
        EqVM.GateReleaseMs     = preset.NoiseGate.ReleaseMs;
        EqVM.CompressorEnabled = preset.Compressor.Enabled;
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

    private static float ResolveSpeechThreshold(NoiseGateSettings settings)
    {
        float derivedLegacyThresholdDb = settings.SpeechThreshold * 70f - 70f;
        if (Math.Abs(settings.ThresholdDb - derivedLegacyThresholdDb) > 0.01f)
            return Math.Clamp((settings.ThresholdDb + 70f) / 70f, 0f, 1f);

        return settings.SpeechThreshold;
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
