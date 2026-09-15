using System.Web;
using FluentFin.Core.Contracts.Services;
using Flurl;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Extensions.Logging;

namespace FluentFin.Core.Services;



public partial class JellyfinClient
{

	public async Task<MediaResponse?> GetMediaUrl(BaseItemDto dto, CancellationToken cancellationToken = default)
	{
		if (dto.Id is not { } id)
		{
			logger.LogWarning("Jellyfin media URL resolution skipped because item id is missing. Type={Type}, MediaType={MediaType}", dto.Type, dto.MediaType);
			return null;
		}

		if (!IsSupportedMediaItem(dto))
		{
			logger.LogInformation("Jellyfin media URL resolution skipped unsupported item. ItemId={ItemId}, Type={Type}, MediaType={MediaType}", id, dto.Type, dto.MediaType);
			return null;
		}

		var bitRate = 0;
		if (dto.MediaType is BaseItemDto_MediaType.Video)
		{
			var endPointInfo = await EndpointInfo();
			cancellationToken.ThrowIfCancellationRequested();
			bitRate = endPointInfo?.IsInNetwork == true ? 0 : await GetCachedBitrate(cancellationToken);
		}

		var startTime = TimeProvider.System.GetTimestamp();
		var playbackInfoDto = new PlaybackInfoDto
		{
			UserId = UserId,
			AutoOpenLiveStream = true,
			DeviceProfile = deviceProfileFactory.GetDeviceProfile(),
			StartTimeTicks = startTime,
			EnableDirectPlay = true,
			EnableDirectStream = true,
			EnableTranscoding = true,
		};

		if (bitRate != 0)
		{
			playbackInfoDto.MaxStreamingBitrate = bitRate;
		}

		var playbackInfo = await _jellyfinApiClient.Items[id].PlaybackInfo.PostAsync(playbackInfoDto, cancellationToken: cancellationToken);

		if (playbackInfo is null or { PlaySessionId: null } or { MediaSources: null })
		{
			logger.LogWarning("Jellyfin PlaybackInfo returned no media sources. ItemId={ItemId}, Type={Type}, MediaType={MediaType}", id, dto.Type, dto.MediaType);
			return null;
		}

		var sessionId = playbackInfo.PlaySessionId;
		var itemIdText = id.ToString("N");
		var mediaSource = playbackInfo.MediaSources
			.Where(IsPlayableMediaSource)
			.OrderByDescending(x => string.Equals(x.Id, itemIdText, StringComparison.OrdinalIgnoreCase))
			.ThenByDescending(x => x.SupportsDirectPlay == true)
			.ThenByDescending(x => x.SupportsDirectStream == true)
			.ThenByDescending(x => x.SupportsTranscoding == true)
			.FirstOrDefault();

		if (mediaSource is null)
		{
			logger.LogWarning("Jellyfin PlaybackInfo contained no playable media source. ItemId={ItemId}, Type={Type}, MediaType={MediaType}, SourceCount={SourceCount}",
				id,
				dto.Type,
				dto.MediaType,
				playbackInfo.MediaSources.Count);
			return null;
		}

		var response = BuildMediaResponse(dto, mediaSource, sessionId, startTime);
		if (response is null)
		{
			logger.LogWarning("Jellyfin media URL resolution failed after selecting source. ItemId={ItemId}, Type={Type}, MediaType={MediaType}, MediaSourceId={MediaSourceId}, SupportsDirectPlay={SupportsDirectPlay}, SupportsDirectStream={SupportsDirectStream}, SupportsTranscoding={SupportsTranscoding}",
				id,
				dto.Type,
				dto.MediaType,
				mediaSource.Id,
				mediaSource.SupportsDirectPlay,
				mediaSource.SupportsDirectStream,
				mediaSource.SupportsTranscoding);
			return null;
		}

		logger.LogInformation("Jellyfin media URL resolved. ItemId={ItemId}, Type={Type}, MediaType={MediaType}, MediaSourceId={MediaSourceId}, Container={Container}, PlayMethod={PlayMethod}, HasTranscodingUrl={HasTranscodingUrl}",
			id,
			dto.Type,
			dto.MediaType,
			response.MediaSourceId,
			mediaSource.Container,
			response.PlayMethod,
			!string.IsNullOrEmpty(mediaSource.TranscodingUrl));
		return response;
	}

