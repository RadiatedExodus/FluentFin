using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentFin.Core.Contracts.Services;
using FluentFin.Core.Playback;
using FluentFin.Core.Services;
using FluentFin.Core.ViewModels;
using FluentFin.Playback.Presentation;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace FluentFin.ViewModels;

public partial class MusicPlaybackViewModel : ObservableObject
{
	private static readonly TimeSpan PreviousTrackRestartThreshold = TimeSpan.FromSeconds(3);
	private readonly IPlaybackService _playbackService;
	private readonly IMusicPlaybackController _musicPlaybackController;
	private readonly IPlaybackPresentationManager _presentationManager;
	private readonly IJellyfinClient _jellyfinClient;
	private readonly INavigationServiceCore _navigationService;
	private readonly ILogger<MusicPlaybackViewModel> _logger;
	private readonly DispatcherQueueTimer _positionTimer;
	private bool _isUpdatingVolume;

	public MusicPlaybackViewModel(
		IPlaybackService playbackService,
		IMusicPlaybackController musicPlaybackController,
		IPlaybackPresentationManager presentationManager,
		IJellyfinClient jellyfinClient,
		INavigationServiceCore navigationService,
		ILogger<MusicPlaybackViewModel> logger)
	{
		_playbackService = playbackService;
		_musicPlaybackController = musicPlaybackController;
		_presentationManager = presentationManager;
		_jellyfinClient = jellyfinClient;
		_navigationService = navigationService;
		_logger = logger;

		_playbackService.PlaybackChanged += (_, _) => EnqueuePlaybackRefresh();
		_playbackService.QueueChanged += (_, _) => EnqueueRefresh();
		_playbackService.VolumeChanged += (_, _) => EnqueuePlaybackRefresh();
		_musicPlaybackController.MusicOptionsChanged += (_, _) => EnqueuePlaybackRefresh();

		var dispatcherQueue = DispatcherQueue.GetForCurrentThread() ?? App.MainWindow.DispatcherQueue;
		_positionTimer = dispatcherQueue.CreateTimer();
		_positionTimer.Interval = TimeSpan.FromMilliseconds(500);
		_positionTimer.Tick += (_, _) =>
		{
			if (IsPlaying)
			{
				RefreshPlaybackState();
			}
		};
		_positionTimer.Start();

		RefreshAll();
	}

	public ObservableCollection<MusicQueueItemViewModel> QueueItems { get; } = [];
	public ObservableCollection<MusicArtistLinkViewModel> ArtistLinks { get; } = [];

	[ObservableProperty]
	public partial PlaybackItem? CurrentItem { get; set; }

	[ObservableProperty]
	public partial PlaybackState State { get; set; } = PlaybackState.Stopped;

	[ObservableProperty]
	public partial TimeSpan Position { get; set; }

	[ObservableProperty]
	public partial TimeSpan Duration { get; set; }

	[ObservableProperty]
	public partial double PositionSeconds { get; set; }

	[ObservableProperty]
	public partial double DurationSeconds { get; set; }

	[ObservableProperty]
	public partial int CurrentQueueIndex { get; set; } = -1;

	[ObservableProperty]
	public partial int VolumePercent { get; set; } = 100;

	public double VolumePercentValue
	{
		get => VolumePercent;
		set => VolumePercent = (int)Math.Round(Math.Clamp(value, 0, 100));
	}

	public bool IsMusicActive => _playbackService.CurrentKind is PlaybackKind.Music && CurrentItem is not null && State is not PlaybackState.Stopped and not PlaybackState.Ended;
	public bool IsPlaying => State is PlaybackState.Playing;
	public bool IsLoading => State is PlaybackState.Opening;
	public bool CanSeek => DurationSeconds > 0;
	public bool ShuffleEnabled => _musicPlaybackController.ShuffleEnabled;
	public PlaybackRepeatMode RepeatMode => _musicPlaybackController.RepeatMode;
	public string TrackTitle => CurrentItem?.Title ?? "";
	public string AlbumTitle => CurrentItem?.Metadata is MusicPlaybackMetadata music ? music.Album ?? "" : "";
	public string ArtistText => CurrentItem?.Metadata is MusicPlaybackMetadata music
		? string.Join(", ", music.Artists.Where(x => !string.IsNullOrWhiteSpace(x)))
		: "";
	public bool HasArtistLinks => ArtistLinks.Count > 0;
	public bool HasNoArtistLinks => !HasArtistLinks;
	public string PositionText => FluentFin.Converters.Converters.TimeSpanToString(Position);
	public string DurationText => Duration > TimeSpan.Zero ? FluentFin.Converters.Converters.TimeSpanToString(Duration) : "--:--";
	public string PlayPauseGlyph => IsPlaying ? "\uE769" : "\uF5B0";
	public Visibility PlayPauseIconVisibility => IsLoading ? Visibility.Collapsed : Visibility.Visible;
	public Visibility LoadingVisibility => IsLoading ? Visibility.Visible : Visibility.Collapsed;
	public string ShuffleGlyph => ShuffleEnabled ? "\uE8B1" : "\uE8B1";
	public string RepeatGlyph => RepeatMode switch
	{
		PlaybackRepeatMode.One => "\uE8ED",
		PlaybackRepeatMode.All => "\uE8EE",
		_ => "\uE8EE"
	};
	public string RepeatToolTip => RepeatMode switch
	{
		PlaybackRepeatMode.One => "Repeat one",
		PlaybackRepeatMode.All => "Repeat queue",
		_ => "Repeat off"
	};
	public ImageSource? Artwork => CurrentItem is null
		? null
		: FluentFin.Converters.BaseItemDtoConverters.GetImage(CurrentItem.Item, ImageType.Primary, 600);

