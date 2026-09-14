using FluentFin.Core.Playback;

namespace FluentFin.Playback.Presentation;

public sealed record MusicPresentationState(
	PlaybackItem? CurrentItem,
	IReadOnlyList<PlaybackItem> QueueItems,
	string? Title,
	PlaybackState State);
