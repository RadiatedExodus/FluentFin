using FluentFin.Core.Contracts.Services;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Extensions.Logging;

namespace FluentFin.Core.Playback;

public sealed class MusicPlaybackController(
	IJellyfinClient jellyfinClient,
	IServiceProvider serviceProvider,
	ILogger<MusicPlaybackController> logger) : IPlaybackController, IMusicPlaybackController
{
	private static readonly TimeSpan ProgressReportInterval = TimeSpan.FromSeconds(10);
	private readonly SemaphoreSlim _transitionLock = new(1, 1);
	private readonly Dictionary<Guid, MediaResponse> _resolvedMedia = [];
	private IMediaPlaybackEngine? _engine;
	private IQueuedPlaybackEngine? _queuedEngine;
	private MusicQueuePolicy? _queuePolicy;
	private PlaybackRequest? _request;
	private PlaybackItem? _currentItem;
	private MediaResponse? _currentMediaResponse;
	private MediaSource? _currentSource;
	private CancellationTokenSource? _progressCancellation;
	private long _playbackStartTimeTicks;

	private IPlaybackService PlaybackService => (IPlaybackService)(serviceProvider.GetService(typeof(IPlaybackService))
		?? throw new InvalidOperationException("IPlaybackService is not registered."));

	public event EventHandler? MusicOptionsChanged;

	public PlaybackKind Kind => PlaybackKind.Music;
	public PlaybackState State => _engine?.State ?? PlaybackState.Stopped;
	public TimeSpan Position => _engine?.Position ?? TimeSpan.Zero;
	public TimeSpan Duration => _engine?.Duration ?? TimeSpan.Zero;
	public MediaSource? CurrentSource => _currentSource;
	public bool ShuffleEnabled => _queuePolicy?.ShuffleEnabled ?? false;
	public PlaybackRepeatMode RepeatMode => _queuePolicy?.RepeatMode ?? PlaybackRepeatMode.Off;

	public async Task PlaySongAsync(BaseItemDto song, CancellationToken cancellationToken = default)
	{
		var items = await BuildMusicItems(song, cancellationToken);
		await PlayQueueAsync(items, items.FirstOrDefault(), cancellationToken);
	}

	public async Task PlayAlbumFromTrackAsync(BaseItemDto album, BaseItemDto selectedTrack, CancellationToken cancellationToken = default)
	{
		var items = await BuildMusicItems(album, cancellationToken);
		await PlayQueueAsync(items, FindSelectedItem(items, selectedTrack) ?? items.FirstOrDefault(), cancellationToken);
	}

	public async Task PlayPlaylistFromTrackAsync(BaseItemDto playlist, BaseItemDto selectedTrack, CancellationToken cancellationToken = default)
	{
		var items = await BuildMusicItems(playlist, cancellationToken);
		await PlayQueueAsync(items, FindSelectedItem(items, selectedTrack) ?? items.FirstOrDefault(), cancellationToken);
	}

	public async Task NextAsync(CancellationToken cancellationToken = default)
	{
		await _transitionLock.WaitAsync(cancellationToken);
		try
		{
			var next = _queuePolicy?.MoveNext(automatic: false);
			logger.LogInformation("Music next requested. NextItemId={NextItemId}", next?.JellyfinId);
			await MoveToCurrentQueueItem(next, restartEngine: true, cancellationToken);
		}
		finally
		{
			_transitionLock.Release();
		}
	}

	public async Task PreviousAsync(CancellationToken cancellationToken = default)
	{
		await _transitionLock.WaitAsync(cancellationToken);
		try
		{
			var previous = _queuePolicy?.MovePrevious();
			logger.LogInformation("Music previous requested. PreviousItemId={PreviousItemId}", previous?.JellyfinId);
			await MoveToCurrentQueueItem(previous, restartEngine: true, cancellationToken);
		}
		finally
		{
			_transitionLock.Release();
		}
	}

	public async Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default)
	{
		EnsureQueuePolicy().SetShuffle(enabled);
		logger.LogInformation("Music shuffle changed. Enabled={Enabled}, CurrentItemId={CurrentItemId}", enabled, _queuePolicy?.Current?.JellyfinId);
		MusicOptionsChanged?.Invoke(this, EventArgs.Empty);
		await RefreshNativeQueue(cancellationToken);
	}

	public async Task SetRepeatModeAsync(PlaybackRepeatMode repeatMode, CancellationToken cancellationToken = default)
	{
		EnsureQueuePolicy().SetRepeatMode(repeatMode);
		logger.LogInformation("Music repeat mode changed. RepeatMode={RepeatMode}, CurrentItemId={CurrentItemId}", repeatMode, _queuePolicy?.Current?.JellyfinId);
		MusicOptionsChanged?.Invoke(this, EventArgs.Empty);
		await RefreshNativeQueue(cancellationToken);
	}

	public async Task AddToQueueAsync(BaseItemDto item, CancellationToken cancellationToken = default)
	{
		var items = await BuildMusicItems(item, cancellationToken);
		EnsureQueuePolicy().AddToQueue(items);
		logger.LogInformation("Music items added to queue. AddedCount={AddedCount}, QueueCount={QueueCount}", items.Count, PlaybackService.Queue.Items.Count);
		await RefreshNativeQueue(cancellationToken);
	}

	public async Task PlayNextAsync(BaseItemDto item, CancellationToken cancellationToken = default)
	{
		var items = await BuildMusicItems(item, cancellationToken);
		EnsureQueuePolicy().PlayNext(items);
		logger.LogInformation("Music items inserted to play next. AddedCount={AddedCount}, QueueCount={QueueCount}", items.Count, PlaybackService.Queue.Items.Count);
		await RefreshNativeQueue(cancellationToken);
	}

	public async Task JumpToQueueItemAsync(int index, CancellationToken cancellationToken = default)
	{
		await _transitionLock.WaitAsync(cancellationToken);
		try
		{
			var item = EnsureQueuePolicy().JumpTo(index);
			logger.LogInformation("Music queue jump requested. Index={Index}, ItemId={ItemId}", index, item?.JellyfinId);
			await MoveToCurrentQueueItem(item, restartEngine: true, cancellationToken);
		}
		finally
		{
			_transitionLock.Release();
		}
	}

	public async Task RemoveQueueItemAsync(int index, CancellationToken cancellationToken = default)
	{
		await _transitionLock.WaitAsync(cancellationToken);
		try
		{
			var queue = PlaybackService.Queue;
			var removedCurrent = index == queue.CurrentIndex;
			var removed = EnsureQueuePolicy().RemoveAt(index);
			logger.LogInformation("Music queue remove requested. Index={Index}, RemovedItemId={RemovedItemId}, RemovedCurrent={RemovedCurrent}, QueueCount={QueueCount}",
				index,
				removed?.JellyfinId,
				removedCurrent,
				queue.Items.Count);

			if (removed is null)
			{
				return;
			}

			if (removedCurrent)
			{
				await ReportStopped(cancellationToken);
				if (queue.Current is null)
				{
					await PlaybackService.StopAsync(cancellationToken);
					return;
				}

				_currentItem = queue.Current;
				await OpenCurrentQueueWindow(cancellationToken);
				if (_engine is not null)
				{
					await _engine.PlayAsync(cancellationToken);
				}

				await ReportStarted(cancellationToken);
			}
			else
			{
				await RefreshNativeQueue(cancellationToken);
			}
		}
		finally
		{
			_transitionLock.Release();
		}
	}

	public async Task PrepareAsync(PlaybackRequest request, IMediaPlaybackEngine engine, CancellationToken cancellationToken = default)
	{
		if (request.Kind is not PlaybackKind.Music)
		{
			throw new ArgumentException("MusicPlaybackController can only prepare music requests.", nameof(request));
		}

		logger.LogInformation("MusicPlaybackController preparing. ItemId={ItemId}, Title={Title}, QueueCount={QueueCount}, StartIndex={StartIndex}",
			request.StartItem.JellyfinId,
			request.StartItem.Title,
			request.EffectiveQueue.Count,
			request.StartIndex);

		DetachEngineEvents();
		_request = request;
		_engine = engine;
		_queuedEngine = engine as IQueuedPlaybackEngine;
		AttachEngineEvents();
		EnsureQueuePolicy();

		_currentItem = PlaybackService.Queue.Current ?? request.StartItem;
		await OpenCurrentQueueWindow(cancellationToken);
	}

	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		if (_engine is null || _currentItem is null)
		{
			return;
		}

		logger.LogInformation("MusicPlaybackController starting. ItemId={ItemId}, Title={Title}", _currentItem.JellyfinId, _currentItem.Title);
		await _engine.PlayAsync(cancellationToken);

		if (_request?.StartPosition is { } startPosition && startPosition > TimeSpan.Zero)
		{
			await _engine.SeekAsync(startPosition, cancellationToken);
		}

		await ReportStarted(cancellationToken);
		StartProgressLoop();
	}

	public async Task PauseAsync(CancellationToken cancellationToken = default)
	{
		if (_engine is null)
		{
			return;
		}

		logger.LogInformation("Music pause requested. ItemId={ItemId}", _currentItem?.JellyfinId);
		await _engine.PauseAsync(cancellationToken);
		await ReportProgress(cancellationToken);
	}

	public async Task ResumeAsync(CancellationToken cancellationToken = default)
	{
		if (_engine is null)
		{
			return;
		}

		logger.LogInformation("Music resume requested. ItemId={ItemId}", _currentItem?.JellyfinId);
		await _engine.PlayAsync(cancellationToken);
		await ReportProgress(cancellationToken);
	}

	public async Task StopAsync(CancellationToken cancellationToken = default)
	{
		logger.LogInformation("Music stop requested. ItemId={ItemId}", _currentItem?.JellyfinId);
		StopProgressLoop();

		if (_engine is not null)
		{
			await _engine.StopAsync(cancellationToken);
		}

		await ReportStopped(cancellationToken);
		DetachEngineEvents();
		_resolvedMedia.Clear();
		_currentItem = null;
		_currentMediaResponse = null;
		_currentSource = null;
		_request = null;
		_engine = null;
		_queuedEngine = null;
	}

	public async Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
	{
		if (_engine is null)
		{
			return;
		}

		logger.LogInformation("Music seek requested. ItemId={ItemId}, Position={Position}", _currentItem?.JellyfinId, position);
		await _engine.SeekAsync(position, cancellationToken);
		await ReportProgress(cancellationToken);
	}

	private async Task PlayQueueAsync(IReadOnlyList<PlaybackItem> items, PlaybackItem? selectedItem, CancellationToken cancellationToken)
	{
		if (selectedItem is null)
		{
			throw new InvalidOperationException("No playable audio items were found.");
		}

		var startIndex = Math.Max(0, items.ToList().FindIndex(x => x.JellyfinId == selectedItem.JellyfinId));
		logger.LogInformation("Music play queue requested. StartItemId={StartItemId}, QueueCount={QueueCount}, StartIndex={StartIndex}",
			selectedItem.JellyfinId,
			items.Count,
			startIndex);

		await PlaybackService.PlayAsync(new PlaybackRequest
		{
			Kind = PlaybackKind.Music,
			StartItem = selectedItem,
			QueueItems = items,
			StartIndex = startIndex,
			StartPosition = selectedItem.Item.UserData?.PlaybackPositionTicks is > 0 and var ticks ? TimeSpan.FromTicks(ticks) : null
		}, cancellationToken);
	}

	private async Task<IReadOnlyList<PlaybackItem>> BuildMusicItems(BaseItemDto item, CancellationToken cancellationToken)
	{
		var audioItems = await jellyfinClient.GetPlayableAudioItems(item, cancellationToken);
		return audioItems
			.Where(x => x.Id is not null)
			.Select(x => PlaybackItem.FromDto(x, PlaybackKind.Music) with { Metadata = BuildMetadata(x) })
			.ToList();
	}

	private static PlaybackItem? FindSelectedItem(IReadOnlyList<PlaybackItem> items, BaseItemDto selectedTrack)
	{
		return selectedTrack.Id is { } id ? items.FirstOrDefault(x => x.JellyfinId == id) : null;
	}

	private static MusicPlaybackMetadata BuildMetadata(BaseItemDto item)
	{
		return new MusicPlaybackMetadata
		{
			Album = item.Album,
			Artists = item.Artists ?? [],
			TrackNumber = item.IndexNumber,
			DiscNumber = item.ParentIndexNumber
		};
	}

	private MusicQueuePolicy EnsureQueuePolicy()
	{
		return _queuePolicy ??= new MusicQueuePolicy(PlaybackService.Queue);
	}

	private async Task MoveToCurrentQueueItem(PlaybackItem? item, bool restartEngine, CancellationToken cancellationToken)
	{
		if (item is null)
		{
			await PlaybackService.StopAsync(cancellationToken);
			return;
		}

		await ReportStopped(cancellationToken);
		_currentItem = item;
		await OpenCurrentQueueWindow(cancellationToken);

		if (restartEngine && _engine is not null)
		{
			await _engine.PlayAsync(cancellationToken);
		}

		await ReportStarted(cancellationToken);
	}

	private async Task OpenCurrentQueueWindow(CancellationToken cancellationToken)
	{
		if (_engine is null || _currentItem is null)
		{
			return;
		}

		var currentSource = await ResolveSource(_currentItem, cancellationToken);
		_currentSource = currentSource;
		_currentMediaResponse = _resolvedMedia[_currentItem.JellyfinId];

		var sources = new List<MediaSource> { currentSource };
		if (RepeatMode is not PlaybackRepeatMode.One && _queuePolicy?.NextItem is { } nextItem)
		{
			sources.Add(await ResolveSource(nextItem, cancellationToken));
		}

		if (_queuedEngine is not null)
		{
			await _queuedEngine.SetQueueAsync(sources, 0, cancellationToken);
		}
		else
		{
			await _engine.OpenAsync(currentSource, cancellationToken);
		}
	}

	private async Task RefreshNativeQueue(CancellationToken cancellationToken)
	{
		if (_engine is null || _currentItem is null)
		{
			return;
		}

		var wasPlaying = _engine.IsPlaying;
		var position = _engine.Position;
		await OpenCurrentQueueWindow(cancellationToken);
		if (position > TimeSpan.Zero)
		{
			await _engine.SeekAsync(position, cancellationToken);
		}

		if (wasPlaying)
		{
			await _engine.PlayAsync(cancellationToken);
		}
	}

	private async Task<MediaSource> ResolveSource(PlaybackItem item, CancellationToken cancellationToken)
	{
		if (!_resolvedMedia.TryGetValue(item.JellyfinId, out var media))
		{
			media = await jellyfinClient.GetMediaUrl(item.Item, cancellationToken)
				?? throw new InvalidOperationException($"No playable audio source was returned for {item.JellyfinId}.");
			_resolvedMedia[item.JellyfinId] = media;
		}

		return new MediaSource(
			media.Uri,
			media.PlaybackSessionId,
			media.MediaSourceId,
			media.PlayMethod,
			media.MediaSourceInfo,
			media.MediaSourceInfo.DefaultAudioStreamIndex ?? 0);
	}

	private void AttachEngineEvents()
	{
		if (_engine is null)
		{
			return;
		}

		_engine.MediaEnded += OnMediaEnded;
		_engine.PlaybackFailed += OnPlaybackFailed;
		if (_queuedEngine is not null)
		{
			_queuedEngine.CurrentItemChanged += OnQueuedCurrentItemChanged;
		}
	}

	private void DetachEngineEvents()
	{
		if (_engine is not null)
		{
			_engine.MediaEnded -= OnMediaEnded;
			_engine.PlaybackFailed -= OnPlaybackFailed;
		}

		if (_queuedEngine is not null)
		{
			_queuedEngine.CurrentItemChanged -= OnQueuedCurrentItemChanged;
		}
	}

	private void OnMediaEnded(object? sender, EventArgs e)
	{
		_ = AdvanceAfterEnded();
	}

	private void OnPlaybackFailed(object? sender, PlaybackErrorEventArgs e)
	{
		logger.LogError(e.Exception, "Music playback failed. ItemId={ItemId}, Message={Message}", _currentItem?.JellyfinId, e.Message);
	}

	private void OnQueuedCurrentItemChanged(object? sender, QueueItemChangedEventArgs e)
	{
		if (e.Index <= 0)
		{
			return;
		}

		_ = AdvanceAfterNativeQueueMove(e.Index);
	}

	private async Task AdvanceAfterEnded()
	{
		await _transitionLock.WaitAsync();
		try
		{
			logger.LogInformation("Music media ended. ItemId={ItemId}, RepeatMode={RepeatMode}", _currentItem?.JellyfinId, RepeatMode);
			var next = _queuePolicy?.MoveNext(automatic: true);
			await MoveToCurrentQueueItem(next, restartEngine: true, CancellationToken.None);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Music automatic advance failed. ItemId={ItemId}", _currentItem?.JellyfinId);
		}
		finally
		{
			_transitionLock.Release();
		}
	}

	private async Task AdvanceAfterNativeQueueMove(int nativeIndex)
	{
		await _transitionLock.WaitAsync();
		try
		{
			logger.LogInformation("Music native queue advanced. NativeIndex={NativeIndex}, PreviousItemId={PreviousItemId}", nativeIndex, _currentItem?.JellyfinId);
			await ReportStopped(CancellationToken.None, _currentItem?.Duration ?? TimeSpan.Zero);
			var next = _queuePolicy?.MoveNext(automatic: true);
			if (next is null)
			{
				await StopAsync(CancellationToken.None);
				return;
			}

			_currentItem = next;
			_currentSource = await ResolveSource(next, CancellationToken.None);
			_currentMediaResponse = _resolvedMedia[next.JellyfinId];
			await ReportStarted(CancellationToken.None);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Music native queue advance handling failed. NativeIndex={NativeIndex}", nativeIndex);
		}
		finally
		{
			_transitionLock.Release();
		}
	}

	private void StartProgressLoop()
	{
		StopProgressLoop();
		_progressCancellation = new CancellationTokenSource();
		_ = RunProgressLoop(_progressCancellation.Token);
	}

	private void StopProgressLoop()
	{
		if (_progressCancellation is null)
		{
			return;
		}

		_progressCancellation.Cancel();
		_progressCancellation.Dispose();
		_progressCancellation = null;
	}

	private async Task RunProgressLoop(CancellationToken cancellationToken)
	{
		using var timer = new PeriodicTimer(ProgressReportInterval);
		try
		{
			while (await timer.WaitForNextTickAsync(cancellationToken))
			{
				await ReportProgress(cancellationToken);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Music progress reporting loop failed. ItemId={ItemId}", _currentItem?.JellyfinId);
		}
	}

	private async Task ReportStarted(CancellationToken cancellationToken)
	{
		if (_currentItem is null || _currentMediaResponse is null)
		{
			return;
		}

		_playbackStartTimeTicks = TimeProvider.System.GetTimestamp();
		logger.LogInformation("Music playback start report requested. ItemId={ItemId}, MediaSourceId={MediaSourceId}", _currentItem.JellyfinId, _currentMediaResponse.MediaSourceId);
		await jellyfinClient.ReportPlaybackStarted(CreateProgressInfo(Position));
	}

	private async Task ReportProgress(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (_currentItem is null || _currentMediaResponse is null)
		{
			return;
		}

		await jellyfinClient.Progress(CreateProgressInfo(Position));
	}

	private async Task ReportStopped(CancellationToken cancellationToken, TimeSpan? positionOverride = null)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (_currentItem is null || _currentMediaResponse is null)
		{
			return;
		}

		var position = positionOverride ?? Position;
		await jellyfinClient.Stop(new PlaybackStopInfo
		{
			ItemId = _currentItem.JellyfinId,
			MediaSourceId = _currentMediaResponse.MediaSourceId,
			SessionId = _currentMediaResponse.PlaybackSessionId,
			PositionTicks = position.Ticks
		});
	}

	private PlaybackProgressInfo CreateProgressInfo(TimeSpan position)
	{
		if (_currentItem is null || _currentMediaResponse is null)
		{
			throw new InvalidOperationException("Cannot create a music playback report without an active item.");
		}

		return new PlaybackProgressInfo
		{
			ItemId = _currentItem.JellyfinId,
			MediaSourceId = _currentMediaResponse.MediaSourceId,
			SessionId = _currentMediaResponse.PlaybackSessionId,
			PlaySessionId = _currentMediaResponse.PlaybackSessionId,
			PositionTicks = position.Ticks,
			IsPaused = _engine?.IsPlaying != true,
			IsMuted = _engine?.IsMuted ?? false,
			PlayMethod = _currentMediaResponse.PlayMethod,
			PlaybackStartTimeTicks = _playbackStartTimeTicks,
			AudioStreamIndex = _currentMediaResponse.MediaSourceInfo.DefaultAudioStreamIndex
		};
	}
}
