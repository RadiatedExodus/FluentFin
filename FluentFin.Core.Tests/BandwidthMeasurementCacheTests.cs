using FluentFin.Core.Services;

namespace FluentFin.Core.Tests;

public class BandwidthMeasurementCacheTests
{
	[Fact]
	public void TryGet_ReturnsCachedValueWithinTtl()
	{
		var timeProvider = new ManualTimeProvider(DateTimeOffset.UtcNow);
		var cache = new BandwidthMeasurementCache(timeProvider, TimeSpan.FromMinutes(30));

		cache.Set("server", 1_000_000);
		timeProvider.Advance(TimeSpan.FromMinutes(29));

		var found = cache.TryGet("server", out var bitrate);

		Assert.True(found);
		Assert.Equal(1_000_000, bitrate);
	}

	[Fact]
	public void TryGet_ExpiresValueAfterTtl()
	{
		var timeProvider = new ManualTimeProvider(DateTimeOffset.UtcNow);
		var cache = new BandwidthMeasurementCache(timeProvider, TimeSpan.FromMinutes(30));

		cache.Set("server", 1_000_000);
		timeProvider.Advance(TimeSpan.FromMinutes(31));

		var found = cache.TryGet("server", out var bitrate);

		Assert.False(found);
		Assert.Equal(0, bitrate);
	}

	private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
	{
		private DateTimeOffset _utcNow = utcNow;

		public override DateTimeOffset GetUtcNow() => _utcNow;

		public void Advance(TimeSpan timeSpan) => _utcNow += timeSpan;
	}
}

