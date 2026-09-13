namespace FluentFin.Core.Playback;

public interface IPlaybackService
{
	PlaybackKind? CurrentKind { get; }
	PlaybackItem? CurrentItem { get; }
	PlaybackState State { get; }
	TimeSpan Position { get; }
	TimeSpan Duration { get; }
	MediaSource? CurrentSource { get; }
	PlaybackQueue Queue { get; }

	Task PlayAsync(PlaybackRequest request, CancellationToken cancellationToken = default);
	Task PauseAsync(CancellationToken cancellationToken = default);
	Task ResumeAsync(CancellationToken cancellationToken = default);
	Task StopAsync(CancellationToken cancellationToken = default);
	Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);
}
