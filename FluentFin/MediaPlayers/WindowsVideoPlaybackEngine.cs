using FluentFin.Core.Playback;
using Microsoft.Extensions.Logging;
using Windows.Foundation.Collections;
using Windows.Media.Core;
using Windows.Media.Playback;
using MediaPlaybackState = Windows.Media.Playback.MediaPlayerState;

namespace FluentFin.MediaPlayers;

public sealed class WindowsVideoPlaybackEngine : IMediaPlaybackEngine, ISubtitlePlayback, IAudioTrackPlayback
{
	private readonly MediaPlayer _player = new() { AutoPlay = false };
	private MediaPlaybackItem? _mediaItem;
	private bool _disposed;

	public WindowsVideoPlaybackEngine(ILogger<WindowsVideoPlaybackEngine> logger)
	{
		Logger = logger;
		_player.CommandManager.IsEnabled = false;
		_player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
		_player.PlaybackSession.PositionChanged += OnPositionChanged;
		_player.PlaybackSession.NaturalDurationChanged += OnDurationChanged;
		_player.MediaOpened += OnMediaOpened;
		_player.MediaEnded += OnMediaEnded;
		_player.MediaFailed += OnMediaFailed;
	}

	private ILogger<WindowsVideoPlaybackEngine> Logger { get; }
	public MediaPlayer Player => _player;
	public string Id => "WindowsMediaPlayer";
	public string DisplayName => "Windows Media Player";
	public PlaybackEngineFeature Features => PlaybackEngineFeature.Video | PlaybackEngineFeature.Audio | PlaybackEngineFeature.Subtitles | PlaybackEngineFeature.ExternalSubtitles | PlaybackEngineFeature.AudioTrackSelection;
	public PlaybackState State => ConvertState(SafeGetValue(x => x.CurrentState, MediaPlaybackState.Closed));
	public TimeSpan Position => SafeGetValue(x => x.Position, TimeSpan.Zero);
	public TimeSpan Duration => SafeGetValue(x => x.NaturalDuration, TimeSpan.Zero);
	public bool IsPlaying => SafeGetValue(x => x.CurrentState, MediaPlaybackState.Closed) is MediaPlaybackState.Playing;
	public bool IsMuted
	{
		get => SafeGetValue(x => x.IsMuted, false);
		set => SafeSetValue(x => x.IsMuted = value);
	}

	public double Volume
	{
		get => SafeGetValue(x => x.Volume, 0);
		set => SafeSetValue(x => x.Volume = Math.Clamp(value, 0, 1));
	}

	public int? SubtitleTrackIndex => null;
	public int? AudioTrackIndex => _mediaItem?.AudioTracks.SelectedIndex is { } index ? (int)index : null;

	public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;
	public event EventHandler<PositionChangedEventArgs>? PositionChanged;
	public event EventHandler<DurationChangedEventArgs>? DurationChanged;
	public event EventHandler? MediaEnded;
	public event EventHandler? MediaLoaded;
	public event EventHandler<PlaybackErrorEventArgs>? PlaybackFailed;

	public Task OpenAsync(FluentFin.Core.Playback.MediaSource source, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		Logger.LogInformation("Windows video opening media source. Uri={Uri}, MediaSourceId={MediaSourceId}", source.Uri, source.MediaSourceId);
		var mediaSource = Windows.Media.Core.MediaSource.CreateFromUri(source.Uri);
		_mediaItem = new MediaPlaybackItem(mediaSource);
		_mediaItem.AudioTracksChanged += OnAudioTracksChanged;
		_player.Source = _mediaItem;
		if (source.DefaultAudioStreamIndex >= 0)
		{
			OpenAudioTrack(source.DefaultAudioStreamIndex);
		}

		return Task.CompletedTask;
	}

