using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeftSharp.Windows.Input.Keyboard;
using FluentFin.Contracts.Services;
using FluentFin.Contracts.ViewModels;
using FluentFin.Core.Contracts.Services;
using FluentFin.Core.Playback;
using FluentFin.Core.Services;
using FluentFin.Core.ViewModels;
using FluentFin.Core.WebSockets;
using FluentFin.Core.WebSockets.Messages;
using FluentFin.Helpers;
using FluentFin.Services;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using AppSettings = FluentFin.Core.Settings.ISettings;
using WpfKey = System.Windows.Input.Key;

namespace FluentFin.ViewModels;

public partial class VideoPlayerViewModel(
	IJellyfinClient jellyfinClient,
	TrickplayViewModel trickplayViewModel,
	ILogger<VideoPlayerViewModel> logger,
	IObservable<IInboundSocketMessage> webSocketMessages,
	INavigationService navigationService,
	AppSettings settings,
	ITaskBarProgress taskBarProgress,
	IPlaybackService playbackService,
	IVideoPlaybackController videoPlaybackController) : ObservableObject, INavigationAware
{
	private readonly KeyboardListener _keyboardListener = new();
	private readonly List<Guid> _keySubscriptions = [];
	private CompositeDisposable _disposables = [];
	private PlayQueueUpdate? _playQueueUpdate;
	private SyncPlaySendCommand? _previousCommand;
	private CancellationTokenSource? _playbackSelectionCts;
	private bool _updatingPlaylistFromQueue;

	public TrickplayViewModel TrickplayViewModel { get; } = trickplayViewModel;
	public IJellyfinClient JellyfinClient { get; } = jellyfinClient;

	[ObservableProperty]
	public partial List<MediaSegmentDto> Segments { get; set; } = [];

	[ObservableProperty]
	public partial bool IsSkipButtonVisible { get; set; }

	[ObservableProperty]
	public partial PlaylistViewModel Playlist { get; set; } = new();

	[ObservableProperty]
	public partial MediaPlayerType MediaPlayerType { get; set; }

	[ObservableProperty]
	public partial PlaybackState PlaybackState { get; set; } = PlaybackState.Stopped;

	[ObservableProperty]
	public partial TimeSpan Position { get; set; }

	[ObservableProperty]
	public partial TimeSpan Duration { get; set; }

	[ObservableProperty]
	public partial double Volume { get; set; } = 1;

	[ObservableProperty]
	public partial bool IsMuted { get; set; }

	[ObservableProperty]
	public partial string SubtitleText { get; set; } = "";

	[ObservableProperty]
	public partial int? SelectedAudioTrackIndex { get; set; }

	[ObservableProperty]
	public partial int? SelectedSubtitleTrackIndex { get; set; }

	public ObservableCollection<AudioTrack> AudioTracks { get; } = [];
	public ObservableCollection<SubtitleTrack> SubtitleTracks { get; } = [];

	public Action? ToggleFullScreen { get; set; }

	public Task OnNavigatedFrom()
	{
		_disposables.Dispose();
		_disposables = [];
		UnsubscribeKeyboard();
		Playlist.PropertyChanged -= OnPlaylistPropertyChanged;
		playbackService.PlaybackChanged -= PlaybackService_PlaybackChanged;
		playbackService.QueueChanged -= PlaybackService_QueueChanged;
		playbackService.MediaLoaded -= PlaybackService_MediaLoaded;
		playbackService.PlaybackFailed -= PlaybackService_PlaybackFailed;
		videoPlaybackController.TracksChanged -= VideoPlaybackController_TracksChanged;
		videoPlaybackController.SubtitleTextChanged -= VideoPlaybackController_SubtitleTextChanged;

		NativeMethods.AllowSleep();
		taskBarProgress.Clear();
		_playbackSelectionCts?.Cancel();
		_playbackSelectionCts?.Dispose();
		_playbackSelectionCts = null;
		return playbackService.StopAsync();
	}

	public async Task OnNavigatedTo(object parameter)
	{
		MediaPlayerType = settings.MediaPlayer;
		if (parameter is BaseItemDto { Id: { } id })
		{
			await Initialize(id);
		}
		else if (parameter is PlayQueueUpdate pqu)
		{
			_playQueueUpdate = pqu;
			InitializeForSyncPlay(pqu);
		}

		if (Playlist.Items.Count == 0)
		{
			return;
		}

		playbackService.PlaybackChanged += PlaybackService_PlaybackChanged;
		playbackService.QueueChanged += PlaybackService_QueueChanged;
		playbackService.MediaLoaded += PlaybackService_MediaLoaded;
		playbackService.PlaybackFailed += PlaybackService_PlaybackFailed;
		videoPlaybackController.TracksChanged += VideoPlaybackController_TracksChanged;
		videoPlaybackController.SubtitleTextChanged += VideoPlaybackController_SubtitleTextChanged;
		SubscribeWebsocketMessage();
		SubscribeKeyboard();
		NativeMethods.PreventSleep();

		Playlist.PropertyChanged += OnPlaylistPropertyChanged;
		await TrickplayViewModel.Initialize();

		if (_playQueueUpdate is null)
		{
			Playlist.AutoSelect();
		}
		else
		{
			Playlist.SelectedItem = Playlist.Items.FirstOrDefault();
		}

		RefreshPlaybackState();
	}

	private async Task Initialize(Guid? id)
	{
		if (!id.HasValue)
		{
			return;
		}

		var dto = await JellyfinClient.GetItem(id.Value);
		if (dto is null)
		{
			return;
		}

		Playlist = dto.Type switch
		{
			BaseItemDto_Type.Movie => PlaylistViewModel.FromMovie(dto),
			BaseItemDto_Type.Episode => await PlaylistViewModel.FromEpisode(JellyfinClient, dto),
			BaseItemDto_Type.Series => await PlaylistViewModel.FromSeries(JellyfinClient, dto),
			BaseItemDto_Type.Season => await PlaylistViewModel.FromSeason(JellyfinClient, dto),
			_ => new PlaylistViewModel()
		};
	}

	private void InitializeForSyncPlay(PlayQueueUpdate pqu)
	{
		if (pqu.PlayingItemIndex < 0)
		{
			return;
		}

		Playlist = PlaylistViewModel.FromSyncPlay(pqu.Playlist);
	}

	private async void OnPlaylistPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (_updatingPlaylistFromQueue || e.PropertyName != nameof(Playlist.SelectedItem))
		{
			return;
		}

		if (Playlist.SelectedItem is not { Dto.Id: not null } selectedItem)
		{
			return;
		}

		await PlaySelectedItemAsync(selectedItem);
	}

	private async Task PlaySelectedItemAsync(PlaylistItem selectedItem)
	{
		_playbackSelectionCts?.Cancel();
		_playbackSelectionCts?.Dispose();
		_playbackSelectionCts = new CancellationTokenSource();
		var cancellationToken = _playbackSelectionCts.Token;

		var full = await JellyfinClient.GetItem(selectedItem.Dto.Id ?? Guid.Empty);
		if (full is null)
		{
			return;
		}

		try
		{
			await LoadMediaSegments(full);
			TrickplayViewModel.SetItem(full);
			await playbackService.PlayAsync(CreatePlaybackRequest(selectedItem, full), cancellationToken);
		}
		catch (OperationCanceledException)
		{
			logger.LogDebug("Video playback selection was canceled. ItemId={ItemId}", selectedItem.Dto.Id);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Unable to start video playback through PlaybackService. ItemId={ItemId}", selectedItem.Dto.Id);
		}
	}

	private PlaybackRequest CreatePlaybackRequest(PlaylistItem selectedItem, BaseItemDto full)
	{
		var startIndex = Math.Max(0, Playlist.Items.IndexOf(selectedItem));
		var queue = Playlist.Items
			.Select((playlistItem, index) => PlaybackItem.FromDto(index == startIndex ? full : playlistItem.Dto, PlaybackKind.Video))
			.ToList();

		if (queue.Count == 0)
		{
			queue.Add(PlaybackItem.FromDto(full, PlaybackKind.Video));
			startIndex = 0;
		}

		return new PlaybackRequest
		{
			Kind = PlaybackKind.Video,
			StartItem = queue[startIndex],
			QueueItems = queue,
			StartIndex = startIndex,
			StartPosition = full.UserData?.PlaybackPositionTicks is { } ticks && ticks > 0 ? TimeSpan.FromTicks(ticks) : null
		};
	}

	private async Task LoadMediaSegments(BaseItemDto dto)
	{
		var segments = await JellyfinClient.GetMediaSegments(dto, [MediaSegmentType.Intro, MediaSegmentType.Outro]);
		Segments = segments?.Items ?? [];
	}

	[RelayCommand]
	private async Task Skip()
	{
		var currentTime = playbackService.Position.Ticks;
		var segment = Segments.FirstOrDefault(x => currentTime > x.StartTicks && currentTime < x.EndTicks);
		if (segment is not { EndTicks: not null })
		{
			return;
		}

		var endPosition = TimeSpan.FromTicks(segment.EndTicks.Value);
		if (_playQueueUpdate is not null)
		{
			await JellyfinClient.SignalSeekForSyncPlay(endPosition);
		}

		await playbackService.SeekAsync(endPosition);
	}

	[RelayCommand]
	private async Task TogglePlayPause()
	{
		if (playbackService.State is PlaybackState.Playing)
		{
			await JellyfinClient.SignalPauseForSyncPlay();
			await playbackService.PauseAsync();
		}
		else
		{
			await JellyfinClient.SignalUnpauseForSyncPlay();
			await playbackService.ResumeAsync();
		}
	}

	[RelayCommand]
	private async Task Seek(TimeSpan position)
	{
		if (_playQueueUpdate is not null)
		{
			await JellyfinClient.SignalSeekForSyncPlay(position);
		}

		await playbackService.SeekAsync(position);
	}

	[RelayCommand]
	private Task SkipBackward() => Seek(playbackService.Position - TimeSpan.FromSeconds(10));

	[RelayCommand]
	private Task SkipForward() => Seek(playbackService.Position + TimeSpan.FromSeconds(30));

	[RelayCommand]
	private Task SkipNext() => playbackService.SkipNextAsync();

	[RelayCommand]
	private Task SkipPrevious() => playbackService.SkipPreviousAsync();

	[RelayCommand]
	private Task SetVolume(double volume) => playbackService.SetVolumeAsync(volume);

	[RelayCommand]
	private Task ToggleMute() => playbackService.SetMutedAsync(!playbackService.IsMuted);

	[RelayCommand]
	private Task Stop()
	{
		return playbackService.StopAsync();
	}

	[RelayCommand]
	private void ToggleFullscreen()
	{
		ToggleFullScreen?.Invoke();
	}

	[RelayCommand]
	private Task SelectAudioTrack(int index) => videoPlaybackController.SelectAudioTrackAsync(index);

	[RelayCommand]
	private Task SelectSubtitleTrack(int index) => videoPlaybackController.SelectSubtitleTrackAsync(index);

	[RelayCommand]
	private Task DisableSubtitles() => videoPlaybackController.DisableSubtitlesAsync();

	private void PlaybackService_PlaybackChanged(object? sender, EventArgs e)
	{
		App.MainWindow.DispatcherQueue.TryEnqueue(RefreshPlaybackState);
	}

	private void PlaybackService_QueueChanged(object? sender, PlaybackQueueChangedEventArgs e)
	{
		App.MainWindow.DispatcherQueue.TryEnqueue(() =>
		{
			_updatingPlaylistFromQueue = true;
			try
			{
				var item = playbackService.CurrentItem;
				Playlist.SelectedItem = item is null ? null : Playlist.Items.FirstOrDefault(x => x.Dto.Id == item.JellyfinId);
			}
			finally
			{
				_updatingPlaylistFromQueue = false;
			}
		});
	}

	private async void PlaybackService_MediaLoaded(object? sender, EventArgs e)
	{
		if (_playQueueUpdate is null)
		{
			return;
		}

		await JellyfinClient.SignalReadyForSyncPlay(new ReadyRequestDto
		{
			When = TimeProvider.System.GetUtcNow(),
			IsPlaying = playbackService.State is PlaybackState.Playing,
			PlaylistItemId = _playQueueUpdate.Playlist[_playQueueUpdate.PlayingItemIndex].PlaylistItemId,
			PositionTicks = playbackService.Position.Ticks
		});
	}

	private async void PlaybackService_PlaybackFailed(object? sender, PlaybackErrorEventArgs e)
	{
		logger.LogError(e.Exception, "An error occurred while playing media. Message={Message}", e.Message);
		await JellyfinClient.Stop();
	}

	private void VideoPlaybackController_TracksChanged(object? sender, EventArgs e)
	{
		App.MainWindow.DispatcherQueue.TryEnqueue(RefreshTracks);
	}

	private void VideoPlaybackController_SubtitleTextChanged(object? sender, string e)
	{
		App.MainWindow.DispatcherQueue.TryEnqueue(() => SubtitleText = e);
	}

	private void RefreshPlaybackState()
	{
		PlaybackState = playbackService.State;
		Position = playbackService.Position;
		Duration = playbackService.Duration;
		Volume = playbackService.Volume;
		IsMuted = playbackService.IsMuted;
		IsSkipButtonVisible = PlaybackState is PlaybackState.Playing &&
			Segments.Any(segment => Position.Ticks > segment.StartTicks && Position.Ticks < segment.EndTicks);

		if (Duration > TimeSpan.Zero)
		{
			taskBarProgress.SetProgressPercent((int)Math.Clamp((Position.TotalSeconds / Duration.TotalSeconds) * 100, 0, 100));
		}
		else
		{
			taskBarProgress.Clear();
		}

		RefreshTracks();
	}

	private void RefreshTracks()
	{
		AudioTracks.Clear();
		foreach (var track in videoPlaybackController.AudioTracks)
		{
			AudioTracks.Add(track);
		}

		SubtitleTracks.Clear();
		foreach (var track in videoPlaybackController.SubtitleTracks)
		{
			SubtitleTracks.Add(track);
		}

		SelectedAudioTrackIndex = videoPlaybackController.AudioTrackIndex;
		SelectedSubtitleTrackIndex = videoPlaybackController.SubtitleTrackIndex;
		SubtitleText = videoPlaybackController.SubtitleText;
	}

	private void SubscribeWebsocketMessage()
	{
		webSocketMessages
			.ObserveOn(RxApp.MainThreadScheduler)
			.Subscribe(async message =>
			{
				switch (message)
				{
					case PlayStateMessage { Data.Command: PlaystateCommand.PlayPause }:
						await TogglePlayPause();
						break;
					case PlayStateMessage { Data.Command: PlaystateCommand.Stop }:
						await playbackService.StopAsync();
						navigationService.NavigateTo<HomeViewModel>(new());
						break;
					case SyncPlayCommandMessage { Data: not null } syncPlay:
						if (syncPlay.Data.When == _previousCommand?.When)
						{
							return;
						}

						switch (syncPlay.Data.Command)
						{
							case SendCommandType.Pause:
							case SendCommandType.Seek:
								await SchedulePause(syncPlay.Data);
								break;
							case SendCommandType.Unpause:
								await SchedulePlay(syncPlay.Data);
								break;
							case SendCommandType.Stop:
								await playbackService.StopAsync();
								break;
						}

						_previousCommand = syncPlay.Data;
						break;
				}
			})
			.DisposeWith(_disposables);
	}

	private async Task SchedulePause(SyncPlaySendCommand command)
	{
		var currentTime = await JellyfinClient.SyncTime();
		var commandTime = command.When;
		await playbackService.SeekAsync(new TimeSpan(command.PositionTicks));
		if (commandTime > currentTime)
		{
			await Task.Delay(commandTime - currentTime);
		}

		await playbackService.PauseAsync();
	}

	private async Task SchedulePlay(SyncPlaySendCommand command)
	{
		var currentTime = await JellyfinClient.SyncTime();
		var commandTime = command.When;
		var position = commandTime > currentTime
			? new TimeSpan(command.PositionTicks)
			: new TimeSpan(command.PositionTicks + (currentTime - commandTime).Ticks);

		await playbackService.SeekAsync(position);
		if (commandTime > currentTime)
		{
			await Task.Delay(commandTime - currentTime);
		}

		await playbackService.ResumeAsync();
	}

	private void SubscribeKeyboard()
	{
		UnsubscribeKeyboard();
		_keySubscriptions.Add(_keyboardListener.Subscribe(WpfKey.Space, async () =>
		{
			if (NativeMethods.IsAppForeground())
			{
				await TogglePlayPause();
			}
		}).Id);
		_keySubscriptions.Add(_keyboardListener.Subscribe(WpfKey.Left, async () =>
		{
			if (NativeMethods.IsAppForeground())
			{
				await Seek(playbackService.Position - TimeSpan.FromSeconds(5));
			}
		}).Id);
		_keySubscriptions.Add(_keyboardListener.Subscribe(WpfKey.Right, async () =>
		{
			if (NativeMethods.IsAppForeground())
			{
				await Seek(playbackService.Position + TimeSpan.FromSeconds(5));
			}
		}).Id);
		_keySubscriptions.Add(_keyboardListener.Subscribe(WpfKey.S, async () =>
		{
			if (NativeMethods.IsAppForeground())
			{
				await Skip();
			}
		}).Id);
		_keySubscriptions.Add(_keyboardListener.Subscribe(WpfKey.F, () =>
		{
			if (NativeMethods.IsAppForeground())
			{
				ToggleFullScreen?.Invoke();
			}
		}).Id);
		_keySubscriptions.Add(_keyboardListener.Subscribe(WpfKey.Up, async () =>
		{
			if (NativeMethods.IsAppForeground())
			{
				await playbackService.SetVolumeAsync(playbackService.Volume + 0.01);
			}
		}).Id);
		_keySubscriptions.Add(_keyboardListener.Subscribe(WpfKey.Down, async () =>
		{
			if (NativeMethods.IsAppForeground())
			{
				await playbackService.SetVolumeAsync(playbackService.Volume - 0.01);
			}
		}).Id);
	}

	private void UnsubscribeKeyboard()
	{
		if (_keySubscriptions.Count == 0)
		{
			return;
		}

		_keyboardListener.Unsubscribe(_keySubscriptions);
		_keySubscriptions.Clear();
	}
}
