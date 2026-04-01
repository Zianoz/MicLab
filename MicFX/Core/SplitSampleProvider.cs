using NAudio.Wave;

namespace MicFX.Core;

/// <summary>
/// Reads from a single source ISampleProvider and makes the data available to
/// two independent consumer ISampleProviders (MonitorOutput and CableOutput).
///
/// Both consumers call Read() independently. The first consumer to read a given
/// chunk fills the shared buffer; the second consumer returns the buffered data.
/// If one consumer falls behind, old data is dropped (live audio — latency matters more than completeness).
/// </summary>
public class SplitSampleProvider : IDisposable
{
    private readonly ISampleProvider _source;
    private float[] _sharedBuffer = Array.Empty<float>();
    private int _sharedCount;
    private int _servedMask; // bit 0 = monitor, bit 1 = cable
    private readonly object _lock = new();

    public ISampleProvider MonitorOutput { get; }
    public ISampleProvider CableOutput { get; }

    public SplitSampleProvider(ISampleProvider source)
    {
        _source = source;
        MonitorOutput = new ConsumerProvider(this, 0);
        CableOutput = new ConsumerProvider(this, 1);
    }

    private int ReadForConsumer(int consumerId, float[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (buffer.Length - offset < count)
            throw new ArgumentException("The destination buffer is too small for the requested read.", nameof(buffer));

        lock (_lock)
        {
            int consumerBit = 1 << consumerId;

            // Read a new chunk if:
            // 1. We have no current audio,
            // 2. Both consumers already saw the current chunk, or
            // 3. This consumer already saw it and wants fresh data again.
            if (_sharedCount == 0 || _servedMask == 0b11 || (_servedMask & consumerBit) != 0)
            {
                if (_sharedBuffer.Length < count)
                    _sharedBuffer = new float[count];

                _sharedCount = _source.Read(_sharedBuffer, 0, count);
                _servedMask = 0;
                if (_sharedCount == 0)
                    return 0;
            }

            int toCopy = Math.Min(count, _sharedCount);
            for (int i = 0; i < toCopy; i++)
                buffer[offset + i] = _sharedBuffer[i];

            _servedMask |= consumerBit;
            return toCopy;
        }
    }

    public void Dispose() { }

    private class ConsumerProvider : ISampleProvider
    {
        private readonly SplitSampleProvider _parent;
        private readonly int _id;
        public WaveFormat WaveFormat => _parent._source.WaveFormat;

        public ConsumerProvider(SplitSampleProvider parent, int id)
        {
            _parent = parent;
            _id = id;
        }

        public int Read(float[] buffer, int offset, int count)
            => _parent.ReadForConsumer(_id, buffer, offset, count);
    }
}
