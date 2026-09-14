using FluentFin.Core.Playback;
using Microsoft.Extensions.Logging;
using Windows.Media.Playback;
using WindowsMediaSource = Windows.Media.Core.MediaSource;

namespace FluentFin.MediaPlayers;

public sealed class WindowsMusicPlaybackEngine : IQueuedPlaybackEngine, IPlaybackSpeedControl
{
	private const int QueueWindowSize = 2;
	private readonly ILogger<WindowsMusicPlaybackEngine> _logger;
	private readonly MediaPlayer _player;
	private MediaPlaybackList? _playbackList;
	private bool _disposed;

	public WindowsMusicPlaybackEngine(ILogger<WindowsMusicPlaybackEngine> logger)
	{
		_logger = logger;
		_player = new MediaPlayer { AutoPlay = false };
		_player.CommandManager.IsEnabled = false;
		_player.MediaOpened += OnMediaOpened;
		_player.MediaEnded += OnMediaEnded;
		_player.MediaFailed += OnMediaFailed;
		_player.VolumeChanged += OnVolumeChanged;
		_player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
		_player.PlaybackSession.PositionChanged += OnPositionChanged;
		_player.PlaybackSession.NaturalDurationChanged += OnNaturalDurationChanged;
		_logger.LogInformation("Windows music playback engine created");
	}

	public string Id => "WindowsMusic";
	public string DisplayName => "Windows Music";
	public PlaybackEngineFeature Features => PlaybackEngineFeature.Audio | PlaybackEngineFeature.GaplessPlayback | PlaybackEngineFeature.PlaybackSpeed;
	public PlaybackState State => ConvertState(SafeGetValue(x => x.CurrentState, MediaPlayerState.Closed));
	public TimeSpan Position => SafeGetValue(x => x.Position, TimeSpan.Zero);
	public TimeSpan Duration => SafeGetValue(x => x.NaturalDuration, TimeSpan.Zero);
	public bool IsPlaying => SafeGetValue(x => x.CurrentState, MediaPlayerState.Closed) is MediaPlayerState.Playing;
	public bool IsMuted => SafeGetValue(x => x.IsMuted, false);

	public double Volume
	{
		get => SafeGetValue(x => x.Volume, 0);
		set
		{
			ThrowIfDisposed();
			_player.Volume = Math.Clamp(value, 0, 1);
		}
	}

	public double PlaybackSpeed
	{
		get => _player.PlaybackSession.PlaybackRate;
		set
		{
			ThrowIfDisposed();
			_player.PlaybackSession.PlaybackRate = value;
		}
	}

	public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;
	public event EventHandler<PositionChangedEventArgs>? PositionChanged;
	public event EventHandler<DurationChangedEventArgs>? DurationChanged;
	public event EventHandler? MediaEnded;
	public event EventHandler? MediaLoaded;
	public event EventHandler<PlaybackErrorEventArgs>? PlaybackFailed;
	public event EventHandler<QueueItemChangedEventArgs>? CurrentItemChanged;

	public async Task OpenAsync(MediaSource source, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfDisposed();

		_logger.LogInformation("Windows music playback engine opening source. Uri={Uri}, MediaSourceId={MediaSourceId}, PlayMethod={PlayMethod}",
			source.Uri,
			source.MediaSourceId,
			source.PlayMethod);

		await SetQueueAsync([source], 0, cancellationToken);
	}

	public Task SetQueueAsync(IReadOnlyList<MediaSource> sources, int startIndex, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfDisposed();

		if (sources.Count == 0)
		{
			throw new ArgumentException("Music playback queue must contain at least one source.", nameof(sources));
		}

		var clampedStartIndex = Math.Clamp(startIndex, 0, sources.Count - 1);
		var window = sources
			.Skip(clampedStartIndex)
			.Take(QueueWindowSize)
			.Select(CreatePlaybackItem)
			.ToList();

		var playbackList = new MediaPlaybackList
		{
			AutoRepeatEnabled = false,
			ShuffleEnabled = false
		};

		foreach (var item in window)
		{
			playbackList.Items.Add(item);
		}

		playbackList.CurrentItemChanged += OnCurrentItemChanged;
		playbackList.ItemFailed += OnItemFailed;

		DetachPlaybackList();
		_playbackList = playbackList;
		_player.Source = playbackList;

		_logger.LogInformation("Windows music playback engine queue set. SourceCount={SourceCount}, StartIndex={StartIndex}, NativeWindowCount={NativeWindowCount}",
			sources.Count,
			clampedStartIndex,
			window.Count);

		return Task.CompletedTask;
	}