	partial void OnVolumePercentChanged(int value)
	{
		if (_isUpdatingVolume)
		{
			return;
		}

		_ = SetVolumeFromPercent(Math.Clamp(value, 0, 100));
		OnPropertyChanged(nameof(VolumePercentValue));
	}

	[RelayCommand]
	private async Task TogglePlayPause()
	{
		if (!IsMusicActive)
		{
			return;
		}

		if (IsPlaying)
		{
			await _playbackService.PauseAsync();
		}
		else
		{
			await _playbackService.ResumeAsync();
		}
	}

	[RelayCommand]
	private Task Previous()
	{
		if (Position > PreviousTrackRestartThreshold)
		{
			return _playbackService.SeekAsync(TimeSpan.Zero);
		}

		return _musicPlaybackController.PreviousAsync();
	}

	[RelayCommand]
	private Task Next() => _musicPlaybackController.NextAsync();

	[RelayCommand]
	private Task Stop() => _playbackService.StopAsync();

	[RelayCommand]
	private Task ToggleShuffle() => _musicPlaybackController.SetShuffleAsync(!ShuffleEnabled);

	[RelayCommand]
	private Task CycleRepeatMode()
	{
		var nextMode = RepeatMode switch
		{
			PlaybackRepeatMode.Off => PlaybackRepeatMode.All,
			PlaybackRepeatMode.All => PlaybackRepeatMode.One,
			_ => PlaybackRepeatMode.Off
		};

		return _musicPlaybackController.SetRepeatModeAsync(nextMode);
	}

	[RelayCommand]
	private void OpenExpanded() => _presentationManager.ShowMusicExpanded();

	[RelayCommand]
	private void OpenQueue()
	{
		if (_presentationManager.MusicMode is MusicPresentationMode.Queue)
		{
			_presentationManager.ShowMusicCompact();
			return;
		}

		_presentationManager.ShowMusicQueue();
	}

	[RelayCommand]
	private void ClosePresentation() => _presentationManager.ShowMusicCompact();

	[RelayCommand]
	private void OpenAlbum()
	{
		if (CurrentItem?.Item is not { } item)
		{
			return;
		}

		if (item.Type is BaseItemDto_Type.MusicAlbum)
		{
			_navigationService.NavigateTo<MusicAlbumViewModel>(item);
			return;
		}

		if (TryGetAlbumId(item) is { } albumId)
		{
			_navigationService.NavigateTo<MusicAlbumViewModel>(albumId);
		}
	}

	public async Task OpenArtist(string name, Guid? artistId)
	{
		if (artistId is null)
		{
			var artist = await _jellyfinClient.FindMusicArtistByName(name);
			artistId = artist?.Id;
		}

		if (artistId is null)
		{
			_logger.LogWarning("Music playback artist navigation skipped because artist id could not be resolved. ItemId={ItemId}, ArtistName={ArtistName}",
				CurrentItem?.JellyfinId,
				name);
			return;
		}

		_logger.LogInformation("Music playback artist navigation requested. ItemId={ItemId}, ArtistId={ArtistId}, ArtistName={ArtistName}",
			CurrentItem?.JellyfinId,
			artistId,
			name);
		if (_presentationManager.MusicMode is MusicPresentationMode.Expanded or MusicPresentationMode.Queue)
		{
			_presentationManager.ShowMusicCompact();
		}

		_navigationService.NavigateTo<MusicArtistViewModel>(artistId);
	}

	[RelayCommand]
	private Task JumpToQueueItem(MusicQueueItemViewModel item) => _musicPlaybackController.JumpToQueueItemAsync(item.Index);

	[RelayCommand]
	private Task RemoveQueueItem(MusicQueueItemViewModel item) => _musicPlaybackController.RemoveQueueItemAsync(item.Index);

	public Task SeekToSeconds(double seconds) => _playbackService.SeekAsync(TimeSpan.FromSeconds(Math.Clamp(seconds, 0, DurationSeconds)));

	public Task SetVolumeFromPercent(int percent) => _playbackService.SetVolumeAsync(Math.Clamp(percent, 0, 100) / 100d);

	private void EnqueueRefresh()
	{
		var dispatcherQueue = DispatcherQueue.GetForCurrentThread() ?? App.MainWindow.DispatcherQueue;
		if (!dispatcherQueue.HasThreadAccess)
		{
			dispatcherQueue.TryEnqueue(RefreshAll);
			return;
		}

		RefreshAll();
	}

