using NAudio.CoreAudioApi;
using NAudio.Wave;
using MicFX.DSP;

namespace MicFX.Core;

/// <summary>
/// Owns the full audio pipeline lifecycle.
/// Exposes RMS/peak levels for the UI (updated via a 30fps timer in the ViewModel, NOT per-sample).
/// </summary>
public class AudioEngine : IDisposable
{
    private readonly AudioDeviceManager _deviceManager;
    private AudioPipeline? _pipeline;
    private readonly object _stateLock = new();

    // DSP processors — kept alive so the UI can bind to them
    public EffectsChain? Effects { get; private set; }

    // Convenience accessors
    public FilterBank?      Filters          => Effects?.Filters;
    public EqProcessor?     Eq               => Effects?.Eq;
    public NoiseSuppressor? NoiseSuppressor  => Effects?.NoiseSuppressor;
    public NoiseGate?       Gate             => Effects?.Gate;
    public Compressor?      Compressor       => Effects?.Compressor;

    // Latest level values written from audio thread, read from UI timer
    private volatile float _inputRms;
    private volatile float _inputPeak;
    private volatile float _outputRms;
    private volatile float _outputPeak;
    public float InputRms => _inputRms;
    public float InputPeak => _inputPeak;
    public float OutputRms => _outputRms;
    public float OutputPeak => _outputPeak;

    // Raised when the pipeline stops unexpectedly (device removed, etc.)
    public event Action<string>? PipelineError;

    public bool IsRunning => _pipeline?.IsRunning ?? false;

    public AudioEngine(AudioDeviceManager deviceManager)
    {
        _deviceManager = deviceManager;
    }

    public void Start(string inputDeviceId, string monitorDeviceId, float monitorVolume, string? cableDeviceId = null)
    {
        lock (_stateLock)
        {
            Stop();

            var inputDevice = _deviceManager.OpenDevice(inputDeviceId);
            var monitorDevice = _deviceManager.OpenDevice(monitorDeviceId);

            if (inputDevice == null || monitorDevice == null)
            {
                PipelineError?.Invoke("Could not open audio devices.");
                return;
            }

            if (inputDeviceId == monitorDeviceId)
                PipelineError?.Invoke("Warning: Input and monitor are the same device — risk of feedback.");

            var cableDevice = cableDeviceId != null ? _deviceManager.OpenDevice(cableDeviceId) : null;
            EffectsChain? chainRef = null;

            _pipeline = new AudioPipeline
            {
                BuildChain = source =>
                {
                    chainRef = new EffectsChain(source);
                    return chainRef;
                },
                InputLevelCallback = (rms, peak) =>
                {
                    _inputRms = rms;
                    _inputPeak = peak;
                },
                OutputLevelCallback = (rms, peak) =>
                {
                    _outputRms = rms;
                    _outputPeak = peak;
                },
                ErrorCallback = msg => PipelineError?.Invoke(msg),
                StatusCallback = msg => PipelineError?.Invoke(msg)
            };

            try
            {
                _pipeline.Start(inputDevice, monitorDevice, monitorVolume, cableDevice);
                Effects = chainRef;
            }
            catch (Exception ex)
            {
                _pipeline.Dispose();
                _pipeline = null;
                Effects = null;
                PipelineError?.Invoke($"Failed to start audio: {ex.Message}");
            }
        }
    }

    public void SetMonitorVolume(float volume) => _pipeline?.SetMonitorVolume(volume);

    public void Stop()
    {
        lock (_stateLock)
        {
            _pipeline?.Stop();
            _pipeline?.Dispose();
            _pipeline = null;
            Effects = null;
            _inputRms = 0f;
            _inputPeak = 0f;
            _outputRms = 0f;
            _outputPeak = 0f;
        }
    }

    public void Dispose() => Stop();
}
