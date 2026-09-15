namespace FluentFin.Core.Playback;

public interface IPlaybackProgressReporter
{
	Task ReportProgressAsync(CancellationToken cancellationToken = default);
}