	public Task PlayAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfDisposed();
		_logger.LogInformation("Windows music playback engine play requested");
		_player.Play();
		return Task.CompletedTask;
	}

	public Task PauseAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfDisposed();
		_logger.LogInformation("Windows music playback engine pause requested");
		_player.Pause();
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfDisposed();
		_logger.LogInformation("Windows music playback engine stop requested");
		_player.Pause();
		_player.Position = TimeSpan.Zero;
		return Task.CompletedTask;
	}

	public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfDisposed();
		_logger.LogInformation("Windows music playback engine seek requested. Position={Position}", position);
		_player.Position = position;
		return Task.CompletedTask;
	}

	public Task SkipNextAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfDisposed();

		var moved = _playbackList?.MoveNext() is not null;
		_logger.LogInformation("Windows music playback engine skip next requested. Moved={Moved}", moved);
		return Task.CompletedTask;
	}

	public Task SkipPreviousAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfDisposed();

		var moved = _playbackList?.MovePrevious() is not null;
		_logger.LogInformation("Windows music playback engine skip previous requested. Moved={Moved}", moved);
		return Task.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return ValueTask.CompletedTask;
		}

		_logger.LogInformation("Windows music playback engine disposing");
		_disposed = true;
		DetachPlaybackList();
		_player.MediaOpened -= OnMediaOpened;
		_player.MediaEnded -= OnMediaEnded;
		_player.MediaFailed -= OnMediaFailed;
		_player.VolumeChanged -= OnVolumeChanged;
		_player.PlaybackSession.PlaybackStateChanged -= OnPlaybackStateChanged;
		_player.PlaybackSession.PositionChanged -= OnPositionChanged;
		_player.PlaybackSession.NaturalDurationChanged -= OnNaturalDurationChanged;
		_player.Dispose();
		return ValueTask.CompletedTask;
	}

	private static MediaPlaybackItem CreatePlaybackItem(MediaSource source)
	{
		var mediaSource = WindowsMediaSource.CreateFromUri(source.Uri);
		return new MediaPlaybackItem(mediaSource);
	}

	private void DetachPlaybackList()
	{
		if (_playbackList is null)
		{
			return;
		}

		_playbackList.CurrentItemChanged -= OnCurrentItemChanged;
		_playbackList.ItemFailed -= OnItemFailed;
		_playbackList = null;
	}

	private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args)
	{
		var state = State;
		_logger.LogDebug("Windows music playback engine state changed. State={State}", state);
		StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(state));
	}

	private void OnPositionChanged(MediaPlaybackSession sender, object args)
	{
		PositionChanged?.Invoke(this, new PositionChangedEventArgs(Position));
	}

	private void OnNaturalDurationChanged(MediaPlaybackSession sender, object args)
	{
		DurationChanged?.Invoke(this, new DurationChangedEventArgs(Duration));
	}

	private void OnMediaOpened(MediaPlayer sender, object args)
	{
		_logger.LogInformation("Windows music playback engine media loaded. Duration={Duration}", Duration);
		MediaLoaded?.Invoke(this, EventArgs.Empty);
	}

	private void OnMediaEnded(MediaPlayer sender, object args)
	{
		_logger.LogInformation("Windows music playback engine media ended");
		StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(PlaybackState.Ended));
		MediaEnded?.Invoke(this, EventArgs.Empty);
	}

	private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
	{
		_logger.LogError("Windows music playback engine failed. Error={Error}, Message={Message}", args.Error, args.ErrorMessage);
		StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(PlaybackState.Error));
		PlaybackFailed?.Invoke(this, new PlaybackErrorEventArgs(null, args.ErrorMessage));
	}

	private void OnVolumeChanged(MediaPlayer sender, object args)
	{
		_logger.LogDebug("Windows music playback engine volume changed. Volume={Volume}, IsMuted={IsMuted}", Volume, IsMuted);
	}

	private void OnCurrentItemChanged(MediaPlaybackList sender, CurrentMediaPlaybackItemChangedEventArgs args)
	{
		var index = sender.Items.IndexOf(args.NewItem);
		_logger.LogInformation("Windows music playback engine current item changed. NativeIndex={NativeIndex}", index);
		if (index >= 0)
		{
			CurrentItemChanged?.Invoke(this, new QueueItemChangedEventArgs(index));
		}
	}

	private void OnItemFailed(MediaPlaybackList sender, MediaPlaybackItemFailedEventArgs args)
	{
		_logger.LogError("Windows music playback engine queue item failed. Error={Error}, NativeIndex={NativeIndex}",
			args.Error,
			sender.Items.IndexOf(args.Item));
		StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(PlaybackState.Error));
		PlaybackFailed?.Invoke(this, new PlaybackErrorEventArgs(null, args.Error.ToString()));
	}

	private T SafeGetValue<T>(Func<MediaPlayer, T> getter, T defaultValue)
	{
		try
		{
			return _disposed ? defaultValue : getter(_player);
		}
		catch
		{
			return defaultValue;
		}
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}

	private static PlaybackState ConvertState(MediaPlayerState state)
	{
		return state switch
		{
			MediaPlayerState.Opening => PlaybackState.Opening,
			MediaPlayerState.Playing => PlaybackState.Playing,
			MediaPlayerState.Paused => PlaybackState.Paused,
			MediaPlayerState.Stopped => PlaybackState.Stopped,
			_ => PlaybackState.Stopped
		};
	}
}
