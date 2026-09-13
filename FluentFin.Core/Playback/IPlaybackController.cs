namespace FluentFin.Core.Playback;

public interface IPlaybackController
{
	PlaybackKind Kind { get; }
	PlaybackState State { get; }
	TimeSpan Position { get; }
	TimeSpan Duration { get; }
	MediaSource? CurrentSource { get; }

	Task PrepareAsync(PlaybackRequest request, IMediaPlaybackEngine engine, CancellationToken cancellationToken = default);
	Task StartAsync(CancellationToken cancellationToken = default);
	Task PauseAsync(CancellationToken cancellationToken = default);
	Task ResumeAsync(CancellationToken cancellationToken = default);
	Task StopAsync(CancellationToken cancellationToken = default);
	Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);
}
