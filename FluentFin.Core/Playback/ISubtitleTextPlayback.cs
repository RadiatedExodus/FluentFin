namespace FluentFin.Core.Playback;

public interface ISubtitleTextPlayback
{
	event EventHandler<string>? SubtitleTextChanged;

	string SubtitleText { get; }
}
