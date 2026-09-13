using FluentFin.Core.Contracts.Services;

namespace FluentFin.Playback;

public interface IHostedPlaybackEngineRegistry
{
	void RegisterHostedPlayer(MediaPlayerType type, IMediaPlayerController controller);
}
