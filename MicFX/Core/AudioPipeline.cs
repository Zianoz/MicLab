using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MicFX.Core;

/// <summary>
/// Wires: WasapiCapture -> [DSP chain] -> SplitSampleProvider
///   -> WasapiOut (self-monitor / headphones)
///   -> WaveOutEvent (virtual cable / VB-CABLE)
/// </summary>
public class AudioPipeline : IDisposable
{
    private static readonly WaveFormat ProcessingFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);

    private WasapiCapture? _capture;
    private WasapiOut? _monitorOut;
    private WasapiOut? _cableOut;
    private BufferedWaveProvider? _buffer;
    private MonitorGainSampleProvider? _monitorVolumeProvider;

    /// <summary>Inject DSP processors. Source -> returns processed chain head.</summary>
    public Func<ISampleProvider, ISampleProvider>? BuildChain { get; set; }

    /// <summary>Called from audio thread with raw capture (rms, peak).</summary>
    public Action<float, float>? InputLevelCallback { get; set; }

    /// <summary>Called from audio thread with processed output (rms, peak).</summary>
    public Action<float, float>? OutputLevelCallback { get; set; }

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

        _capture = CreateCapture(inputDevice);
        _firstDataReceived = false;
        _buffer = new BufferedWaveProvider(_capture.WaveFormat)
        {
            BufferDuration = TimeSpan.FromMilliseconds(100),
            DiscardOnBufferOverflow = true
        };

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;

        // Capture in the device's native shared-mode format, then normalize to MicFX's
        // internal 48kHz mono float pipeline for RNNoise and the rest of the DSP chain.
        ISampleProvider chain = CreateProcessingSource(_buffer);
        if (BuildChain != null)
            chain = BuildChain(chain);

        var levelMeter = new LevelMeasuringSampleProvider(chain);
        levelMeter.LevelMeasured += (rms, peak) => OutputLevelCallback?.Invoke(rms, peak);

        ISampleProvider monitorSource;
        if (cableDevice != null)
        {
            var splitter = new SplitSampleProvider(levelMeter);
            monitorSource = CreateRenderSource(splitter.MonitorOutput, monitorDevice);
            var cableSource = CreateRenderSource(splitter.CableOutput, cableDevice);

            _cableOut = new WasapiOut(cableDevice, AudioClientShareMode.Shared, true, 10);
            _cableOut.PlaybackStopped += OnPlaybackStopped;
            _cableOut.Init(cableSource);
            _cableOut.Play();
        }
        else
        {
            monitorSource = CreateRenderSource(levelMeter, monitorDevice);
        }

        _monitorVolumeProvider = new MonitorGainSampleProvider(monitorSource) { Gain = monitorVolume };
        _monitorOut = new WasapiOut(monitorDevice, AudioClientShareMode.Shared, true, 10);
        _monitorOut.PlaybackStopped += OnPlaybackStopped;
        _monitorOut.Init(_monitorVolumeProvider);
        IsRunning = true;
        _monitorOut.Play();

        var fmt = _capture.WaveFormat;
        StatusCallback?.Invoke($"Started - capture {fmt.Encoding} {fmt.BitsPerSample}bit {fmt.SampleRate}Hz {fmt.Channels}ch, processing {ProcessingFormat.SampleRate}Hz {ProcessingFormat.Channels}ch");
        _capture.StartRecording();
    }

    public void SetMonitorVolume(float volume)
    {
        if (_monitorVolumeProvider != null)
            _monitorVolumeProvider.Gain = volume;
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
            if (e.BytesRecorded <= 0)
                return;

            MeasureInputLevel(e.Buffer, e.BytesRecorded);
            _buffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);

            if (_firstDataReceived)
                return;

            _firstDataReceived = true;
            StatusCallback?.Invoke($"Receiving audio - {e.BytesRecorded} bytes/packet");
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

    private static WasapiCapture CreateCapture(MMDevice inputDevice)
    {
        return new WasapiCapture(inputDevice, false, 20)
        {
            ShareMode = AudioClientShareMode.Shared
        };
    }

    private static ISampleProvider CreateProcessingSource(IWaveProvider source)
    {
        ISampleProvider current = source.ToSampleProvider();

        if (current.WaveFormat.Channels != ProcessingFormat.Channels)
            current = new MonoDownmixSampleProvider(current);

        if (current.WaveFormat.SampleRate != ProcessingFormat.SampleRate)
            current = new WdlResamplingSampleProvider(current, ProcessingFormat.SampleRate);

        if (current.WaveFormat.Channels != ProcessingFormat.Channels || current.WaveFormat.SampleRate != ProcessingFormat.SampleRate)
            throw new InvalidOperationException($"Unable to normalize capture stream to {ProcessingFormat.SampleRate}Hz mono.");

        return current;
    }

    private static ISampleProvider CreateRenderSource(ISampleProvider source, MMDevice device)
    {
        var mixFormat = device.AudioClient.MixFormat;
        ISampleProvider current = source;

        if (current.WaveFormat.Channels != mixFormat.Channels)
            current = new MultiChannelUpmixSampleProvider(current, mixFormat.Channels);

        if (current.WaveFormat.SampleRate != mixFormat.SampleRate)
            current = new WdlResamplingSampleProvider(current, mixFormat.SampleRate);

        return current;
    }

    private void MeasureInputLevel(byte[] buffer, int bytesRecorded)
    {
        var format = _capture?.WaveFormat;
        if (format == null || format.BitsPerSample != 32 || format.Encoding != WaveFormatEncoding.IeeeFloat)
            return;

        int channels = Math.Max(format.Channels, 1);
        int sampleCount = bytesRecorded / sizeof(float);
        int frameCount = sampleCount / channels;
        if (frameCount <= 0)
            return;

        var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(
            new ReadOnlySpan<byte>(buffer, 0, bytesRecorded));

        float sumSq = 0f;
        float peak = 0f;
        for (int frame = 0; frame < frameCount; frame++)
        {
            float abs = MathF.Abs(samples[frame * channels]);
            sumSq += abs * abs;
            if (abs > peak)
                peak = abs;
        }

        InputLevelCallback?.Invoke(MathF.Sqrt(sumSq / frameCount), peak);
    }

    private sealed class MonoDownmixSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly WaveFormat _waveFormat;
        private float[] _sourceBuffer = Array.Empty<float>();

        public WaveFormat WaveFormat => _waveFormat;

        public MonoDownmixSampleProvider(ISampleProvider source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int sourceChannels = _source.WaveFormat.Channels;
            int sourceSamplesNeeded = count * sourceChannels;
            if (_sourceBuffer.Length < sourceSamplesNeeded)
                _sourceBuffer = new float[sourceSamplesNeeded];

            int sourceRead = _source.Read(_sourceBuffer, 0, sourceSamplesNeeded);
            int framesRead = sourceRead / sourceChannels;

            for (int frame = 0; frame < framesRead; frame++)
            {
                int frameOffset = frame * sourceChannels;
                buffer[offset + frame] = _sourceBuffer[frameOffset];
            }

            return framesRead;
        }
    }

    private sealed class MultiChannelUpmixSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly WaveFormat _waveFormat;
        private float[] _sourceBuffer = Array.Empty<float>();

        public WaveFormat WaveFormat => _waveFormat;

        public MultiChannelUpmixSampleProvider(ISampleProvider source, int targetChannels)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            if (targetChannels <= 0)
                throw new ArgumentOutOfRangeException(nameof(targetChannels));

            _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, targetChannels);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int targetChannels = _waveFormat.Channels;
            int framesRequested = count / targetChannels;
            if (framesRequested <= 0)
                return 0;

            int sourceSamplesNeeded = framesRequested * _source.WaveFormat.Channels;
            if (_sourceBuffer.Length < sourceSamplesNeeded)
                _sourceBuffer = new float[sourceSamplesNeeded];

            int sourceRead = _source.Read(_sourceBuffer, 0, sourceSamplesNeeded);
            int framesRead = sourceRead / _source.WaveFormat.Channels;

            for (int frame = 0; frame < framesRead; frame++)
            {
                float sample = _sourceBuffer[frame * _source.WaveFormat.Channels];
                int destOffset = offset + (frame * targetChannels);
                for (int ch = 0; ch < targetChannels; ch++)
                    buffer[destOffset + ch] = sample;
            }

            return framesRead * targetChannels;
        }
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Linear gain below unity; tanh soft saturation above — avoids harsh WASAPI hard-clipping
    /// when the user boosts the monitor above 100%.
    /// </summary>
    private sealed class MonitorGainSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        public WaveFormat WaveFormat => _source.WaveFormat;
        public float Gain { get; set; } = 1f;

        public MonitorGainSampleProvider(ISampleProvider source) => _source = source;

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            float gain = Gain;
            if (gain <= 1.0f)
            {
                for (int i = offset; i < offset + read; i++)
                    buffer[i] *= gain;
            }
            else
            {
                // Amplify then soft-clip via tanh so peaks saturate smoothly to ±1
                for (int i = offset; i < offset + read; i++)
                    buffer[i] = MathF.Tanh(buffer[i] * gain);
            }
            return read;
        }
    }
}
