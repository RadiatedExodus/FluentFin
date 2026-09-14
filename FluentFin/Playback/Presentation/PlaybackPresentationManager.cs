using CommunityToolkit.Mvvm.ComponentModel;
using FluentFin.Core.Playback;
using FluentFin.Core.WebSockets.Messages;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Extensions.Logging;

namespace FluentFin.Playback.Presentation;

public partial class PlaybackPresentationManager(ILogger<PlaybackPresentationManager> logger) : ObservableObject, IPlaybackPresentationManager
{
	private int _requestId;

	[ObservableProperty]
	public partial PlaybackPresentationMode Mode { get; private set; } = PlaybackPresentationMode.None;

	[ObservableProperty]
	public partial VideoOverlayPresentationState? VideoOverlay { get; private set; }

	[ObservableProperty]
	public partial MusicPresentationState? Music { get; private set; }

	public bool HasActivePresentation => Mode is not PlaybackPresentationMode.None;

	public void ShowVideo(object? parameter)
	{
		var requestId = Interlocked.Increment(ref _requestId);
		logger.LogInformation("Showing video playback presentation. RequestId={RequestId}, ParameterType={ParameterType}, ItemId={ItemId}",
			requestId,
			parameter?.GetType().FullName ?? "<null>",
			GetItemId(parameter));

		Music = null;
		VideoOverlay = new VideoOverlayPresentationState(parameter, requestId);
		Mode = PlaybackPresentationMode.VideoOverlay;
		OnPropertyChanged(nameof(Mode));
		OnPropertyChanged(nameof(HasActivePresentation));
	}

	public void ShowMusicPlaceholder(PlaybackItem? currentItem, IReadOnlyList<PlaybackItem>? queueItems = null, string? title = null, PlaybackState state = PlaybackState.Stopped)
	{
		var items = queueItems ?? [];
		logger.LogInformation("Showing placeholder music playback presentation. ItemId={ItemId}, QueueCount={QueueCount}, State={State}",
			currentItem?.JellyfinId,
			items.Count,
			state);

		Music = new MusicPresentationState(currentItem, items, title ?? currentItem?.Title, state);
		Mode = PlaybackPresentationMode.MusicPlaceholder;
		VideoOverlay = null;
		OnPropertyChanged(nameof(HasActivePresentation));
	}

	public Task HideAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (Mode is PlaybackPresentationMode.None)
		{
			return Task.CompletedTask;
		}

		logger.LogInformation("Hiding playback presentation. Mode={Mode}, VideoRequestId={VideoRequestId}, MusicItemId={MusicItemId}",
			Mode,
			VideoOverlay?.RequestId,
			Music?.CurrentItem?.JellyfinId);

		VideoOverlay = null;
		Music = null;
		Mode = PlaybackPresentationMode.None;
		OnPropertyChanged(nameof(HasActivePresentation));

		return Task.CompletedTask;
	}

	private static Guid? GetItemId(object? parameter)
	{
		return parameter switch
		{
			BaseItemDto { Id: { } id } => id,
			PlayQueueUpdate { PlayingItemIndex: >= 0 } update when update.PlayingItemIndex < update.Playlist.Count => update.Playlist[update.PlayingItemIndex].ItemId,
			_ => null
		};
	}
}
