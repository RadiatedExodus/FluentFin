using Microsoft.Extensions.Logging;

namespace FluentFin.Core.Playback.Lyrics;

public sealed class LyricsService(IEnumerable<ILyricsProvider> providers, ILogger<LyricsService> logger) : ILyricsService
{
	private readonly SemaphoreSlim _cacheLock = new(1, 1);
	private readonly Dictionary<Guid, LyricsDocument?> _cache = [];

	public async Task<LyricsDocument?> GetLyricsAsync(PlaybackItem item, CancellationToken cancellationToken = default)
	{
		if (item.Kind is not PlaybackKind.Music)
		{
			return null;
		}

		await _cacheLock.WaitAsync(cancellationToken);
		try
		{
			if (_cache.TryGetValue(item.JellyfinId, out var cached))
			{
				logger.LogDebug("Lyrics cache hit. ItemId={ItemId}, HasLyrics={HasLyrics}", item.JellyfinId, cached is not null);
				return cached;
			}
		}
		finally
		{
			_cacheLock.Release();
		}

		LyricsDocument? result = null;
		foreach (var provider in providers)
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				logger.LogInformation("Lyrics provider lookup started. Provider={Provider}, ItemId={ItemId}", provider.Name, item.JellyfinId);
				result = await provider.GetLyricsAsync(item, cancellationToken);
				logger.LogInformation("Lyrics provider lookup completed. Provider={Provider}, ItemId={ItemId}, HasLyrics={HasLyrics}, LineCount={LineCount}",
					provider.Name,
					item.JellyfinId,
					result is not null,
					result?.Lines.Count ?? 0);

				if (result is not null)
				{
					break;
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				logger.LogWarning(ex, "Lyrics provider lookup failed. Provider={Provider}, ItemId={ItemId}", provider.Name, item.JellyfinId);
			}
		}

		await _cacheLock.WaitAsync(cancellationToken);
		try
		{
			_cache[item.JellyfinId] = result;
		}
		finally
		{
			_cacheLock.Release();
		}

		return result;
	}
}
