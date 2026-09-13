using System.Reactive.Linq;
using FluentFin.Core.Contracts.Services;
using FluentFin.Core.Playback;

namespace FluentFin.Playback;

public sealed class MediaPlayerControllerEngineAdapter : IMediaPlaybackEngine, ISubtitlePlayback, IAudioTrackPlayback
{
	private readonly IMediaPlayerController _controller;
	private TimeSpan _duration;

	public MediaPlayerControllerEngineAdapter(string id, string displayName, IMediaPlayerController controller)
	{
		Id = id;
		DisplayName = displayName;
		_controller = controller;

		_controller.Playing.Subscribe(_ => StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(PlaybackState.Playing)));
		_controller.Paused.Subscribe(_ => StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(PlaybackState.Paused)));
		_controller.Stopped.Subscribe(_ => StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(PlaybackState.Stopped)));
		_controller.Ended.Subscribe(_ =>
		{
			StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(PlaybackState.Ended));
			MediaEnded?.Invoke(this, EventArgs.Empty);
		});
		_controller.Errored.Subscribe(_ =>
		{
			StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(PlaybackState.Error));
			PlaybackFailed?.Invoke(this, new PlaybackErrorEventArgs(null, "The media player reported a playback error."));
		});
		_controller.MediaLoaded.Subscribe(_ => MediaLoaded?.Invoke(this, EventArgs.Empty));
		_controller.PositionChanged.Subscribe(position => PositionChanged?.Invoke(this, new PositionChangedEventArgs(position)));
		_controller.DurationChanged.Subscribe(duration =>
		{
			_duration = duration;
			DurationChanged?.Invoke(this, new DurationChangedEventArgs(duration));
		});
	}

	public string Id { get; }
	public string DisplayName { get; }
	public PlaybackEngineFeature Features => PlaybackEngineFeature.Video | PlaybackEngineFeature.Audio | PlaybackEngineFeature.Subtitles | PlaybackEngineFeature.ExternalSubtitles | PlaybackEngineFeature.AudioTrackSelection;
	public PlaybackState State => ConvertState(_controller.State);
	public TimeSpan Position => _controller.Position;
	public TimeSpan Duration => _duration;
	public bool IsPlaying => _controller.IsPlaying;
	public bool IsMuted => _controller.IsMuted;
	public int? SubtitleTrackIndex => _controller.SubtitleTrackIndex;
	public int? AudioTrackIndex => _controller.AudioTrackIndex;

	public double Volume
	{
		get => _controller.Volume;
		set => _controller.Volume = value;
	}

	public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;
	public event EventHandler<PositionChangedEventArgs>? PositionChanged;
	public event EventHandler<DurationChangedEventArgs>? DurationChanged;
	public event EventHandler? MediaEnded;
	public event EventHandler? MediaLoaded;
	public event EventHandler<PlaybackErrorEventArgs>? PlaybackFailed;

	public Task OpenAsync(MediaSource source, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var opened = _controller.Play(source.Uri, source.DefaultAudioStreamIndex);
		if (!opened)
		{
			throw new InvalidOperationException($"Unable to open media source with playback engine {Id}.");
		}

		return Task.CompletedTask;
	}

	public Task PlayAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_controller.Play();
		return Task.CompletedTask;
	}

	public Task PauseAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_controller.Pause();
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_controller.Stop();
		return Task.CompletedTask;
	}

	public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_controller.SeekTo(position);
		return Task.CompletedTask;
	}

	public void OpenExternalSubtitleTrack(string url) => _controller.OpenExternalSubtitleTrack(url);
	public void OpenInternalSubtitleTrack(int trackIndex, int subtitleIndex) => _controller.OpenInternalSubtitleTrack(trackIndex, subtitleIndex);
	public void DisableSubtitles() => _controller.DisableSubtitles();
	public IEnumerable<AudioTrack> GetAudioTracks() => _controller.GetAudioTracks();
	public void OpenAudioTrack(int index) => _controller.OpenAudioTrack(index);

	public ValueTask DisposeAsync()
	{
		_controller.Dispose();
		return ValueTask.CompletedTask;
	}

	private static PlaybackState ConvertState(MediaPlayerState state)
	{
		return state switch
		{
			MediaPlayerState.Opening => PlaybackState.Opening,
			MediaPlayerState.Playing => PlaybackState.Playing,
			MediaPlayerState.Paused => PlaybackState.Paused,
			MediaPlayerState.Stopped => PlaybackState.Stopped,
			MediaPlayerState.Ended => PlaybackState.Ended,
			MediaPlayerState.Error => PlaybackState.Error,
			_ => PlaybackState.Error
		};
	}
}
