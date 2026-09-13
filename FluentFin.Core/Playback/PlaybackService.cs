using Microsoft.Extensions.Logging;

namespace FluentFin.Core.Playback;

public sealed class PlaybackService(
	IEnumerable<IPlaybackController> controllers,
	IPlaybackEngineManager engineManager,
	ILogger<PlaybackService> logger) : IPlaybackService
{
	private readonly Dictionary<PlaybackKind, IPlaybackController> _controllers = controllers.ToDictionary(x => x.Kind);
	private IPlaybackController? _activeController;

	public PlaybackKind? CurrentKind => _activeController?.Kind;
	public PlaybackItem? CurrentItem => Queue.Current;
	public PlaybackState State => _activeController?.State ?? PlaybackState.Stopped;
	public TimeSpan Position => _activeController?.Position ?? TimeSpan.Zero;
	public TimeSpan Duration => _activeController?.Duration ?? TimeSpan.Zero;
	public PlaybackQueue Queue { get; } = new();

	public async Task PlayAsync(PlaybackRequest request, CancellationToken cancellationToken = default)
	{
		logger.LogInformation("PlaybackService PlayAsync requested. Kind={Kind}, ItemId={ItemId}, StartIndex={StartIndex}, QueueCount={QueueCount}",
			request.Kind, request.StartItem.JellyfinId, request.StartIndex, request.EffectiveQueue.Count);

		if (!_controllers.TryGetValue(request.Kind, out var controller))
		{
			throw new NotSupportedException($"No playback controller is registered for {request.Kind}.");
		}

		if (_activeController is { } active && active != controller)
		{
			logger.LogInformation("Stopping active playback controller before switching. From={From}, To={To}", active.Kind, controller.Kind);
			await active.StopAsync(cancellationToken);
			await engineManager.DeactivateAsync(cancellationToken);
		}

		Queue.Replace(request.EffectiveQueue, request.StartIndex);
		_activeController = controller;

		var engine = await engineManager.ActivateAsync(request.Kind, cancellationToken);
		await controller.PrepareAsync(request, engine, cancellationToken);
		await controller.StartAsync(cancellationToken);
	}

	public async Task PauseAsync(CancellationToken cancellationToken = default)
	{
		logger.LogInformation("PlaybackService PauseAsync requested. Kind={Kind}, ItemId={ItemId}", CurrentKind, CurrentItem?.JellyfinId);
		if (_activeController is { } controller)
		{
			await controller.PauseAsync(cancellationToken);
		}
	}

	public async Task ResumeAsync(CancellationToken cancellationToken = default)
	{
		logger.LogInformation("PlaybackService ResumeAsync requested. Kind={Kind}, ItemId={ItemId}", CurrentKind, CurrentItem?.JellyfinId);
		if (_activeController is { } controller)
		{
			await controller.ResumeAsync(cancellationToken);
		}
	}

	public async Task StopAsync(CancellationToken cancellationToken = default)
	{
		logger.LogInformation("PlaybackService StopAsync requested. Kind={Kind}, ItemId={ItemId}", CurrentKind, CurrentItem?.JellyfinId);
		if (_activeController is { } controller)
		{
			await controller.StopAsync(cancellationToken);
		}

		Queue.Clear();
		_activeController = null;
		await engineManager.DeactivateAsync(cancellationToken);
	}

	public async Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
	{
		logger.LogInformation("PlaybackService SeekAsync requested. Kind={Kind}, ItemId={ItemId}, Position={Position}",
			CurrentKind, CurrentItem?.JellyfinId, position);
		if (_activeController is { } controller)
		{
			await controller.SeekAsync(position, cancellationToken);
		}
	}
}
