using FluentFin.Core.Playback;
using LibVLCSharp.Shared;
using Microsoft.Extensions.Logging;

namespace FluentFin.MediaPlayers;

public sealed class VlcPlaybackEngine(ILogger<VlcPlaybackEngine> logger) : IMediaPlaybackEngine, ISubtitlePlayback, IAudioTrackPlayback
{
	private readonly TaskCompletionSource _viewAttached = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private LibVLC? _libVlc;
	private MediaPlayer? _player;
	private Media? _media;
	private TimeSpan _position;
	private TimeSpan _duration;
	private int? _pendingDefaultAudioTrackIndex;
	private bool _disposed;

	public string Id => "Vlc";
	public string DisplayName => "VLC";
	public PlaybackEngineFeature Features => PlaybackEngineFeature.Video | PlaybackEngineFeature.Audio | PlaybackEngineFeature.Subtitles | PlaybackEngineFeature.ExternalSubtitles | PlaybackEngineFeature.AudioTrackSelection;
	public PlaybackState State => ConvertState(_player?.State ?? VLCState.Stopped);
	public TimeSpan Position => _position;
	public TimeSpan Duration => _duration;
	public bool IsPlaying => _player?.IsPlaying == true;
	public bool IsMuted
	{
		get => _player?.Mute == true;
		set
		{
			if (_player is not null)
			{
				_player.Mute = value;
			}
		}
	}

	public double Volume
	{
		get => (_player?.Volume ?? 100) / 100d;
		set
		{
			if (_player is not null)
			{
				_player.Volume = (int)Math.Clamp(value * 100, 0, 100);
			}
		}
	}

	public int? SubtitleTrackIndex => _player?.Spu == -1 ? null : _player?.Spu;
	public int? AudioTrackIndex => _player?.AudioTrack == -1 ? null : _player?.AudioTrack;

	public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;
	public event EventHandler<PositionChangedEventArgs>? PositionChanged;
	public event EventHandler<DurationChangedEventArgs>? DurationChanged;
	public event EventHandler? MediaEnded;
	public event EventHandler? MediaLoaded;
	public event EventHandler<PlaybackErrorEventArgs>? PlaybackFailed;
	public event EventHandler? AudioTracksChanged;

	public void AttachVideoView(LibVLCSharp.Platforms.Windows.VideoView view, string[] swapChainOptions)
	{
		ThrowIfDisposed();
		if (_player is not null)
		{
			view.MediaPlayer = _player;
			_viewAttached.TrySetResult();
			return;
		}

		logger.LogInformation("Creating VLC playback engine from WinUI video view");
		_libVlc = new LibVLC(enableDebugLogs: false, swapChainOptions);
		_player = new MediaPlayer(_libVlc);
		_player.Playing += OnPlaying;
		_player.Paused += OnPaused;
		_player.Stopped += OnStopped;
		_player.EndReached += OnEndReached;
		_player.EncounteredError += OnEncounteredError;
		_player.TimeChanged += OnTimeChanged;
		_player.LengthChanged += OnLengthChanged;
		_player.MediaChanged += OnMediaChanged;
		_player.VolumeChanged += OnVolumeChanged;
		view.MediaPlayer = _player;
		_viewAttached.TrySetResult();
	}

	public async Task OpenAsync(MediaSource source, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		await _viewAttached.Task.WaitAsync(cancellationToken);
		ThrowIfDisposed();
		if (_libVlc is null || _player is null)
		{
			throw new InvalidOperationException("VLC video surface is not attached.");
		}

		_media?.Dispose();
		_media = new Media(_libVlc, source.Uri);
		logger.LogInformation("VLC opening media source. Uri={Uri}, MediaSourceId={MediaSourceId}", source.Uri, source.MediaSourceId);
		_player.Media = _media;
		MediaLoaded?.Invoke(this, EventArgs.Empty);
		AudioTracksChanged?.Invoke(this, EventArgs.Empty);

		if (source.DefaultAudioStreamIndex > 0)
		{
			_pendingDefaultAudioTrackIndex = source.DefaultAudioStreamIndex;
		}
	}

	public async Task PlayAsync(CancellationToken cancellationToken = default)
	{
		await _viewAttached.Task.WaitAsync(cancellationToken);
		if (_player is not null && !_player.Play())
		{
			throw new InvalidOperationException("VLC could not start playback.");
		}
	}

