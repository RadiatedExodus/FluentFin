using System.Reactive;
using System.Reactive.Subjects;
using FluentFin.Core.Contracts.Services;
using FluentFin.Core.Playback;
using FluentFin.Playback;
using Microsoft.Extensions.Logging;

namespace FluentFin.MediaPlayers;

public sealed class PlaybackServiceMediaPlayerControllerAdapter : IMediaPlayerController
{
	private readonly IPlaybackService _playbackService;
	private readonly IPlaybackEngineManager _engineManager;
	private readonly ILogger<PlaybackServiceMediaPlayerControllerAdapter> _logger;
	private readonly Subject<TimeSpan> _durationChanged = new();
	private readonly Subject<TimeSpan> _positionChanged = new();
	private readonly Subject<Unit> _playing = new();
	private readonly Subject<Unit> _paused = new();
	private readonly Subject<Unit> _ended = new();
	private readonly Subject<Unit> _errored = new();
	private readonly Subject<Unit> _stopped = new();
	private readonly Subject<Unit> _mediaLoaded = new();
	private readonly Subject<double> _volumeChanged = new();
	private readonly Subject<string> _subtitleText = new();
	private IMediaPlaybackEngine? _subscribedEngine;
	private double _volume = 100;

	public PlaybackServiceMediaPlayerControllerAdapter(
		IPlaybackService playbackService,
		IPlaybackEngineManager engineManager,
		ILogger<PlaybackServiceMediaPlayerControllerAdapter> logger)
	{
		_playbackService = playbackService;
		_engineManager = engineManager;
		_logger = logger;
		_playbackService.PlaybackChanged += OnPlaybackChanged;
	}

	public MediaPlayerState State => ConvertState(_playbackService.State);
	public TimeSpan Position => _playbackService.Position;
	public bool IsPlaying => _playbackService.State is PlaybackState.Playing;
	public bool IsMuted => _engineManager.ActiveEngine?.IsMuted ?? false;
	public int? SubtitleTrackIndex => (_engineManager.ActiveEngine as ISubtitlePlayback)?.SubtitleTrackIndex;
	public int? AudioTrackIndex => (_engineManager.ActiveEngine as IAudioTrackPlayback)?.AudioTrackIndex;

	public double Volume
	{
		get => _engineManager.ActiveEngine is { } engine ? engine.Volume * 100 : _volume;
		set
		{
			_volume = Math.Clamp(value, 0, 100);
			_ = _playbackService.SetVolumeAsync(_volume / 100d);
			_volumeChanged.OnNext(_volume);
		}
	}

	public IObservable<TimeSpan> DurationChanged => _durationChanged;
	public IObservable<TimeSpan> PositionChanged => _positionChanged;
	public IObservable<Unit> Playing => _playing;
	public IObservable<Unit> Paused => _paused;
	public IObservable<Unit> Ended => _ended;
	public IObservable<Unit> Errored => _errored;
	public IObservable<Unit> Stopped => _stopped;
	public IObservable<Unit> MediaLoaded => _mediaLoaded;
	public IObservable<double> VolumeChanged => _volumeChanged;
	public IObservable<string> SubtitleText => _subtitleText;

	public bool Play()
	{
		_logger.LogInformation("PlaybackService media player adapter play requested");
		_ = _playbackService.ResumeAsync();
		return true;
	}

	public bool Play(Uri uri, int defaultAudioIndex = 0)
	{
		_logger.LogInformation("PlaybackService media player adapter direct URI play requested. Uri={Uri}", uri);
		_ = PlayDirectUriAsync(uri, defaultAudioIndex);
		return true;
	}

	public void Pause()
	{
		_logger.LogInformation("PlaybackService media player adapter pause requested");
		_ = _playbackService.PauseAsync();
	}

	public void Stop()
	{
		_logger.LogInformation("PlaybackService media player adapter stop requested");
		_ = _playbackService.StopAsync();
	}

	public void OpenExternalSubtitleTrack(string url) => (_engineManager.ActiveEngine as ISubtitlePlayback)?.OpenExternalSubtitleTrack(url);
	public void OpenInternalSubtitleTrack(int trackIndex, int subtitleIndex) => (_engineManager.ActiveEngine as ISubtitlePlayback)?.OpenInternalSubtitleTrack(trackIndex, subtitleIndex);
	public void DisableSubtitles() => (_engineManager.ActiveEngine as ISubtitlePlayback)?.DisableSubtitles();
	public void OpenAudioTrack(int index) => (_engineManager.ActiveEngine as IAudioTrackPlayback)?.OpenAudioTrack(index);
	public void SeekTo(TimeSpan timeSpan) => _ = _playbackService.SeekAsync(timeSpan);
	public IEnumerable<AudioTrack> GetAudioTracks() => (_engineManager.ActiveEngine as IAudioTrackPlayback)?.GetAudioTracks() ?? [];

