using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentFin.Core.Playback;
using FluentFin.Core.Playback.Lyrics;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace FluentFin.ViewModels;

public partial class MusicLyricsViewModel : ObservableObject
{
	private readonly IPlaybackService _playbackService;
	private readonly ILyricsService _lyricsService;
	private readonly ILogger<MusicLyricsViewModel> _logger;
	private readonly DispatcherQueue _dispatcherQueue;
	private readonly DispatcherQueueTimer _positionTimer;
	private CancellationTokenSource? _loadCts;
	private Guid? _loadedItemId;

	public MusicLyricsViewModel(
		IPlaybackService playbackService,
		ILyricsService lyricsService,
		ILogger<MusicLyricsViewModel> logger)
	{
		_playbackService = playbackService;
		_lyricsService = lyricsService;
		_logger = logger;

		_playbackService.PlaybackChanged += (_, _) => EnqueuePlaybackRefresh();
		_playbackService.QueueChanged += (_, _) => EnqueuePlaybackRefresh();

		_dispatcherQueue = DispatcherQueue.GetForCurrentThread() ?? App.MainWindow.DispatcherQueue;
		_positionTimer = _dispatcherQueue.CreateTimer();
		_positionTimer.Interval = TimeSpan.FromMilliseconds(350);
		_positionTimer.Tick += (_, _) => RefreshActiveLine();
		_positionTimer.Start();

		RefreshForCurrentItem();
	}

	public ObservableCollection<LyricsLineViewModel> Lines { get; } = [];

	[ObservableProperty]
	public partial PlaybackItem? CurrentItem { get; set; }

	[ObservableProperty]
	public partial string TrackTitle { get; set; } = "";

	[ObservableProperty]
	public partial string ArtistText { get; set; } = "";

	[ObservableProperty]
	public partial string SourceText { get; set; } = "";

	[ObservableProperty]
	public partial bool IsLoading { get; set; }

	[ObservableProperty]
	public partial bool HasLyrics { get; set; }

	[ObservableProperty]
	public partial bool IsSynced { get; set; }

	[ObservableProperty]
	public partial int ActiveLineIndex { get; set; } = -1;

	public Visibility LoadingVisibility => IsLoading ? Visibility.Visible : Visibility.Collapsed;
	public Visibility EmptyVisibility => !IsLoading && !HasLyrics ? Visibility.Visible : Visibility.Collapsed;
	public Visibility LyricsVisibility => !IsLoading && HasLyrics ? Visibility.Visible : Visibility.Collapsed;
	public string SyncText => IsSynced ? "Synced lyrics" : "Lyrics";

	partial void OnIsLoadingChanged(bool value)
	{
		OnPropertyChanged(nameof(LoadingVisibility));
		OnPropertyChanged(nameof(EmptyVisibility));
		OnPropertyChanged(nameof(LyricsVisibility));
	}

	partial void OnHasLyricsChanged(bool value)
	{
		OnPropertyChanged(nameof(EmptyVisibility));
		OnPropertyChanged(nameof(LyricsVisibility));
	}

	partial void OnIsSyncedChanged(bool value) => OnPropertyChanged(nameof(SyncText));

	private void EnqueuePlaybackRefresh()
	{
		if (_dispatcherQueue.HasThreadAccess)
		{
			RefreshForCurrentItem();
			return;
		}

		_dispatcherQueue.TryEnqueue(RefreshForCurrentItem);
	}

	private void RefreshForCurrentItem()
	{
		var item = _playbackService.CurrentKind is PlaybackKind.Music ? _playbackService.CurrentItem : null;
		CurrentItem = item;
		TrackTitle = item?.Title ?? "";
		ArtistText = item?.Metadata is MusicPlaybackMetadata music ? string.Join(", ", music.Artists.Where(x => !string.IsNullOrWhiteSpace(x))) : "";

		if (item?.JellyfinId == _loadedItemId)
		{
			RefreshActiveLine();
			return;
		}

		_loadedItemId = item?.JellyfinId;
		_loadCts?.Cancel();
		_loadCts?.Dispose();
		_loadCts = null;
		Lines.Clear();
		HasLyrics = false;
		IsSynced = false;
		SourceText = "";
		SetActiveLine(-1);

		if (item is null)
		{
			IsLoading = false;
			return;
		}

		_loadCts = new CancellationTokenSource();
		_ = LoadLyricsAsync(item, _loadCts.Token);
	}

	private async Task LoadLyricsAsync(PlaybackItem item, CancellationToken cancellationToken)
	{
		try
		{
			IsLoading = true;
			var document = await _lyricsService.GetLyricsAsync(item, cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();

			Lines.Clear();
			if (document is null || document.Lines.Count == 0)
			{
				HasLyrics = false;
				IsSynced = false;
				SourceText = "";
				return;
			}

			IsSynced = document.SyncKind is LyricsSyncKind.Synced;
			SourceText = document.Metadata?.Provider ?? "";
			foreach (var line in document.Lines)
			{
				Lines.Add(new LyricsLineViewModel(line.Text, IsSynced ? line.Start : null, IsSynced));
			}

			HasLyrics = Lines.Count > 0;
			RefreshActiveLine();
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Lyrics load failed. ItemId={ItemId}", item.JellyfinId);
			Lines.Clear();
			HasLyrics = false;
			IsSynced = false;
			SourceText = "";
		}
		finally
		{
			if (!cancellationToken.IsCancellationRequested)
			{
				IsLoading = false;
			}
		}
	}

	private void RefreshActiveLine()
	{
		if (!IsSynced || Lines.Count == 0)
		{
			SetActiveLine(-1);
			return;
		}

		var position = _playbackService.Position;
		var index = -1;
		for (var i = 0; i < Lines.Count; i++)
		{
			if (Lines[i].Start is { } start && start <= position)
			{
				index = i;
				continue;
			}

			break;
		}

		SetActiveLine(index);
	}

	private void SetActiveLine(int index)
	{
		if (ActiveLineIndex == index)
		{
			return;
		}

		if (ActiveLineIndex >= 0 && ActiveLineIndex < Lines.Count)
		{
			Lines[ActiveLineIndex].IsActive = false;
		}

		ActiveLineIndex = index;

		if (ActiveLineIndex >= 0 && ActiveLineIndex < Lines.Count)
		{
			Lines[ActiveLineIndex].IsActive = true;
		}
	}
}
