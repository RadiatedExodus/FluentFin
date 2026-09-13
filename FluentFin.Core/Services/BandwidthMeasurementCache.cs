using FluentFin.Core.Contracts.Services;

namespace FluentFin.Core.Services;

public sealed class BandwidthMeasurementCache : IBandwidthMeasurementCache
{
	private readonly Dictionary<string, Entry> _entries = [];
	private readonly Lock _lock = new();
	private readonly TimeProvider _timeProvider;

	public BandwidthMeasurementCache()
		: this(TimeProvider.System, TimeSpan.FromMinutes(30))
	{
	}

	public BandwidthMeasurementCache(TimeProvider timeProvider, TimeSpan timeToLive)
	{
		_timeProvider = timeProvider;
		TimeToLive = timeToLive;
	}

	public TimeSpan TimeToLive { get; }

	public bool TryGet(string serverKey, out int bitrate)
	{
		lock (_lock)
		{
			if (_entries.TryGetValue(serverKey, out var entry)
				&& _timeProvider.GetUtcNow() - entry.MeasuredAt < TimeToLive)
			{
				bitrate = entry.Bitrate;
				return true;
			}

			_entries.Remove(serverKey);
		}

		bitrate = 0;
		return false;
	}

	public void Set(string serverKey, int bitrate)
	{
		lock (_lock)
		{
			_entries[serverKey] = new Entry(bitrate, _timeProvider.GetUtcNow());
		}
	}

	public void Clear()
	{
		lock (_lock)
		{
			_entries.Clear();
		}
	}

	private sealed record Entry(int Bitrate, DateTimeOffset MeasuredAt);
}

