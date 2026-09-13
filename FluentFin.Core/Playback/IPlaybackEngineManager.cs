namespace FluentFin.Core.Playback;

public interface IPlaybackEngineManager
{
	IMediaPlaybackEngine? ActiveEngine { get; }

	Task<IMediaPlaybackEngine> ActivateAsync(PlaybackKind kind, CancellationToken cancellationToken = default);
	Task DeactivateAsync(CancellationToken cancellationToken = default);
}
