using FluentFin.Core.Contracts.Services;
using FluentFin.Mpv;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Extensions.Logging;

namespace FluentFin.MediaPlayers;

public sealed class MpvTrackMapper(ILogger<MpvTrackMapper> logger)
{
	public long? FindMpvTrackId(IEnumerable<MpvTrack> tracks, MediaStream_Type type, int jellyfinStreamIndex)
	{
		var mpvType = type switch
		{
			MediaStream_Type.Audio => "audio",
			MediaStream_Type.Subtitle => "sub",
			_ => ""
		};

		if (string.IsNullOrWhiteSpace(mpvType))
		{
			return null;
		}

		var available = tracks.ToList();
		var track = available.FirstOrDefault(x =>
			string.Equals(x.Type, mpvType, StringComparison.OrdinalIgnoreCase) &&
			x.FfmpegIndex == jellyfinStreamIndex);

		if (track is not null)
		{
			logger.LogInformation("Mapped Jellyfin media stream to mpv track. StreamType={StreamType}, JellyfinIndex={JellyfinIndex}, MpvTrackId={MpvTrackId}, FfmpegIndex={FfmpegIndex}",
				type,
				jellyfinStreamIndex,
				track.Id,
				track.FfmpegIndex);
			return track.Id;
		}

		logger.LogWarning("Unable to map Jellyfin media stream to mpv track. StreamType={StreamType}, JellyfinIndex={JellyfinIndex}, AvailableTracks={AvailableTracks}",
			type,
			jellyfinStreamIndex,
			string.Join("; ", available.Select(x => $"{x.Type}:{x.Id}:ff={x.FfmpegIndex}:lang={x.Language}:title={x.Title}")));
		return null;
	}

	public IEnumerable<AudioTrack> ToAudioTracks(IEnumerable<MpvTrack> tracks)
	{
		return tracks
			.Where(x => string.Equals(x.Type, "audio", StringComparison.OrdinalIgnoreCase))
			.Select(x => new AudioTrack((int)(x.FfmpegIndex ?? x.Id), x.Language, x.Title));
	}
}
