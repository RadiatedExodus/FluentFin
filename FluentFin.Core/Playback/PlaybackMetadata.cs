using FluentFin.Core.Contracts.Services;

namespace FluentFin.Core.Playback;

public abstract record PlaybackMetadata;

public sealed record VideoPlaybackMetadata : PlaybackMetadata
{
	public string? SeriesName { get; init; }
	public int? SeasonNumber { get; init; }
	public int? EpisodeNumber { get; init; }
	public IReadOnlyList<SubtitleTrack> Subtitles { get; init; } = [];
	public IReadOnlyList<AudioTrack> AudioTracks { get; init; } = [];
}

public sealed record MusicPlaybackMetadata : PlaybackMetadata
{
	public string? Album { get; init; }
	public IReadOnlyList<string> Artists { get; init; } = [];
	public int? TrackNumber { get; init; }
	public int? DiscNumber { get; init; }
}