	public async Task<IReadOnlyList<BaseItemDto>> GetPlayableAudioItems(BaseItemDto dto, CancellationToken cancellationToken = default)
	{
		if (dto.Id is not { } id)
		{
			return [];
		}

		logger.LogInformation("Jellyfin playable audio expansion started. ItemId={ItemId}, Type={Type}, MediaType={MediaType}", id, dto.Type, dto.MediaType);
		var items = dto.Type switch
		{
			BaseItemDto_Type.Audio when dto.MediaType is BaseItemDto_MediaType.Audio => [dto],
			BaseItemDto_Type.MusicAlbum => await GetAlbumAudioItems(id, cancellationToken),
			BaseItemDto_Type.MusicArtist => await GetArtistAudioItems(id, cancellationToken),
			BaseItemDto_Type.Playlist => await GetPlaylistAudioItems(id, cancellationToken),
			_ => []
		};
		logger.LogInformation("Jellyfin playable audio expansion completed. ItemId={ItemId}, Type={Type}, Count={Count}", id, dto.Type, items.Count);
		return items;
	}

	public async Task<LyricDto?> GetLyrics(Guid audioItemId, CancellationToken cancellationToken = default)
	{
		try
		{
			logger.LogInformation("Jellyfin lyrics request started. ItemId={ItemId}", audioItemId);
			var lyrics = await _jellyfinApiClient.Audio[audioItemId].Lyrics.GetAsync(cancellationToken: cancellationToken);
			logger.LogInformation("Jellyfin lyrics request completed. ItemId={ItemId}, HasLyrics={HasLyrics}, LineCount={LineCount}, IsSynced={IsSynced}",
				audioItemId,
				lyrics?.Lyrics?.Count > 0,
				lyrics?.Lyrics?.Count ?? 0,
				lyrics?.Metadata?.IsSynced);
			return lyrics;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			logger.LogInformation(ex, "Jellyfin lyrics unavailable. ItemId={ItemId}", audioItemId);
			return null;
		}
	}

	private async Task<IReadOnlyList<BaseItemDto>> GetAlbumAudioItems(Guid albumId, CancellationToken cancellationToken)
	{
		var response = await _jellyfinApiClient.Items.GetAsync(x =>
		{
			var query = x.QueryParameters;
			query.UserId = UserId;
			query.ParentId = albumId;
			query.Recursive = true;
			query.IncludeItemTypes = [BaseItemKind.Audio];
			query.MediaTypes = [MediaType.Audio];
			query.SortBy = [ItemSortBy.ParentIndexNumber, ItemSortBy.IndexNumber, ItemSortBy.SortName];
			query.SortOrder = [SortOrder.Ascending];
			query.Fields = [ItemFields.PrimaryImageAspectRatio, ItemFields.MediaStreams, ItemFields.MediaSourceCount, ItemFields.Path];
			query.EnableImageTypes = [ImageType.Primary, ImageType.Backdrop, ImageType.Thumb];
			query.ImageTypeLimit = 1;
		}, cancellationToken);

		return response?.Items ?? [];
	}

	private async Task<IReadOnlyList<BaseItemDto>> GetArtistAudioItems(Guid artistId, CancellationToken cancellationToken)
	{
		var response = await GetArtistAudioItems(artistId, useAlbumArtistIds: false, cancellationToken);
		if (response.Count > 0)
		{
			return response;
		}

		return await GetArtistAudioItems(artistId, useAlbumArtistIds: true, cancellationToken);
	}

	private async Task<IReadOnlyList<BaseItemDto>> GetArtistAudioItems(Guid artistId, bool useAlbumArtistIds, CancellationToken cancellationToken)
	{
		var response = await _jellyfinApiClient.Items.GetAsync(x =>
		{
			var query = x.QueryParameters;
			query.UserId = UserId;
			query.Recursive = true;
			query.IncludeItemTypes = [BaseItemKind.Audio];
			query.MediaTypes = [MediaType.Audio];
			query.SortBy = [ItemSortBy.Album, ItemSortBy.ParentIndexNumber, ItemSortBy.IndexNumber, ItemSortBy.SortName];
			query.SortOrder = [SortOrder.Ascending];
			query.Fields = [ItemFields.PrimaryImageAspectRatio, ItemFields.MediaStreams, ItemFields.MediaSourceCount, ItemFields.Path];
			query.EnableImageTypes = [ImageType.Primary, ImageType.Backdrop, ImageType.Thumb];
			query.ImageTypeLimit = 1;
			if (useAlbumArtistIds)
			{
				query.AlbumArtistIds = [artistId];
			}
			else
			{
				query.ArtistIds = [artistId];
			}
		}, cancellationToken);

		return response?.Items ?? [];
	}

