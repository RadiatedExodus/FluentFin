namespace FluentFin.Core.Playback;

public interface IVideoPlaybackController : IPlaybackController, IPlaybackProgressReporter
{
	event EventHandler? TracksChanged;
	event EventHandler<string>? SubtitleTextChanged;

	IReadOnlyList<AudioTrack> AudioTracks { get; }
	IReadOnlyList<SubtitleTrack> SubtitleTracks { get; }
	int? AudioTrackIndex { get; }
	int? SubtitleTrackIndex { get; }
	string SubtitleText { get; }

	Task SelectAudioTrackAsync(int index, CancellationToken cancellationToken = default);
	Task SelectSubtitleTrackAsync(int index, CancellationToken cancellationToken = default);
	Task DisableSubtitlesAsync(CancellationToken cancellationToken = default);
}
