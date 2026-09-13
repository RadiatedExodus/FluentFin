using Jellyfin.Sdk.Generated.Models;

namespace FluentFin.Core.Playback;

public sealed record MediaSource(
	Uri Uri,
	string? PlaybackSessionId = null,
	string? MediaSourceId = null,
	PlaybackProgressInfo_PlayMethod? PlayMethod = null,
	MediaSourceInfo? MediaSourceInfo = null,
	int DefaultAudioStreamIndex = 0);