	private async Task<IReadOnlyList<BaseItemDto>> GetPlaylistAudioItems(Guid playlistId, CancellationToken cancellationToken)
	{
		var response = await _jellyfinApiClient.Playlists[playlistId].Items.GetAsync(x =>
		{
			var query = x.QueryParameters;
			query.UserId = UserId;
			query.Fields = [ItemFields.PrimaryImageAspectRatio, ItemFields.MediaStreams, ItemFields.MediaSourceCount, ItemFields.Path];
			query.EnableImageTypes = [ImageType.Primary, ImageType.Backdrop, ImageType.Thumb];
			query.ImageTypeLimit = 1;
		}, cancellationToken);

		return response?.Items?.Where(x => x.Type is BaseItemDto_Type.Audio && x.MediaType is BaseItemDto_MediaType.Audio).ToList() ?? [];
	}

	private static bool IsSupportedMediaItem(BaseItemDto dto)
	{
		return dto switch
		{
			{ MediaType: BaseItemDto_MediaType.Video, Type: BaseItemDto_Type.Episode or BaseItemDto_Type.Movie } => true,
			{ MediaType: BaseItemDto_MediaType.Audio, Type: BaseItemDto_Type.Audio } => true,
			_ => false
		};
	}

	private static bool IsPlayableMediaSource(MediaSourceInfo mediaSource)
	{
		return mediaSource.SupportsDirectPlay == true
			|| mediaSource.SupportsDirectStream == true
			|| (!string.IsNullOrEmpty(mediaSource.TranscodingUrl) && mediaSource.SupportsTranscoding == true);
	}

	private MediaResponse? BuildMediaResponse(BaseItemDto dto, MediaSourceInfo mediaSource, string sessionId, long startTime)
	{
		if (!string.IsNullOrEmpty(mediaSource.TranscodingUrl) && mediaSource.SupportsTranscoding == true)
		{
			var uri = new Uri(HttpUtility.UrlDecode(BaseUrl.AppendPathSegment(mediaSource.TranscodingUrl)));
			var playMethod = mediaSource.SupportsDirectStream == true
				? PlaybackProgressInfo_PlayMethod.DirectStream
				: PlaybackProgressInfo_PlayMethod.Transcode;
			return new(AddApiKey(uri), playMethod, sessionId, mediaSource.Id ?? "", mediaSource);
		}

		if (mediaSource.SupportsDirectPlay != true || dto.Id is not { } id)
		{
			return null;
		}

		return dto.MediaType switch
		{
			BaseItemDto_MediaType.Video => BuildVideoDirectPlayResponse(id, mediaSource, sessionId, startTime),
			BaseItemDto_MediaType.Audio => BuildAudioDirectPlayResponse(id, mediaSource, sessionId, startTime),
			_ => null
		};
	}

	private MediaResponse BuildVideoDirectPlayResponse(Guid id, MediaSourceInfo mediaSource, string sessionId, long startTime)
	{
		var info = _jellyfinApiClient.Videos[id].Stream.ToGetRequestInformation(x =>
		{
			var query = x.QueryParameters;
			query.Container = mediaSource.Container;
			query.PlaySessionId = sessionId;
			query.Static = true;
			query.Tag = mediaSource.ETag;
			query.StartTimeTicks = startTime;
		});

		return new(AddApiKey(info.URI), PlaybackProgressInfo_PlayMethod.DirectPlay, sessionId, mediaSource.Id ?? "", mediaSource);
	}

	private MediaResponse BuildAudioDirectPlayResponse(Guid id, MediaSourceInfo mediaSource, string sessionId, long startTime)
	{
		var info = _jellyfinApiClient.Audio[id].Stream.ToGetRequestInformation(x =>
		{
			var query = x.QueryParameters;
			query.Container = mediaSource.Container;
			query.PlaySessionId = sessionId;
			query.MediaSourceId = mediaSource.Id;
			query.Static = true;
			query.Tag = mediaSource.ETag;
			query.StartTimeTicks = startTime;
		});

		return new(AddApiKey(info.URI), PlaybackProgressInfo_PlayMethod.DirectPlay, sessionId, mediaSource.Id ?? "", mediaSource);
	}

	public Uri GetImage(BaseItemDto item, ImageInfo info)
	{
		return BaseUrl.AppendPathSegment($"/Items/{item.Id}/Images/{info.ImageType}").SetQueryParam("tag", info.ImageTag).ToUri();
	}

