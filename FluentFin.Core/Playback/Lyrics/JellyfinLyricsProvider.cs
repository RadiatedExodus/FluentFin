using FluentFin.Core.Contracts.Services;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Extensions.Logging;

namespace FluentFin.Core.Playback.Lyrics;

public sealed class JellyfinLyricsProvider(IJellyfinClient jellyfinClient, ILogger<JellyfinLyricsProvider> logger) : ILyricsProvider
{
	public string Name => "Jellyfin";

	public async Task<LyricsDocument?> GetLyricsAsync(PlaybackItem item, CancellationToken cancellationToken = default)
	{
		if (item.Item.Type is not BaseItemDto_Type.Audio || item.Item.MediaType is not BaseItemDto_MediaType.Audio)
		{
			return null;
		}

		var dto = await jellyfinClient.GetLyrics(item.JellyfinId, cancellationToken);
		if (dto?.Lyrics is null || dto.Lyrics.Count == 0)
		{
			logger.LogInformation("Jellyfin returned no lyrics. ItemId={ItemId}", item.JellyfinId);
			return null;
		}

		var timestampUnit = DetectTimestampUnit(dto.Lyrics.Select(x => x.Start).OfType<long>(), item.Duration);
		var lines = dto.Lyrics
			.Select(x => new LyricsLine(x.Text ?? "", x.Start is { } start ? ConvertTimestamp(start, timestampUnit) : null))
			.Where(x => !string.IsNullOrWhiteSpace(x.Text))
			.ToList();

		if (lines.Count == 0)
		{
			return null;
		}

		var hasValidTimestamps = lines.Any(x => x.Start is not null);
		var isSynced = hasValidTimestamps && dto.Metadata?.IsSynced != false;
		logger.LogInformation("Jellyfin lyrics mapped. ItemId={ItemId}, IsSynced={IsSynced}, TimestampUnit={TimestampUnit}, FirstTimestamp={FirstTimestamp}, LastTimestamp={LastTimestamp}, Duration={Duration}",
			item.JellyfinId,
			isSynced,
			timestampUnit,
			lines.FirstOrDefault(x => x.Start is not null)?.Start,
			lines.LastOrDefault(x => x.Start is not null)?.Start,
			item.Duration);

		return new LyricsDocument
		{
			ItemId = item.JellyfinId,
			SyncKind = isSynced ? LyricsSyncKind.Synced : LyricsSyncKind.Unsynced,
			Lines = lines,
			Metadata = new LyricsMetadata
			{
				Title = dto.Metadata?.Title,
				Artist = dto.Metadata?.Artist,
				Album = dto.Metadata?.Album,
				Author = dto.Metadata?.Author,
				Creator = dto.Metadata?.Creator,
				Provider = Name
			}
		};
	}

	private enum LyricTimestampUnit
	{
		Ticks,
		Milliseconds
	}

	private static LyricTimestampUnit DetectTimestampUnit(IEnumerable<long> rawTimestamps, TimeSpan? itemDuration)
	{
		var values = rawTimestamps.Where(x => x >= 0).ToList();
		if (values.Count == 0)
		{
			return LyricTimestampUnit.Ticks;
		}

		var max = values.Max();
		if (itemDuration is { } duration && duration > TimeSpan.Zero)
		{
			var asTicks = TimeSpan.FromTicks(max);
			var asMilliseconds = TimeSpan.FromMilliseconds(max);
			if (asTicks < TimeSpan.FromSeconds(5) && asMilliseconds <= duration.Add(TimeSpan.FromMinutes(5)))
			{
				return LyricTimestampUnit.Milliseconds;
			}
		}

		// LRC-style millisecond values for normal-length songs are usually small enough that
		// treating them as ticks compresses the whole lyric file into less than a second.
		return max < TimeSpan.TicksPerSecond ? LyricTimestampUnit.Milliseconds : LyricTimestampUnit.Ticks;
	}

	private static TimeSpan ConvertTimestamp(long value, LyricTimestampUnit unit)
	{
		return unit switch
		{
			LyricTimestampUnit.Milliseconds => TimeSpan.FromMilliseconds(value),
			_ => TimeSpan.FromTicks(value)
		};
	}
}
