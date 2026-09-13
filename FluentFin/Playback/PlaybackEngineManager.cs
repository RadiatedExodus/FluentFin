using FluentFin.Core.Contracts.Services;
using FluentFin.Core.Playback;
using FluentFin.Core.Settings;
using Microsoft.Extensions.Logging;

namespace FluentFin.Playback;

public sealed class PlaybackEngineManager(
	ISettings settings,
	ILogger<PlaybackEngineManager> logger) : IPlaybackEngineManager
	, IHostedPlaybackEngineRegistry
{
	private IMediaPlaybackEngine? _registeredEngine;

	public IMediaPlaybackEngine? ActiveEngine { get; private set; }

	public void RegisterHostedPlayer(MediaPlayerType type, IMediaPlayerController controller)
	{
		var id = type.ToString();
		logger.LogInformation("Registering hosted media player engine. EngineId={EngineId}", id);
		_registeredEngine = new MediaPlayerControllerEngineAdapter(id, id, controller);
	}

	public Task<IMediaPlaybackEngine> ActivateAsync(PlaybackKind kind, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		logger.LogInformation("Activating playback engine. Kind={Kind}, ConfiguredVideoEngine={ConfiguredVideoEngine}", kind, settings.MediaPlayer);

		if (kind is PlaybackKind.Music)
		{
			throw new NotSupportedException("Music playback engines are not implemented yet.");
		}

		if (_registeredEngine is null)
		{
			throw new InvalidOperationException("No hosted media player is registered yet.");
		}

		ActiveEngine = _registeredEngine;
		return Task.FromResult(ActiveEngine);
	}

	public async Task DeactivateAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (ActiveEngine is null)
		{
			return;
		}

		logger.LogInformation("Deactivating playback engine. EngineId={EngineId}", ActiveEngine.Id);
		await ActiveEngine.StopAsync(cancellationToken);
		ActiveEngine = null;
	}
}