	public async Task Playing(BaseItemDto dto)
	{
		try
		{
			await _jellyfinApiClient.Sessions.Playing.PostAsync(new PlaybackStartInfo
			{
				ItemId = dto?.Id,
			});
		}
		catch (Exception ex)
		{
			logger.LogError(ex, @"Unhandled exception");
		}
	}

	public async Task ReportPlaybackStarted(PlaybackProgressInfo info)
	{
		try
		{
			logger.LogInformation("Jellyfin playback start report started. ItemId={ItemId}, MediaSourceId={MediaSourceId}, SessionId={SessionId}, PlayMethod={PlayMethod}, PositionTicks={PositionTicks}, IsPaused={IsPaused}, IsMuted={IsMuted}",
				info.ItemId,
				info.MediaSourceId,
				info.SessionId,
				info.PlayMethod,
				info.PositionTicks,
				info.IsPaused,
				info.IsMuted);
			await _jellyfinApiClient.Sessions.Playing.PostAsync(new PlaybackStartInfo
			{
				ItemId = info.ItemId,
				MediaSourceId = info.MediaSourceId,
				PositionTicks = info.PositionTicks,
				SessionId = info.SessionId,
				PlaySessionId = info.PlaySessionId,
				IsPaused = info.IsPaused,
				IsMuted = info.IsMuted,
				PlaybackStartTimeTicks = info.PlaybackStartTimeTicks,
				AudioStreamIndex = info.AudioStreamIndex,
				SubtitleStreamIndex = info.SubtitleStreamIndex,
				PlayMethod = info.PlayMethod is { } playMethod
					? Enum.Parse<PlaybackStartInfo_PlayMethod>(playMethod.ToString())
					: null,
			});
			_currentItem = info;
			logger.LogInformation("Jellyfin playback start report completed. ItemId={ItemId}, MediaSourceId={MediaSourceId}, SessionId={SessionId}", info.ItemId, info.MediaSourceId, info.SessionId);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Jellyfin playback start report failed. ItemId={ItemId}, MediaSourceId={MediaSourceId}, SessionId={SessionId}", info.ItemId, info.MediaSourceId, info.SessionId);
		}
	}

	public async Task Progress(PlaybackProgressInfo info)
	{
		try
		{
			logger.LogInformation("Jellyfin playback progress report started. ItemId={ItemId}, MediaSourceId={MediaSourceId}, SessionId={SessionId}, PositionTicks={PositionTicks}, IsPaused={IsPaused}",
				info.ItemId,
				info.MediaSourceId,
				info.SessionId,
				info.PositionTicks,
				info.IsPaused);
			await _jellyfinApiClient.Sessions.Playing.Progress.PostAsync(info);
			_currentItem = info;
			logger.LogInformation("Jellyfin playback progress report completed. ItemId={ItemId}, MediaSourceId={MediaSourceId}, SessionId={SessionId}", info.ItemId, info.MediaSourceId, info.SessionId);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Jellyfin playback progress report failed. ItemId={ItemId}, MediaSourceId={MediaSourceId}, SessionId={SessionId}", info.ItemId, info.MediaSourceId, info.SessionId);
		}
	}

	public async Task Stop(PlaybackStopInfo info)
	{
		try
		{
			logger.LogInformation("Jellyfin playback stop report started. ItemId={ItemId}, MediaSourceId={MediaSourceId}, SessionId={SessionId}, PositionTicks={PositionTicks}",
				info.ItemId,
				info.MediaSourceId,
				info.SessionId,
				info.PositionTicks);
			await _jellyfinApiClient.Sessions.Playing.Stopped.PostAsync(info);
			_currentItem = null;
			logger.LogInformation("Jellyfin playback stop report completed. ItemId={ItemId}, MediaSourceId={MediaSourceId}, SessionId={SessionId}", info.ItemId, info.MediaSourceId, info.SessionId);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Jellyfin playback stop report failed. ItemId={ItemId}, MediaSourceId={MediaSourceId}, SessionId={SessionId}", info.ItemId, info.MediaSourceId, info.SessionId);
		}
	}

	public async Task Stop()
	{
		if (_currentItem is null)
		{
			return;
		}

		var info = new PlaybackStopInfo
		{
			ItemId = _currentItem.ItemId,
			MediaSourceId = _currentItem.MediaSourceId,
			PositionTicks = _currentItem.PositionTicks,
			SessionId = _currentItem.SessionId,
		};

		await Stop(info);
	}