	private void EnqueuePlaybackRefresh()
	{
		var dispatcherQueue = DispatcherQueue.GetForCurrentThread() ?? App.MainWindow.DispatcherQueue;
		if (!dispatcherQueue.HasThreadAccess)
		{
			dispatcherQueue.TryEnqueue(RefreshPlaybackState);
			return;
		}

		RefreshPlaybackState();
	}

	private void RefreshAll()
	{
		RefreshPlaybackState();
		RefreshQueue();
	}

	private void RefreshPlaybackState()
	{
		CurrentItem = _playbackService.CurrentItem;
		State = _playbackService.State;
		Position = _playbackService.Position;
		Duration = _playbackService.Duration;
		PositionSeconds = Position.TotalSeconds;
		DurationSeconds = Math.Max(0, Duration.TotalSeconds);

		_isUpdatingVolume = true;
		VolumePercent = (int)Math.Round(Math.Clamp(_playbackService.Volume, 0d, 1d) * 100);
		_isUpdatingVolume = false;

		OnPropertyChanged(nameof(IsMusicActive));
		OnPropertyChanged(nameof(IsPlaying));
		OnPropertyChanged(nameof(IsLoading));
		OnPropertyChanged(nameof(CanSeek));
		OnPropertyChanged(nameof(ShuffleEnabled));
		OnPropertyChanged(nameof(RepeatMode));
		OnPropertyChanged(nameof(TrackTitle));
		OnPropertyChanged(nameof(AlbumTitle));
		OnPropertyChanged(nameof(ArtistText));
		RefreshArtistLinks();
		OnPropertyChanged(nameof(PositionText));
		OnPropertyChanged(nameof(DurationText));
		OnPropertyChanged(nameof(PlayPauseGlyph));
		OnPropertyChanged(nameof(PlayPauseIconVisibility));
		OnPropertyChanged(nameof(LoadingVisibility));
		OnPropertyChanged(nameof(RepeatGlyph));
		OnPropertyChanged(nameof(RepeatToolTip));
		OnPropertyChanged(nameof(Artwork));
		OnPropertyChanged(nameof(VolumePercentValue));
	}

	private void RefreshQueue()
	{
		CurrentQueueIndex = _playbackService.Queue.CurrentIndex;
		QueueItems.Clear();
		for (var i = 0; i < _playbackService.Queue.Items.Count; i++)
		{
			QueueItems.Add(new MusicQueueItemViewModel(_playbackService.Queue.Items[i], i, i == CurrentQueueIndex));
		}

		OnPropertyChanged(nameof(CurrentQueueIndex));
	}

	private void RefreshArtistLinks()
	{
		ArtistLinks.Clear();
		foreach (var artist in GetCurrentArtistLinks())
		{
			ArtistLinks.Add(new MusicArtistLinkViewModel(artist.Name, artist.Id, OpenArtist));
		}

		OnPropertyChanged(nameof(HasArtistLinks));
		OnPropertyChanged(nameof(HasNoArtistLinks));
	}

	private IEnumerable<(string Name, Guid? Id)> GetCurrentArtistLinks()
	{
		if (CurrentItem?.Item is not { } item)
		{
			return [];
		}

		var links = new List<(string Name, Guid? Id)>();
		AddArtistLinks(links, item.ArtistItems);
		if (CurrentItem.Metadata is MusicPlaybackMetadata metadata)
		{
			AddArtistLinks(links, metadata.ArtistItems);
		}

		if (links.Count == 0)
		{
			AddArtistLinks(links, item.AlbumArtists);
		}

		if (links.Count == 0)
		{
			AddArtistNames(links, item.Artists);
			if (CurrentItem.Metadata is MusicPlaybackMetadata music)
			{
				AddArtistNames(links, music.Artists);
			}
		}

		return links
			.Where(x => !string.IsNullOrWhiteSpace(x.Name))
			.GroupBy(x => x.Id?.ToString() ?? x.Name, StringComparer.OrdinalIgnoreCase)
			.Select(x => x.First());
	}

	private static void AddArtistLinks(List<(string Name, Guid? Id)> links, IReadOnlyList<NameGuidPair>? artists)
	{
		if (artists is null)
		{
			return;
		}

		foreach (var artist in artists)
		{
			if (!string.IsNullOrWhiteSpace(artist.Name))
			{
				links.Add((artist.Name, artist.Id));
			}
		}
	}

	private static void AddArtistNames(List<(string Name, Guid? Id)> links, IReadOnlyList<string>? artists)
	{
		if (artists is null)
		{
			return;
		}

		foreach (var artist in artists)
		{
			if (!string.IsNullOrWhiteSpace(artist))
			{
				links.Add((artist, null));
			}
		}
	}

	private Guid? TryGetAlbumId(BaseItemDto item)
	{
		try
		{
			return item.GetType().GetProperty("AlbumId")?.GetValue(item) is Guid albumId ? albumId : null;
		}
		catch (Exception ex)
		{
			_logger.LogDebug(ex, "Unable to read album id from playback item. ItemId={ItemId}", item.Id);
			return null;
		}
	}
}
