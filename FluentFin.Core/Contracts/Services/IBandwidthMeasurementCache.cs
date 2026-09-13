namespace FluentFin.Core.Contracts.Services;

public interface IBandwidthMeasurementCache
{
	TimeSpan TimeToLive { get; }

	bool TryGet(string serverKey, out int bitrate);

	void Set(string serverKey, int bitrate);

	void Clear();
}

