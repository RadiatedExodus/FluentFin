namespace FluentFin.Core.Playback;

public interface IPlaybackService
{
	event EventHandler? PlaybackChanged;
	event EventHandler? MediaLoaded;
	event EventHandler? MediaEnded;
	event EventHandler<PlaybackErrorEventArgs>? PlaybackFailed;
	event EventHandler<PlaybackQueueChangedEventArgs>? QueueChanged;
	event EventHandler? VolumeChanged;

	PlaybackKind? CurrentKind { get; }
	PlaybackItem? CurrentItem { get; }
	PlaybackState State { get; }
	TimeSpan Position { get; }
	TimeSpan Duration { get; }
	MediaSource? CurrentSource { get; }
	PlaybackQueue Queue { get; }
	double Volume { get; }
	bool IsMuted { get; }

	Task PlayAsync(PlaybackRequest request, CancellationToken cancellationToken = default);
	Task PauseAsync(CancellationToken cancellationToken = default);
	Task ResumeAsync(CancellationToken cancellationToken = default);
	Task StopAsync(CancellationToken cancellationToken = default);
	Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);
	Task SetVolumeAsync(double volume, CancellationToken cancellationToken = default);
	Task SetMutedAsync(bool muted, CancellationToken cancellationToken = default);
	Task SkipNextAsync(CancellationToken cancellationToken = default);
	Task SkipPreviousAsync(CancellationToken cancellationToken = default);
	Task SkipToAsync(int queueIndex, CancellationToken cancellationToken = default);
}