	public Task PlayAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_player.Play();
		return Task.CompletedTask;
	}

	public Task PauseAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_player.Pause();
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_player.Pause();
		_player.Source = null;
		return Task.CompletedTask;
	}

	public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		SafeSetValue(x => x.Position = position);
		return Task.CompletedTask;
	}

	public void OpenExternalSubtitleTrack(string url)
	{
		if (_mediaItem is null)
		{
			return;
		}

		var track = TimedTextSource.CreateFromUri(new Uri(url));
		_mediaItem.Source.ExternalTimedTextSources.Add(track);
		track.Resolved += Track_Resolved;
	}

	public void OpenInternalSubtitleTrack(int trackIndex, int subtitleIndex)
	{
		if (_mediaItem is null or { TimedMetadataTracks: null })
		{
			return;
		}

		foreach (var track in _mediaItem.TimedMetadataTracks.Index())
		{
			_mediaItem.TimedMetadataTracks.SetPresentationMode((uint)track.Index, TimedMetadataTrackPresentationMode.Disabled);
		}

		if (subtitleIndex >= 0 && subtitleIndex < _mediaItem.TimedMetadataTracks.Count)
		{
			_mediaItem.TimedMetadataTracks.SetPresentationMode((uint)subtitleIndex, TimedMetadataTrackPresentationMode.PlatformPresented);
		}
	}

	public void DisableSubtitles()
	{
		if (_mediaItem is null or { TimedMetadataTracks: null })
		{
			return;
		}

		foreach (var track in _mediaItem.TimedMetadataTracks.Index())
		{
			_mediaItem.TimedMetadataTracks.SetPresentationMode((uint)track.Index, TimedMetadataTrackPresentationMode.Disabled);
		}
	}

	public void OpenAudioTrack(int index)
	{
		if (_mediaItem is null || index < 0 || index >= (int)_mediaItem.AudioTracks.Count)
		{
			return;
		}

		_mediaItem.AudioTracks.SelectedIndex = index;
	}

	public IEnumerable<FluentFin.Core.Playback.AudioTrack> GetAudioTracks()
	{
		if (_mediaItem is null)
		{
			return [];
		}

		return _mediaItem.AudioTracks
			.Select((x, index) => new FluentFin.Core.Playback.AudioTrack(index, x.Language, x.Label));
	}

	public ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return ValueTask.CompletedTask;
		}

		_disposed = true;
		_player.PlaybackSession.PlaybackStateChanged -= OnPlaybackStateChanged;
		_player.PlaybackSession.PositionChanged -= OnPositionChanged;
		_player.PlaybackSession.NaturalDurationChanged -= OnDurationChanged;
		_player.MediaOpened -= OnMediaOpened;
		_player.MediaEnded -= OnMediaEnded;
		_player.MediaFailed -= OnMediaFailed;
		_player.Dispose();
		return ValueTask.CompletedTask;
	}

	private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args) => StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(State));
	private void OnPositionChanged(MediaPlaybackSession sender, object args) => PositionChanged?.Invoke(this, new PositionChangedEventArgs(Position));
	private void OnDurationChanged(MediaPlaybackSession sender, object args) => DurationChanged?.Invoke(this, new DurationChangedEventArgs(Duration));
	private void OnMediaOpened(MediaPlayer sender, object args) => MediaLoaded?.Invoke(this, EventArgs.Empty);
	private void OnMediaEnded(MediaPlayer sender, object args) => MediaEnded?.Invoke(this, EventArgs.Empty);
	private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args) => PlaybackFailed?.Invoke(this, new PlaybackErrorEventArgs(args.ExtendedErrorCode, args.ErrorMessage));
	private void OnAudioTracksChanged(MediaPlaybackItem sender, IVectorChangedEventArgs args) => MediaLoaded?.Invoke(this, EventArgs.Empty);

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

	private void SafeSetValue(Action<MediaPlayer> setter)
	{
		try
		{
			if (!_disposed)
			{
				setter(_player);
			}
		}
		catch
		{
		}
	}

	private static PlaybackState ConvertState(MediaPlaybackState state)
	{
		return state switch
		{
			MediaPlaybackState.Opening => PlaybackState.Opening,
			MediaPlaybackState.Playing => PlaybackState.Playing,
			MediaPlaybackState.Paused => PlaybackState.Paused,
			MediaPlaybackState.Stopped => PlaybackState.Stopped,
			_ => PlaybackState.Stopped
		};
	}

	private void Track_Resolved(TimedTextSource sender, TimedTextSourceResolveResultEventArgs args)
	{
		var tracks = args.Tracks[0].PlaybackItem.TimedMetadataTracks;
		for (var i = 0; i < tracks.Count; i++)
		{
			if (ReferenceEquals(tracks[i], args.Tracks[0]))
			{
				tracks.SetPresentationMode((uint)i, TimedMetadataTrackPresentationMode.PlatformPresented);
				break;
			}
		}
		sender.Resolved -= Track_Resolved;
	}
}