	public async Task PauseAsync(CancellationToken cancellationToken = default)
	{
		await _viewAttached.Task.WaitAsync(cancellationToken);
		_player?.Pause();
	}

	public async Task StopAsync(CancellationToken cancellationToken = default)
	{
		await _viewAttached.Task.WaitAsync(cancellationToken);
		_player?.Stop();
	}

	public async Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
	{
		await _viewAttached.Task.WaitAsync(cancellationToken);
		_player?.SeekTo(position);
	}

	public void OpenExternalSubtitleTrack(string url) => _player?.AddSlave(MediaSlaveType.Subtitle, url, true);
	public void OpenInternalSubtitleTrack(int trackIndex, int subtitleIndex) => _player?.SetSpu(trackIndex);
	public void DisableSubtitles() => _player?.SetSpu(-1);
	public void OpenAudioTrack(int index) => _player?.SetAudioTrack(index);
	public IEnumerable<FluentFin.Core.Playback.AudioTrack> GetAudioTracks() => _player?.Media?.Tracks.Where(x => x.TrackType == TrackType.Audio).Select(x => new FluentFin.Core.Playback.AudioTrack(x.Id, x.Language, x.Description)) ?? [];

	public ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return ValueTask.CompletedTask;
		}

		_disposed = true;
		if (_player is not null)
		{
			_player.Playing -= OnPlaying;
			_player.Paused -= OnPaused;
			_player.Stopped -= OnStopped;
			_player.EndReached -= OnEndReached;
			_player.EncounteredError -= OnEncounteredError;
			_player.TimeChanged -= OnTimeChanged;
			_player.LengthChanged -= OnLengthChanged;
			_player.MediaChanged -= OnMediaChanged;
			_player.VolumeChanged -= OnVolumeChanged;
			_player.Dispose();
		}

		_media?.Dispose();
		_libVlc?.Dispose();
		return ValueTask.CompletedTask;
	}

	private void OnPlaying(object? sender, EventArgs e) => StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(PlaybackState.Playing));
	private void OnPaused(object? sender, EventArgs e) => StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(PlaybackState.Paused));
	private void OnStopped(object? sender, EventArgs e) => StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(PlaybackState.Stopped));
	private void OnEndReached(object? sender, EventArgs e)
	{
		StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(PlaybackState.Ended));
		MediaEnded?.Invoke(this, EventArgs.Empty);
	}

	private void OnEncounteredError(object? sender, EventArgs e)
	{
		StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(PlaybackState.Error));
		PlaybackFailed?.Invoke(this, new PlaybackErrorEventArgs(null, "VLC reported a playback error."));
	}

	private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
	{
		_position = TimeSpan.FromMilliseconds(e.Time);
		PositionChanged?.Invoke(this, new PositionChangedEventArgs(_position));
	}

	private void OnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
	{
		_duration = TimeSpan.FromMilliseconds(e.Length);
		DurationChanged?.Invoke(this, new DurationChangedEventArgs(_duration));
		AudioTracksChanged?.Invoke(this, EventArgs.Empty);
		if (_pendingDefaultAudioTrackIndex is { } defaultAudioTrackIndex)
		{
			_pendingDefaultAudioTrackIndex = null;
			OpenAudioTrack(defaultAudioTrackIndex);
		}
	}

	private void OnMediaChanged(object? sender, MediaPlayerMediaChangedEventArgs e)
	{
		MediaLoaded?.Invoke(this, EventArgs.Empty);
		AudioTracksChanged?.Invoke(this, EventArgs.Empty);
	}
	private void OnVolumeChanged(object? sender, MediaPlayerVolumeChangedEventArgs e) => StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(State));

	private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

	private static PlaybackState ConvertState(VLCState state)
	{
		return state switch
		{
			VLCState.Opening => PlaybackState.Opening,
			VLCState.Playing => PlaybackState.Playing,
			VLCState.Paused => PlaybackState.Paused,
			VLCState.Stopped => PlaybackState.Stopped,
			VLCState.Ended => PlaybackState.Ended,
			VLCState.Error => PlaybackState.Error,
			_ => PlaybackState.Stopped
		};
	}
}
