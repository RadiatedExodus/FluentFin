namespace FluentFin.Core.Playback.Lyrics;

public interface ILyricsService
{
	Task<LyricsDocument?> GetLyricsAsync(PlaybackItem item, CancellationToken cancellationToken = default);
}
