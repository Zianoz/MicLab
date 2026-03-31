using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MicFX.Core;

/// <summary>
/// Wires: WasapiCapture → [DSP chain] → SplitSampleProvider
///   ├─→ WasapiOut (self-monitor / headphones)
///   └─→ WaveOutEvent (virtual cable / VB-CABLE)
/// </summary>
public class AudioPipeline : IDisposable
{
    private WasapiCapture? _capture;
    private WasapiOut? _monitorOut;
    private WaveOutEvent? _cableOut;
    private BufferedWaveProvider? _buffer;
    private VolumeSampleProvider? _monitorVolumeProvider;

    /// <summary>Inject DSP processors. Source → returns processed chain head.</summary>
    public Func<ISampleProvider, ISampleProvider>? BuildChain { get; set; }

    /// <summary>Called from audio thread with (rms, peak) after every read.</summary>
    public Action<float, float>? LevelCallback { get; set; }

    /// <summary>Called when an audio thread error occurs.</summary>
    public Action<string>? ErrorCallback { get; set; }

    public bool IsRunning { get; private set; }

    /// <summary>Called once with the capture format string when recording starts successfully.</summary>
    public Action<string>? StatusCallback { get; set; }

    private readonly object _captureLock = new();
    private bool _firstDataReceived;

    public void Start(
        MMDevice inputDevice,
        MMDevice monitorDevice,
        float monitorVolume,
        MMDevice? cableDevice = null)
    {
        Stop();

        _capture = new WasapiCapture(inputDevice, false, 100); // polling mode, more compatible
        _firstDataReceived = false;
        _buffer = new BufferedWaveProvider(_capture.WaveFormat)
        {
            BufferDuration = TimeSpan.FromMilliseconds(500),
            DiscardOnBufferOverflow = true
        };

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;

        // Build DSP chain
        ISampleProvider chain = new WaveToSampleProvider(_buffer);
        if (BuildChain != null)
            chain = BuildChain(chain);

        // Level measurement
        var levelMeter = new LevelMeasuringSampleProvider(chain);
        levelMeter.LevelMeasured += (rms, peak) => LevelCallback?.Invoke(rms, peak);

        // Route audio — only split if a cable device is present
        ISampleProvider monitorSource;
        if (cableDevice != null)
        {
            var splitter = new SplitSampleProvider(levelMeter);
            monitorSource = splitter.MonitorOutput;

            _cableOut = new WaveOutEvent();
            _cableOut.DeviceNumber = FindWaveOutDevice(cableDevice.FriendlyName);
            _cableOut.Init(splitter.CableOutput);
            _cableOut.Play();
        }
        else
        {
            monitorSource = levelMeter; // direct pipe, no split needed
        }

        // Monitor output (headphones)
        _monitorVolumeProvider = new VolumeSampleProvider(monitorSource) { Volume = monitorVolume };
        _monitorOut = new WasapiOut(monitorDevice, AudioClientShareMode.Shared, true, 20);
        _monitorOut.PlaybackStopped += OnPlaybackStopped;
        _monitorOut.Init(_monitorVolumeProvider);
        IsRunning = true; // set before Play() so PlaybackStopped errors are not swallowed
        _monitorOut.Play();

        var fmt = _capture.WaveFormat;
        StatusCallback?.Invoke($"Started — {fmt.Encoding} {fmt.BitsPerSample}bit {fmt.SampleRate}Hz {fmt.Channels}ch");
        _capture.StartRecording();
    }

    public void SetMonitorVolume(float volume)
    {
        if (_monitorVolumeProvider != null)
            _monitorVolumeProvider.Volume = volume;
    }

    public void Stop()
    {
        IsRunning = false;
        try { _capture?.StopRecording(); } catch { }
        try { _monitorOut?.Stop(); } catch { }
        try { _cableOut?.Stop(); } catch { }

        _capture?.Dispose();
        _monitorOut?.Dispose();
        _cableOut?.Dispose();

        _capture = null;
        _monitorOut = null;
        _cableOut = null;
        _buffer = null;
        _monitorVolumeProvider = null;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_captureLock)
        {
            _buffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);

            if (!_firstDataReceived)
            {
                _firstDataReceived = true;
                StatusCallback?.Invoke($"Receiving audio — {e.BytesRecorded} bytes/packet");
            }
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
            ErrorCallback?.Invoke($"Capture stopped: {e.Exception.Message}");
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
            ErrorCallback?.Invoke($"Playback error: {e.Exception.Message}");
    }

    private static int FindWaveOutDevice(string friendlyName)
    {
        for (int i = 0; i < WaveOut.DeviceCount; i++)
        {
            var caps = WaveOut.GetCapabilities(i);
            if (friendlyName.Contains(caps.ProductName, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return 0; // fallback to default
    }

    public void Dispose() => Stop();
}
