using Microsoft.UI.Xaml.Media.Imaging;

namespace FluentFin.Contracts.Services;

public interface IImageSourceCache
{
	BitmapImage Get(Uri uri);

	Task<Uri?> GetCachedUriAsync(Uri uri, CancellationToken cancellationToken = default);

	Task<ImageCacheStats> GetStatsAsync(CancellationToken cancellationToken = default);

	Task ClearAsync(CancellationToken cancellationToken = default);

	void Clear();
}

public sealed record ImageCacheStats(long SizeBytes, int FileCount);
