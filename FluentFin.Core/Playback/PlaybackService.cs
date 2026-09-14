using Microsoft.Extensions.Logging;

namespace FluentFin.Core.Playback;

public sealed class PlaybackService : IPlaybackService
{
	private readonly IPlaybackEngineManager _engineManager;
	private readonly ILogger<PlaybackService> _logger;
	private readonly Dictionary<PlaybackKind, IPlaybackController> _controllers;
	private IPlaybackController? _activeController;
	private IMediaPlaybackEngine? _activeEngine;
	private PlaybackState? _stateOverride;

	public event EventHandler? PlaybackChanged;
	public event EventHandler<PlaybackQueueChangedEventArgs>? QueueChanged;
	public event EventHandler? VolumeChanged;

	public PlaybackKind? CurrentKind => _activeController?.Kind;
	public PlaybackItem? CurrentItem => Queue.Current;
	public PlaybackState State => _stateOverride ?? _activeController?.State ?? PlaybackState.Stopped;
	public TimeSpan Position => _activeController?.Position ?? TimeSpan.Zero;
	public TimeSpan Duration => _activeController?.Duration ?? TimeSpan.Zero;
	public MediaSource? CurrentSource => _activeController?.CurrentSource;
	public PlaybackQueue Queue { get; } = new();
	public double Volume => _activeEngine?.Volume ?? 1d;

	public PlaybackService(
		IEnumerable<IPlaybackController> controllers,
		IPlaybackEngineManager engineManager,
		ILogger<PlaybackService> logger)
	{
		_engineManager = engineManager;
		_logger = logger;
		_controllers = controllers.ToDictionary(x => x.Kind);
		Queue.Changed += (_, e) => QueueChanged?.Invoke(this, e);
	}

	public async Task PlayAsync(PlaybackRequest request, CancellationToken cancellationToken = default)
	{
		_logger.LogInformation("PlaybackService PlayAsync requested. Kind={Kind}, ItemId={ItemId}, StartIndex={StartIndex}, QueueCount={QueueCount}",
			request.Kind, request.StartItem.JellyfinId, request.StartIndex, request.EffectiveQueue.Count);

		if (!_controllers.TryGetValue(request.Kind, out var controller))
		{
			throw new NotSupportedException($"No playback controller is registered for {request.Kind}.");
		}

		var isDifferentItem = CurrentItem?.JellyfinId != request.StartItem.JellyfinId;
		if (_activeController is { } active && (active != controller || isDifferentItem))
		{
			_logger.LogInformation("Stopping active playback controller before switching. From={From}, To={To}, IsDifferentItem={IsDifferentItem}",
				active.Kind, controller.Kind, isDifferentItem);
			await active.StopAsync(cancellationToken);

			if (active != controller)
			{
				DetachEngineEvents();
				await _engineManager.DeactivateAsync(cancellationToken);
			}
		}

		Queue.Replace(request.EffectiveQueue, request.StartIndex);
		_activeController = controller;
		_stateOverride = PlaybackState.Opening;
		OnPlaybackChanged();

		try
		{
			var engine = await _engineManager.ActivateAsync(request.Kind, cancellationToken);
			AttachEngineEvents(engine);
			await controller.PrepareAsync(request, engine, cancellationToken);
			await controller.StartAsync(cancellationToken);
			_stateOverride = null;
			OnPlaybackChanged();
		}
		catch (Exception ex)
		{
			_stateOverride = null;
			_logger.LogError(ex, "PlaybackService failed to start playback. Kind={Kind}, ItemId={ItemId}",
				request.Kind, request.StartItem.JellyfinId);
			OnPlaybackChanged();
			throw;
		}
	}

	public async Task PauseAsync(CancellationToken cancellationToken = default)
	{
		_logger.LogInformation("PlaybackService PauseAsync requested. Kind={Kind}, ItemId={ItemId}", CurrentKind, CurrentItem?.JellyfinId);
		if (_activeController is { } controller)
		{
			await controller.PauseAsync(cancellationToken);
			OnPlaybackChanged();
		}
	}

	public async Task ResumeAsync(CancellationToken cancellationToken = default)
	{
		_logger.LogInformation("PlaybackService ResumeAsync requested. Kind={Kind}, ItemId={ItemId}", CurrentKind, CurrentItem?.JellyfinId);
		if (_activeController is { } controller)
		{
			await controller.ResumeAsync(cancellationToken);
			OnPlaybackChanged();
		}
	}

	public async Task StopAsync(CancellationToken cancellationToken = default)
	{
		_logger.LogInformation("PlaybackService StopAsync requested. Kind={Kind}, ItemId={ItemId}", CurrentKind, CurrentItem?.JellyfinId);
		if (_activeController is { } controller)
		{
			await controller.StopAsync(cancellationToken);
		}

		Queue.Clear();
		_activeController = null;
		_stateOverride = null;
		DetachEngineEvents();
		await _engineManager.DeactivateAsync(cancellationToken);
		OnPlaybackChanged();
	}

	public async Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
	{
		_logger.LogInformation("PlaybackService SeekAsync requested. Kind={Kind}, ItemId={ItemId}, Position={Position}",
			CurrentKind, CurrentItem?.JellyfinId, position);
		if (_activeController is { } controller)
		{
			await controller.SeekAsync(position, cancellationToken);
			OnPlaybackChanged();
		}
	}

	public Task SetVolumeAsync(double volume, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var clamped = Math.Clamp(volume, 0d, 1d);
		_logger.LogInformation("PlaybackService SetVolumeAsync requested. Kind={Kind}, Volume={Volume}", CurrentKind, clamped);
		if (_activeEngine is not null)
		{
			_activeEngine.Volume = clamped;
		}

		VolumeChanged?.Invoke(this, EventArgs.Empty);
		OnPlaybackChanged();
		return Task.CompletedTask;
	}

	private void AttachEngineEvents(IMediaPlaybackEngine engine)
	{
		if (ReferenceEquals(_activeEngine, engine))
		{
			return;
		}

		DetachEngineEvents();
		_activeEngine = engine;
		engine.StateChanged += OnEngineStateChanged;
		engine.PositionChanged += OnEnginePositionChanged;
		engine.DurationChanged += OnEngineDurationChanged;
		engine.MediaEnded += OnEngineMediaEnded;
	}

	private void DetachEngineEvents()
	{
		if (_activeEngine is null)
		{
			return;
		}

		_activeEngine.StateChanged -= OnEngineStateChanged;
		_activeEngine.PositionChanged -= OnEnginePositionChanged;
		_activeEngine.DurationChanged -= OnEngineDurationChanged;
		_activeEngine.MediaEnded -= OnEngineMediaEnded;
		_activeEngine = null;
	}

	private void OnEngineStateChanged(object? sender, PlaybackStateChangedEventArgs e) => OnPlaybackChanged();
	private void OnEnginePositionChanged(object? sender, PositionChangedEventArgs e) => OnPlaybackChanged();
	private void OnEngineDurationChanged(object? sender, DurationChangedEventArgs e) => OnPlaybackChanged();
	private void OnEngineMediaEnded(object? sender, EventArgs e) => OnPlaybackChanged();
	private void OnPlaybackChanged() => PlaybackChanged?.Invoke(this, EventArgs.Empty);
}
