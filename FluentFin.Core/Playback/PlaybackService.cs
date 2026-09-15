using Microsoft.Extensions.Logging;

namespace FluentFin.Core.Playback;

public sealed class PlaybackService : IPlaybackService
{
	private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(20);
	private readonly IPlaybackEngineManager _engineManager;
	private readonly ILogger<PlaybackService> _logger;
	private readonly Dictionary<PlaybackKind, IPlaybackController> _controllers;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private IPlaybackController? _activeController;
	private IMediaPlaybackEngine? _activeEngine;
	private PlaybackState? _stateOverride;
	private CancellationTokenSource? _progressCts;

	public event EventHandler? PlaybackChanged;
	public event EventHandler? MediaLoaded;
	public event EventHandler? MediaEnded;
	public event EventHandler<PlaybackErrorEventArgs>? PlaybackFailed;
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
	public bool IsMuted => _activeEngine?.IsMuted ?? false;

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
		await _gate.WaitAsync(cancellationToken);
		try
		{
			await PlayCoreAsync(request, replaceQueue: true, cancellationToken);
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task PauseAsync(CancellationToken cancellationToken = default)
	{
		_logger.LogInformation("PlaybackService PauseAsync requested. Kind={Kind}, ItemId={ItemId}", CurrentKind, CurrentItem?.JellyfinId);
		if (_activeController is { } controller)
		{
			await controller.PauseAsync(cancellationToken);
			await ReportProgressAsync(cancellationToken);
			OnPlaybackChanged();
		}
	}

	public async Task ResumeAsync(CancellationToken cancellationToken = default)
	{
		_logger.LogInformation("PlaybackService ResumeAsync requested. Kind={Kind}, ItemId={ItemId}", CurrentKind, CurrentItem?.JellyfinId);
		if (_activeController is { } controller)
		{
			await controller.ResumeAsync(cancellationToken);
			await ReportProgressAsync(cancellationToken);
			OnPlaybackChanged();
		}
	}

	public async Task StopAsync(CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			_logger.LogInformation("PlaybackService StopAsync requested. Kind={Kind}, ItemId={ItemId}", CurrentKind, CurrentItem?.JellyfinId);
			StopProgressLoop();
			if (_activeController is { } controller)
			{
				await ReportProgressAsync(cancellationToken);
				await controller.StopAsync(cancellationToken);
			}

			Queue.Clear();
			_activeController = null;
			_stateOverride = null;
			DetachEngineEvents();
			await _engineManager.DeactivateAsync(cancellationToken);
			OnPlaybackChanged();
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
	{
		_logger.LogInformation("PlaybackService SeekAsync requested. Kind={Kind}, ItemId={ItemId}, Position={Position}",
			CurrentKind, CurrentItem?.JellyfinId, position);
		if (_activeController is { } controller)
		{
			await controller.SeekAsync(position, cancellationToken);
			await ReportProgressAsync(cancellationToken);
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

	public Task SetMutedAsync(bool muted, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_logger.LogInformation("PlaybackService SetMutedAsync requested. Kind={Kind}, Muted={Muted}", CurrentKind, muted);
		if (_activeEngine is not null)
		{
			_activeEngine.IsMuted = muted;
		}

		VolumeChanged?.Invoke(this, EventArgs.Empty);
		OnPlaybackChanged();
		return Task.CompletedTask;
	}

	public async Task SkipNextAsync(CancellationToken cancellationToken = default)
	{
		if (Queue.Next() is null)
		{
			_logger.LogInformation("PlaybackService SkipNextAsync reached end of queue. Kind={Kind}", CurrentKind);
			await StopAsync(cancellationToken);
			return;
		}

		await PlayCurrentQueueItemAsync(cancellationToken);
	}

	public async Task SkipPreviousAsync(CancellationToken cancellationToken = default)
	{
		if (Queue.Previous() is null)
		{
			await SeekAsync(TimeSpan.Zero, cancellationToken);
			return;
		}

		await PlayCurrentQueueItemAsync(cancellationToken);
	}

	public async Task SkipToAsync(int queueIndex, CancellationToken cancellationToken = default)
	{
		if (Queue.MoveTo(queueIndex) is null)
		{
			return;
		}

		await PlayCurrentQueueItemAsync(cancellationToken);
	}

	private async Task PlayCurrentQueueItemAsync(CancellationToken cancellationToken)
	{
		if (Queue.Current is not { } current)
		{
			return;
		}

		var request = new PlaybackRequest
		{
			Kind = current.Kind,
			StartItem = current,
			QueueItems = Queue.Items,
			StartIndex = Queue.CurrentIndex
		};

		await _gate.WaitAsync(cancellationToken);
		try
		{
			await PlayCoreAsync(request, replaceQueue: false, cancellationToken);
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task PlayCoreAsync(PlaybackRequest request, bool replaceQueue, CancellationToken cancellationToken)
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
			StopProgressLoop();
			await ReportProgressAsync(cancellationToken);
			await active.StopAsync(cancellationToken);

			if (active != controller)
			{
				DetachEngineEvents();
				await _engineManager.DeactivateAsync(cancellationToken);
			}
		}

		if (replaceQueue)
		{
			Queue.Replace(request.EffectiveQueue, request.StartIndex);
		}

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
			StartProgressLoop(controller);
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
		engine.MediaLoaded += OnEngineMediaLoaded;
		engine.PlaybackFailed += OnEnginePlaybackFailed;
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
		_activeEngine.MediaLoaded -= OnEngineMediaLoaded;
		_activeEngine.PlaybackFailed -= OnEnginePlaybackFailed;
		_activeEngine = null;
	}

	private void StartProgressLoop(IPlaybackController controller)
	{
		StopProgressLoop();
		if (controller is not IPlaybackProgressReporter reporter)
		{
			return;
		}

		_progressCts = new CancellationTokenSource();
		var token = _progressCts.Token;
		_ = Task.Run(async () =>
		{
			using var timer = new PeriodicTimer(ProgressInterval);
			try
			{
				while (await timer.WaitForNextTickAsync(token))
				{
					await reporter.ReportProgressAsync(token);
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Playback progress reporting loop failed. Kind={Kind}, ItemId={ItemId}", CurrentKind, CurrentItem?.JellyfinId);
			}
		}, token);
	}

	private void StopProgressLoop()
	{
		_progressCts?.Cancel();
		_progressCts?.Dispose();
		_progressCts = null;
	}

	private Task ReportProgressAsync(CancellationToken cancellationToken)
	{
		return _activeController is IPlaybackProgressReporter reporter
			? reporter.ReportProgressAsync(cancellationToken)
			: Task.CompletedTask;
	}

	private void OnEngineStateChanged(object? sender, PlaybackStateChangedEventArgs e) => OnPlaybackChanged();
	private void OnEnginePositionChanged(object? sender, PositionChangedEventArgs e) => OnPlaybackChanged();
	private void OnEngineDurationChanged(object? sender, DurationChangedEventArgs e) => OnPlaybackChanged();

	private void OnEngineMediaLoaded(object? sender, EventArgs e)
	{
		MediaLoaded?.Invoke(this, EventArgs.Empty);
		OnPlaybackChanged();
	}

	private async void OnEngineMediaEnded(object? sender, EventArgs e)
	{
		MediaEnded?.Invoke(this, EventArgs.Empty);
		OnPlaybackChanged();

		if (CurrentKind is PlaybackKind.Video && Queue.CanMoveNext)
		{
			try
			{
				await SkipNextAsync();
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "PlaybackService failed to advance video queue. ItemId={ItemId}", CurrentItem?.JellyfinId);
			}
		}
	}

	private void OnEnginePlaybackFailed(object? sender, PlaybackErrorEventArgs e)
	{
		PlaybackFailed?.Invoke(this, e);
		OnPlaybackChanged();
	}

	private void OnPlaybackChanged() => PlaybackChanged?.Invoke(this, EventArgs.Empty);
}
