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
	private readonly Player _player = new();
	private readonly CompositeDisposable _subscriptions = [];
	private readonly ILogger<FlyleafPlaybackEngine> _logger;
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

	public FlyleafPlaybackEngine(ILogger<FlyleafPlaybackEngine> logger)
	{
		_logger = logger;
		_player.Config.Player.KeyBindings.RemoveAll();
		_player.WhenAnyValue(x => x.Status)
			.Subscribe(OnStatusChanged)
			.DisposeWith(_subscriptions);
		_player.WhenAnyValue(x => x.CurTime)
			.Select(x => new TimeSpan(x))
			.Subscribe(position => PositionChanged?.Invoke(this, new PositionChangedEventArgs(position)))
			.DisposeWith(_subscriptions);
		_player.WhenAnyValue(x => x.Duration)
			.Select(x => new TimeSpan(x))
			.Subscribe(duration => DurationChanged?.Invoke(this, new DurationChangedEventArgs(duration)))
			.DisposeWith(_subscriptions);
		_player.WhenAnyValue(x => x.Subtitles.SubsText)
			.Subscribe(text => SubtitleTextChanged?.Invoke(this, text ?? ""))
			.DisposeWith(_subscriptions);
	}

	public Task OpenAsync(MediaSource source, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_logger.LogInformation("Flyleaf opening media source. Uri={Uri}, MediaSourceId={MediaSourceId}", source.Uri, source.MediaSourceId);
		var args = _player.Open(HttpUtility.UrlDecode(source.Uri.ToString()));
		if (!args.Success)
		{
			throw new InvalidOperationException("Flyleaf could not open the media source.");
		}

		MediaLoaded?.Invoke(this, EventArgs.Empty);
		if (source.DefaultAudioStreamIndex > 0)
		{
			OpenAudioTrack(source.DefaultAudioStreamIndex);
		}

		return Task.CompletedTask;
	}

	public Task PlayAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_player.Play();
		return Task.CompletedTask;
	}

	public Task PauseAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_player.Pause();
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_player.Stop();
		return Task.CompletedTask;
	}

	public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_player.SeekAccurate((int)position.TotalMilliseconds);
		return Task.CompletedTask;
	}

	public void OpenExternalSubtitleTrack(string url)
	{
		_player.Config.Subtitles.Enabled = true;
		_player.Open(url, forceSubtitles: true);
	}

	public void OpenInternalSubtitleTrack(int trackIndex, int subtitleIndex)
	{
		_player.Config.Subtitles.Enabled = true;
		if (subtitleIndex >= 0 && subtitleIndex < _player.Subtitles.Streams.Count)
		{
			_player.Open(_player.Subtitles.Streams[subtitleIndex]);
		}
	}

	public void DisableSubtitles() => _player.Config.Subtitles.Enabled = false;

	public IEnumerable<AudioTrack> GetAudioTracks() => _player.Audio.Streams.Select(x => new AudioTrack(x.StreamIndex, x.Language.TopEnglishName, x.Title));

	public void OpenAudioTrack(int index)
	{
		if (_player.Audio.Streams.FirstOrDefault(x => x.StreamIndex == index) is { } stream)
		{
			_player.Open(stream);
		}
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
		return ValueTask.CompletedTask;
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
