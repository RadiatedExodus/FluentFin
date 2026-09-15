using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Web;
using DynamicData.Binding;
using FluentFin.Core.Playback;
using FlyleafLib.MediaPlayer;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace FluentFin.MediaPlayers;

public sealed class FlyleafPlaybackEngine : IMediaPlaybackEngine, ISubtitlePlayback, IAudioTrackPlayback, ISubtitleTextPlayback
{
	private static readonly TimeSpan PositionNotificationInterval = TimeSpan.FromMilliseconds(250);

	private readonly Player _player = new();
	private readonly CompositeDisposable _subscriptions = [];
	private readonly ILogger<FlyleafPlaybackEngine> _logger;
	private readonly SemaphoreSlim _operationGate = new(1, 1);
	private bool _disposed;

	public Player Player => _player;
	public string Id => "Flyleaf";
	public string DisplayName => "Flyleaf";
	public PlaybackEngineFeature Features => PlaybackEngineFeature.Video | PlaybackEngineFeature.Audio | PlaybackEngineFeature.Subtitles | PlaybackEngineFeature.ExternalSubtitles | PlaybackEngineFeature.AudioTrackSelection;
	public PlaybackState State => ConvertState(_player.Status);
	public TimeSpan Position => new(_player.CurTime);
	public TimeSpan Duration => new(_player.Duration);
	public bool IsPlaying => _player.IsPlaying;
	public bool IsMuted
	{
		get => _player.Audio.Mute;
		set => _player.Audio.Mute = value;
	}

	public double Volume
	{
		get => _player.Audio.Volume / 100d;
		set => _player.Audio.Volume = (int)Math.Clamp(value * 100, 0, 100);
	}

	public int? SubtitleTrackIndex => _player.Subtitles.Streams.FirstOrDefault(x => x.Enabled)?.StreamIndex - 1;
	public int? AudioTrackIndex => _player.Audio.Streams.FirstOrDefault(x => x.Enabled)?.StreamIndex - 1;
	public string SubtitleText => _player.Subtitles.SubsText ?? "";

	public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;
	public event EventHandler<PositionChangedEventArgs>? PositionChanged;
	public event EventHandler<DurationChangedEventArgs>? DurationChanged;
	public event EventHandler? MediaEnded;
	public event EventHandler? MediaLoaded;
	public event EventHandler<PlaybackErrorEventArgs>? PlaybackFailed;
	public event EventHandler<string>? SubtitleTextChanged;
	public event EventHandler? AudioTracksChanged;

	public FlyleafPlaybackEngine(ILogger<FlyleafPlaybackEngine> logger)
	{
		_logger = logger;
		_player.Config.Player.KeyBindings.RemoveAll();
		_player.WhenAnyValue(x => x.Status)
			.DistinctUntilChanged()
			.Subscribe(OnStatusChanged)
			.DisposeWith(_subscriptions);
		_player.WhenAnyValue(x => x.CurTime)
			.Select(x => new TimeSpan(x))
			.DistinctUntilChanged()
			.Sample(PositionNotificationInterval)
			.Subscribe(position => PositionChanged?.Invoke(this, new PositionChangedEventArgs(position)))
			.DisposeWith(_subscriptions);
		_player.WhenAnyValue(x => x.Duration)
			.Select(x => new TimeSpan(x))
			.DistinctUntilChanged()
			.Subscribe(duration => DurationChanged?.Invoke(this, new DurationChangedEventArgs(duration)))
			.DisposeWith(_subscriptions);
		_player.WhenAnyValue(x => x.Subtitles.SubsText)
			.DistinctUntilChanged()
			.Subscribe(text => SubtitleTextChanged?.Invoke(this, text ?? ""))
			.DisposeWith(_subscriptions);
		_player.Audio.Streams.ToObservableChangeSet()
			.Subscribe(_ => AudioTracksChanged?.Invoke(this, EventArgs.Empty))
			.DisposeWith(_subscriptions);
	}

	public async Task OpenAsync(MediaSource source, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_logger.LogInformation("Flyleaf opening media source. Uri={Uri}, MediaSourceId={MediaSourceId}", source.Uri, source.MediaSourceId);
		var args = await RunPlayerOperationAsync(() => _player.Open(HttpUtility.UrlDecode(source.Uri.ToString())), cancellationToken);
		if (!args.Success)
		{
			throw new InvalidOperationException("Flyleaf could not open the media source.");
		}

		MediaLoaded?.Invoke(this, EventArgs.Empty);
		AudioTracksChanged?.Invoke(this, EventArgs.Empty);
		if (source.DefaultAudioStreamIndex > 0)
		{
			_ = RunPlayerOperationAsync(() => OpenAudioTrackCore(source.DefaultAudioStreamIndex), CancellationToken.None);
		}
	}

