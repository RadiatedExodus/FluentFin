namespace FluentFin.Core.Playback.Lyrics;

public interface ILyricsProvider
{
	string Name { get; }
	Task<LyricsDocument?> GetLyricsAsync(PlaybackItem item, CancellationToken cancellationToken = default);
}
