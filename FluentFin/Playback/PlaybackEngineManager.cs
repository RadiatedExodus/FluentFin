using FluentFin.Core.Playback;
using FluentFin.Core.Settings;
using FluentFin.MediaPlayers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentFin.Playback;

public sealed class PlaybackEngineManager(
	ISettings settings,
	IServiceProvider serviceProvider,
	ILogger<PlaybackEngineManager> logger) : IPlaybackEngineManager
{
	public IMediaPlaybackEngine? ActiveEngine { get; private set; }

	public Task<IMediaPlaybackEngine> ActivateAsync(PlaybackKind kind, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		logger.LogInformation("Activating playback engine. Kind={Kind}, ConfiguredVideoEngine={ConfiguredVideoEngine}", kind, settings.MediaPlayer);

		ActiveEngine = kind is PlaybackKind.Music
			? serviceProvider.GetRequiredService<WindowsMusicPlaybackEngine>()
			: settings.MediaPlayer switch
			{
				MediaPlayerType.Mpv => serviceProvider.GetRequiredService<MpvPlaybackEngine>(),
				MediaPlayerType.Flyleaf => serviceProvider.GetRequiredService<FlyleafPlaybackEngine>(),
				MediaPlayerType.Vlc => serviceProvider.GetRequiredService<VlcPlaybackEngine>(),
				MediaPlayerType.WindowsMediaPlayer => serviceProvider.GetRequiredService<WindowsVideoPlaybackEngine>(),
				_ => throw new NotSupportedException($"Unsupported video playback engine {settings.MediaPlayer}.")
			};

		logger.LogInformation("Playback engine activated. EngineId={EngineId}", ActiveEngine.Id);
		return Task.FromResult(ActiveEngine);
	}

	public Task DeactivateAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (ActiveEngine is null)
		{
			return Task.CompletedTask;
		}

		logger.LogInformation("Deactivating playback engine. EngineId={EngineId}", ActiveEngine.Id);
		ActiveEngine = null;
		return Task.CompletedTask;
	}
}