	public Task PlayAsync(CancellationToken cancellationToken = default)
	{
		return RunPlayerOperationAsync(_player.Play, cancellationToken);
	}

	public Task PauseAsync(CancellationToken cancellationToken = default)
	{
		return RunPlayerOperationAsync(_player.Pause, cancellationToken);
	}

	public Task StopAsync(CancellationToken cancellationToken = default)
	{
		return RunPlayerOperationOnUiAsync(_player.Stop, cancellationToken);
	}

	public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
	{
		return RunPlayerOperationAsync(() => _player.SeekAccurate((int)position.TotalMilliseconds), cancellationToken);
	}

	public void OpenExternalSubtitleTrack(string url)
	{
		_ = RunPlayerOperationAsync(() =>
		{
			_player.Config.Subtitles.Enabled = true;
			_player.Open(url, forceSubtitles: true);
		}, CancellationToken.None);
	}

	public void OpenInternalSubtitleTrack(int trackIndex, int subtitleIndex)
	{
		_ = RunPlayerOperationAsync(() =>
		{
			_player.Config.Subtitles.Enabled = true;
			if (subtitleIndex >= 0 && subtitleIndex < _player.Subtitles.Streams.Count)
			{
				_player.Open(_player.Subtitles.Streams[subtitleIndex]);
			}
		}, CancellationToken.None);
	}

	public void DisableSubtitles()
	{
		_ = RunPlayerOperationAsync(() => _player.Config.Subtitles.Enabled = false, CancellationToken.None);
	}

	public IEnumerable<AudioTrack> GetAudioTracks() => _player.Audio.Streams.Select(x => new AudioTrack(x.StreamIndex, x.Language.TopEnglishName, x.Title));

	public void OpenAudioTrack(int index)
	{
		_ = RunPlayerOperationAsync(() => OpenAudioTrackCore(index), CancellationToken.None);
	}

	public ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return ValueTask.CompletedTask;
		}

		_disposed = true;
		_subscriptions.Dispose();
		_player.Dispose();
		_operationGate.Dispose();
		return ValueTask.CompletedTask;
	}

	private void OpenAudioTrackCore(int index)
	{
		if (_player.Audio.Streams.FirstOrDefault(x => x.StreamIndex == index) is { } stream)
		{
			_player.Open(stream);
		}
	}

	private async Task RunPlayerOperationAsync(Action operation, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		await _operationGate.WaitAsync(cancellationToken);
		try
		{
			await Task.Run(operation, cancellationToken);
		}
		finally
		{
			_operationGate.Release();
		}
	}

	private async Task<T> RunPlayerOperationAsync<T>(Func<T> operation, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		await _operationGate.WaitAsync(cancellationToken);
		try
		{
			return await Task.Run(operation, cancellationToken);
		}
		finally
		{
			_operationGate.Release();
		}
	}

	private async Task RunPlayerOperationOnUiAsync(Action operation, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		await _operationGate.WaitAsync(cancellationToken);
		try
		{
			var dispatcher = App.MainWindow.DispatcherQueue;
			if (dispatcher.HasThreadAccess)
			{
				operation();
				return;
			}

			var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			if (!dispatcher.TryEnqueue(() =>
			{
				try
				{
					operation();
					completion.TrySetResult();
				}
				catch (Exception ex)
				{
					completion.TrySetException(ex);
				}
			}))
			{
				throw new InvalidOperationException("Flyleaf player operation could not be queued on the UI thread.");
			}

			await completion.Task.WaitAsync(cancellationToken);
		}
		finally
		{
			_operationGate.Release();
		}
	}

	private void OnStatusChanged(Status status)
	{
		var state = ConvertState(status);
		StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(state));
		if (status is Status.Ended)
		{
			MediaEnded?.Invoke(this, EventArgs.Empty);
		}
		else if (status is Status.Failed)
		{
			PlaybackFailed?.Invoke(this, new PlaybackErrorEventArgs(null, "Flyleaf reported a playback failure."));
		}
	}

	private static PlaybackState ConvertState(Status status)
	{
		return status switch
		{
			Status.Opening => PlaybackState.Opening,
			Status.Playing => PlaybackState.Playing,
			Status.Paused => PlaybackState.Paused,
			Status.Stopped => PlaybackState.Stopped,
			Status.Ended => PlaybackState.Ended,
			Status.Failed => PlaybackState.Error,
			_ => PlaybackState.Stopped
		};
	}
}
