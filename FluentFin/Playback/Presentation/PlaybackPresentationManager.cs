using CommunityToolkit.Mvvm.ComponentModel;
using FluentFin.Core.Playback;
using FluentFin.Core.WebSockets.Messages;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Extensions.Logging;

namespace FluentFin.Playback.Presentation;

public partial class PlaybackPresentationManager : ObservableObject, IPlaybackPresentationManager
{
	private readonly IPlaybackService _playbackService;
	private readonly ILogger<PlaybackPresentationManager> _logger;
	private int _requestId;

	public PlaybackPresentationManager(IPlaybackService playbackService, ILogger<PlaybackPresentationManager> logger)
	{
		_playbackService = playbackService;
		_logger = logger;
		_playbackService.PlaybackChanged += (_, _) => EnqueueSyncMusicPresentationFromPlayback();
		_playbackService.QueueChanged += (_, _) => EnqueueSyncMusicPresentationFromPlayback();
	}

	[ObservableProperty]
	public partial PlaybackPresentationMode Mode { get; private set; } = PlaybackPresentationMode.None;

	[ObservableProperty]
	public partial MusicPresentationMode MusicMode { get; private set; } = MusicPresentationMode.Hidden;

	[ObservableProperty]
	public partial VideoOverlayPresentationState? VideoOverlay { get; private set; }

	[ObservableProperty]
	public partial MusicPresentationState? Music { get; private set; }

	public bool HasActivePresentation => Mode is not PlaybackPresentationMode.None || MusicMode is not MusicPresentationMode.Hidden;

	public void ShowVideo(object? parameter)
	{
		var requestId = Interlocked.Increment(ref _requestId);
		_logger.LogInformation("Showing video playback presentation. RequestId={RequestId}, ParameterType={ParameterType}, ItemId={ItemId}",
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
		_logger.LogInformation("Showing placeholder music playback presentation. ItemId={ItemId}, QueueCount={QueueCount}, State={State}",
			currentItem?.JellyfinId,
			items.Count,
			state);

		Music = new MusicPresentationState(currentItem, items, title ?? currentItem?.Title, state);
		MusicMode = MusicPresentationMode.Compact;
		Mode = PlaybackPresentationMode.None;
		VideoOverlay = null;
		OnPropertyChanged(nameof(HasActivePresentation));
	}

	public void ShowMusicCompact()
	{
		if (!IsMusicActive())
		{
			return;
		}

		_logger.LogInformation("Showing compact music presentation. ItemId={ItemId}", _playbackService.CurrentItem?.JellyfinId);
		UpdateMusicState();
		MusicMode = MusicPresentationMode.Compact;
		OnPropertyChanged(nameof(HasActivePresentation));
	}

	public void ShowMusicExpanded()
	{
		if (!IsMusicActive())
		{
			return;
		}

		_logger.LogInformation("Showing expanded music presentation. ItemId={ItemId}", _playbackService.CurrentItem?.JellyfinId);
		UpdateMusicState();
		MusicMode = MusicPresentationMode.Expanded;
		OnPropertyChanged(nameof(HasActivePresentation));
	}

	public void ShowMusicQueue()
	{
		if (!IsMusicActive())
		{
			return;
		}

		_logger.LogInformation("Showing music queue presentation. ItemId={ItemId}, QueueCount={QueueCount}",
			_playbackService.CurrentItem?.JellyfinId,
			_playbackService.Queue.Items.Count);
		UpdateMusicState();
		MusicMode = MusicPresentationMode.Queue;
		OnPropertyChanged(nameof(HasActivePresentation));
	}

	public void ShowMusicLyrics()
	{
		if (!IsMusicActive())
		{
			return;
		}

		_logger.LogInformation("Showing music lyrics presentation. ItemId={ItemId}",
			_playbackService.CurrentItem?.JellyfinId);
		UpdateMusicState();
		MusicMode = MusicPresentationMode.Lyrics;
		OnPropertyChanged(nameof(HasActivePresentation));
	}

	public void HideMusic()
	{
		if (MusicMode is MusicPresentationMode.Hidden)
		{
			return;
		}

		_logger.LogInformation("Hiding music presentation. Mode={MusicMode}, ItemId={ItemId}", MusicMode, Music?.CurrentItem?.JellyfinId);
		Music = null;
		MusicMode = MusicPresentationMode.Hidden;
		OnPropertyChanged(nameof(HasActivePresentation));
	}

	public Task HideAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (Mode is PlaybackPresentationMode.None && MusicMode is MusicPresentationMode.Hidden)
		{
			return Task.CompletedTask;
		}

		_logger.LogInformation("Hiding playback presentation. Mode={Mode}, MusicMode={MusicMode}, VideoRequestId={VideoRequestId}, MusicItemId={MusicItemId}",
			Mode,
			MusicMode,
			VideoOverlay?.RequestId,
			Music?.CurrentItem?.JellyfinId);

		if (Mode is PlaybackPresentationMode.VideoOverlay)
		{
			VideoOverlay = null;
			Mode = PlaybackPresentationMode.None;
		}
		else if (MusicMode is MusicPresentationMode.Expanded or MusicPresentationMode.Queue or MusicPresentationMode.Lyrics)
		{
			MusicMode = IsMusicActive() ? MusicPresentationMode.Compact : MusicPresentationMode.Hidden;
			if (MusicMode is MusicPresentationMode.Hidden)
			{
				Music = null;
			}
		}
		else
		{
			HideMusic();
		}

		OnPropertyChanged(nameof(HasActivePresentation));

		return Task.CompletedTask;
	}

	private void SyncMusicPresentationFromPlayback()
	{
		if (IsMusicActive())
		{
			UpdateMusicState();
			if (MusicMode is MusicPresentationMode.Hidden)
			{
				MusicMode = MusicPresentationMode.Compact;
				OnPropertyChanged(nameof(HasActivePresentation));
			}

			return;
		}

		if (MusicMode is not MusicPresentationMode.Hidden)
		{
			HideMusic();
		}
	}

	private bool IsMusicActive() =>
		_playbackService.CurrentKind is PlaybackKind.Music &&
		_playbackService.CurrentItem is not null &&
		_playbackService.State is not PlaybackState.Stopped and not PlaybackState.Ended;

	private void UpdateMusicState()
	{
		if (_playbackService.CurrentKind is not PlaybackKind.Music)
		{
			return;
		}

		Music = new MusicPresentationState(
			_playbackService.CurrentItem,
			_playbackService.Queue.Items.ToList(),
			_playbackService.CurrentItem?.Title,
			_playbackService.State);
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

	private void EnqueueSyncMusicPresentationFromPlayback()
	{
		var dispatcher = App.MainWindow.DispatcherQueue;
		if (dispatcher.HasThreadAccess)
		{
			SyncMusicPresentationFromPlayback();
			return;
		}

		dispatcher.TryEnqueue(SyncMusicPresentationFromPlayback);
	}
}
