using FluentFin.Core.Contracts.Services;

namespace FluentFin.Core.Playback;

public interface IMediaPlaybackEngine : IAsyncDisposable
{
	string Id { get; }
	string DisplayName { get; }
	PlaybackEngineFeature Features { get; }
	PlaybackState State { get; }
	TimeSpan Position { get; }
	TimeSpan Duration { get; }
	bool IsPlaying { get; }
	bool IsMuted { get; }
	double Volume { get; set; }

	event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;
	event EventHandler<PositionChangedEventArgs>? PositionChanged;
	event EventHandler<DurationChangedEventArgs>? DurationChanged;
	event EventHandler? MediaEnded;
	event EventHandler? MediaLoaded;
	event EventHandler<PlaybackErrorEventArgs>? PlaybackFailed;

	Task OpenAsync(MediaSource source, CancellationToken cancellationToken = default);
	Task PlayAsync(CancellationToken cancellationToken = default);
	Task PauseAsync(CancellationToken cancellationToken = default);
	Task StopAsync(CancellationToken cancellationToken = default);
	Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);
}

[Flags]
public enum PlaybackEngineFeature
{
	None = 0,
	Video = 1 << 0,
	Audio = 1 << 1,
	Subtitles = 1 << 2,
	ExternalSubtitles = 1 << 3,
	AudioTrackSelection = 1 << 4,
	PlaybackSpeed = 1 << 5,
	HardwareDecoding = 1 << 6,
	GaplessPlayback = 1 << 7
}

public interface ISubtitlePlayback
{
	int? SubtitleTrackIndex { get; }
	void OpenExternalSubtitleTrack(string url);
	void OpenInternalSubtitleTrack(int trackIndex, int subtitleIndex);
	void DisableSubtitles();
}

public interface IAudioTrackPlayback
{
	int? AudioTrackIndex { get; }
	IEnumerable<AudioTrack> GetAudioTracks();
	void OpenAudioTrack(int index);
}

public interface IPlaybackSpeedControl
{
	double PlaybackSpeed { get; set; }
}

public interface IQueuedPlaybackEngine : IMediaPlaybackEngine
{
	Task SetQueueAsync(IReadOnlyList<MediaSource> sources, int startIndex, CancellationToken cancellationToken = default);
	Task SkipNextAsync(CancellationToken cancellationToken = default);
	Task SkipPreviousAsync(CancellationToken cancellationToken = default);
}