	public void AttachActiveEngine()
	{
		if (ReferenceEquals(_subscribedEngine, _engineManager.ActiveEngine))
		{
			return;
		}

		if (_subscribedEngine is not null)
		{
			_subscribedEngine.StateChanged -= OnStateChanged;
			_subscribedEngine.PositionChanged -= OnPositionChanged;
			_subscribedEngine.DurationChanged -= OnDurationChanged;
			_subscribedEngine.MediaLoaded -= OnMediaLoaded;
			_subscribedEngine.MediaEnded -= OnMediaEnded;
			_subscribedEngine.PlaybackFailed -= OnPlaybackFailed;
		}

		_subscribedEngine = _engineManager.ActiveEngine;
		if (_subscribedEngine is null)
		{
			return;
		}

		_subscribedEngine.StateChanged += OnStateChanged;
		_subscribedEngine.PositionChanged += OnPositionChanged;
		_subscribedEngine.DurationChanged += OnDurationChanged;
		_subscribedEngine.MediaLoaded += OnMediaLoaded;
		_subscribedEngine.MediaEnded += OnMediaEnded;
		_subscribedEngine.PlaybackFailed += OnPlaybackFailed;
		_durationChanged.OnNext(_subscribedEngine.Duration);
		_positionChanged.OnNext(_subscribedEngine.Position);
	}

	public void Dispose()
	{
		if (_subscribedEngine is not null)
		{
			_subscribedEngine.StateChanged -= OnStateChanged;
			_subscribedEngine.PositionChanged -= OnPositionChanged;
			_subscribedEngine.DurationChanged -= OnDurationChanged;
			_subscribedEngine.MediaLoaded -= OnMediaLoaded;
			_subscribedEngine.MediaEnded -= OnMediaEnded;
			_subscribedEngine.PlaybackFailed -= OnPlaybackFailed;
		}

		_durationChanged.Dispose();
		_positionChanged.Dispose();
		_playing.Dispose();
		_paused.Dispose();
		_ended.Dispose();
		_errored.Dispose();
		_stopped.Dispose();
		_mediaLoaded.Dispose();
		_volumeChanged.Dispose();
		_subtitleText.Dispose();
		_playbackService.PlaybackChanged -= OnPlaybackChanged;
	}

	private void OnPlaybackChanged(object? sender, EventArgs e) => AttachActiveEngine();

	private async Task PlayDirectUriAsync(Uri uri, int defaultAudioIndex)
	{
		var engine = await _engineManager.ActivateAsync(PlaybackKind.Video);
		AttachActiveEngine();
		await engine.OpenAsync(new MediaSource(uri, DefaultAudioStreamIndex: defaultAudioIndex));
		await engine.PlayAsync();
	}

	private void OnStateChanged(object? sender, PlaybackStateChangedEventArgs e)
	{
		switch (e.State)
		{
			case PlaybackState.Playing:
				_playing.OnNext(Unit.Default);
				break;
			case PlaybackState.Paused:
				_paused.OnNext(Unit.Default);
				break;
			case PlaybackState.Stopped:
				_stopped.OnNext(Unit.Default);
				break;
			case PlaybackState.Ended:
				_ended.OnNext(Unit.Default);
				break;
			case PlaybackState.Error:
				_errored.OnNext(Unit.Default);
				break;
		}
	}

	private void OnPositionChanged(object? sender, PositionChangedEventArgs e) => _positionChanged.OnNext(e.Position);
	private void OnDurationChanged(object? sender, DurationChangedEventArgs e) => _durationChanged.OnNext(e.Duration);
	private void OnMediaLoaded(object? sender, EventArgs e) => _mediaLoaded.OnNext(Unit.Default);
	private void OnMediaEnded(object? sender, EventArgs e) => _ended.OnNext(Unit.Default);
	private void OnPlaybackFailed(object? sender, PlaybackErrorEventArgs e) => _errored.OnNext(Unit.Default);

	private static MediaPlayerState ConvertState(PlaybackState state)
	{
		return state switch
		{
			PlaybackState.Opening => MediaPlayerState.Opening,
			PlaybackState.Playing => MediaPlayerState.Playing,
			PlaybackState.Paused => MediaPlayerState.Paused,
			PlaybackState.Stopped => MediaPlayerState.Stopped,
			PlaybackState.Ended => MediaPlayerState.Ended,
			PlaybackState.Error => MediaPlayerState.Error,
			_ => MediaPlayerState.Error
		};
	}
}
