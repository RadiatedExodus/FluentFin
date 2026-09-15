namespace FluentFin.Core.Contracts.Services;

public interface IClientImageCacheMaintenance
{
	Task ClearAsync(CancellationToken cancellationToken = default);

	Task<ClientImageCacheStats> GetStatsAsync(CancellationToken cancellationToken = default);
}

public sealed record ClientImageCacheStats(long SizeBytes, int FileCount);
