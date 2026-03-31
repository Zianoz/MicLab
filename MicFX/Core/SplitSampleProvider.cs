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
    private int _consumersRead; // how many of the 2 consumers have read this chunk
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
        lock (_lock)
        {
            // If we haven't read new data yet, pull from source
            if (_consumersRead == 0 || _consumersRead == 2)
            {
                if (_sharedBuffer.Length < count)
                    _sharedBuffer = new float[count];

                _sharedCount = _source.Read(_sharedBuffer, 0, count);
                _consumersRead = 0;
            }

            int toCopy = Math.Min(count, _sharedCount);
            Array.Copy(_sharedBuffer, 0, buffer, offset, toCopy);
            _consumersRead++;
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
