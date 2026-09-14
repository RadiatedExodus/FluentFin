using System.ComponentModel;
using FluentFin.Core.Playback;

namespace FluentFin.Playback.Presentation;

public interface IPlaybackPresentationManager : INotifyPropertyChanged
{
	PlaybackPresentationMode Mode { get; }
	bool HasActivePresentation { get; }
	VideoOverlayPresentationState? VideoOverlay { get; }
	MusicPresentationState? Music { get; }

	void ShowVideo(object? parameter);
	void ShowMusicPlaceholder(PlaybackItem? currentItem, IReadOnlyList<PlaybackItem>? queueItems = null, string? title = null, PlaybackState state = PlaybackState.Stopped);
	Task HideAsync(CancellationToken cancellationToken = default);
}