	public async Task<bool> RunAudioPlaybackSmokeAsync(Guid itemId, CancellationToken cancellationToken = default)
	{
		logger.LogInformation("Jellyfin audio playback smoke started. RequestedItemId={RequestedItemId}", itemId);
		var item = await GetItem(itemId);
		cancellationToken.ThrowIfCancellationRequested();
		if (item is null)
		{
			logger.LogWarning("Jellyfin audio playback smoke failed because item was not found. RequestedItemId={RequestedItemId}", itemId);
			return false;
		}

		var audioItems = await GetPlayableAudioItems(item, cancellationToken);
		var audio = audioItems.FirstOrDefault();
		if (audio?.Id is not { } audioId)
		{
			logger.LogWarning("Jellyfin audio playback smoke failed because no playable audio item was found. RequestedItemId={RequestedItemId}, Type={Type}", itemId, item.Type);
			return false;
		}

		var media = await GetMediaUrl(audio, cancellationToken);
		if (media is null)
		{
			logger.LogWarning("Jellyfin audio playback smoke failed because no media URL was resolved. RequestedItemId={RequestedItemId}, AudioItemId={AudioItemId}", itemId, audioId);
			return false;
		}

		var progressInfo = new PlaybackProgressInfo
		{
			ItemId = audioId,
			MediaSourceId = media.MediaSourceId,
			SessionId = media.PlaybackSessionId,
			PlaySessionId = media.PlaybackSessionId,
			PlayMethod = media.PlayMethod,
			PositionTicks = 0,
			IsPaused = true,
			IsMuted = false,
			PlaybackStartTimeTicks = TimeProvider.System.GetTimestamp(),
		};

		await ReportPlaybackStarted(progressInfo);
		cancellationToken.ThrowIfCancellationRequested();
		await Progress(progressInfo);
		cancellationToken.ThrowIfCancellationRequested();
		await Stop(new PlaybackStopInfo
		{
			ItemId = progressInfo.ItemId,
			MediaSourceId = progressInfo.MediaSourceId,
			PositionTicks = progressInfo.PositionTicks,
			SessionId = progressInfo.SessionId,
		});
		logger.LogInformation("Jellyfin audio playback smoke completed. RequestedItemId={RequestedItemId}, AudioItemId={AudioItemId}, MediaSourceId={MediaSourceId}, PlayMethod={PlayMethod}",
			itemId,
			audioId,
			media.MediaSourceId,
			media.PlayMethod);
		return true;
	}

	public Uri? GetStreamUrl(BaseItemDto dto)
	{
		if (dto.Id is not { } id)
		{
			return null;
		}

		var info = _jellyfinApiClient.Items[id].Download.ToGetRequestInformation();
		return AddApiKey(info.URI);
	}

	public async Task<MediaSegmentDtoQueryResult?> GetMediaSegments(BaseItemDto dto, MediaSegmentType[]? types = null)
	{
		if (dto.Id is not { } id)
		{
			return null;
		}

		try
		{
			return await _jellyfinApiClient.MediaSegments[id].GetAsync(x => x.QueryParameters.IncludeSegmentTypes = types);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, @"Unhandled exception");
			return null;
		}
	}

	public async Task<List<SessionInfoDto>> GetControllableSessions()
	{
		try
		{
			return await _jellyfinApiClient.Sessions.GetAsync(x => x.QueryParameters.ControllableByUserId = UserId) ?? [];
		}
		catch (Exception ex)
		{
			logger.LogError(ex, @"Unhandled exception");
			return [];
		}
	}

	public async Task PlayOnSession(string sessionId, params IEnumerable<Guid?> items)
	{
		await _jellyfinApiClient.Sessions[sessionId].Playing.PostAsync(x =>
		{
			x.QueryParameters.ItemIds = [.. items];
			x.QueryParameters.PlayCommand = Jellyfin.Sdk.Generated.Sessions.Item.Playing.PlayCommand.PlayNow;
		});
	}

	public async Task TogglePlayPause(string sessionId)
	{
		try
		{
			await _jellyfinApiClient.Sessions[sessionId].Playing["PlayPause"].PostAsync();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, @"Unhandled exception");
			return;
		}
	}

	public async Task Stop(string sessionId)
	{
		try
		{
			await _jellyfinApiClient.Sessions[sessionId].Playing["Stop"].PostAsync();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, @"Unhandled exception");
			return;
		}
	}

	public Uri GetTrickplayImage(BaseItemDto dto, int index, int resolution)
	{
		return new Uri(HttpUtility.HtmlDecode(BaseUrl.AppendPathSegment($"/Videos/{dto?.Id}/Trickplay/{resolution}/{index}.jpg").AppendQueryParam("ApiKey", _token).ToString()));
	}

}
