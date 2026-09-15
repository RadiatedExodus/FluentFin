using FluentFin.Core.Contracts.Services;
using FluentFin.Core.Playback;
using FluentFin.Mpv;
using FluentFin.Playback;
using Microsoft.Extensions.Logging;

namespace FluentFin.MediaPlayers;

public sealed class MpvPlaybackEngine(
	MpvTrackMapper trackMapper,
	ILogger<MpvPlaybackEngine> logger) : IMediaPlaybackEngine, ISubtitlePlayback, IAudioTrackPlayback, IPlaybackSpeedControl, IVideoOutputEngine
{
	private readonly SemaphoreSlim _gate = new(1, 1);
	private MpvClient? _mpv;
	private PlaybackState _state = PlaybackState.Stopped;
	private TimeSpan _position;
	private TimeSpan _duration;
	private int? _audioTrackIndex;
	private int? _subtitleTrackIndex;
	private int? _pendingDefaultAudioTrackIndex;
	private TimeSpan? _pendingSeekAfterLoad;
	private bool _isMediaLoaded;
	private uint _videoOutputWidth;
	private uint _videoOutputHeight;
	private int _swapChainPollVersion;
	private bool _disposed;

	public string Id => "Mpv";
	public string DisplayName => "mpv";
	public PlaybackEngineFeature Features => PlaybackEngineFeature.Video
		| PlaybackEngineFeature.Audio
		| PlaybackEngineFeature.Subtitles
		| PlaybackEngineFeature.ExternalSubtitles
		| PlaybackEngineFeature.AudioTrackSelection
		| PlaybackEngineFeature.PlaybackSpeed
		| PlaybackEngineFeature.HardwareDecoding;
	public PlaybackState State => _state;
	public TimeSpan Position => _position;
	public TimeSpan Duration => _duration;
	public bool IsPlaying => _state is PlaybackState.Playing;
	public bool IsMuted
	{
		get => _mpv?.Muted ?? false;
		set
		{
			if (_mpv is not null)
			{
				_mpv.Muted = value;
			}
		}
	}
	public int? SubtitleTrackIndex => _subtitleTrackIndex;
	public int? AudioTrackIndex => _audioTrackIndex;
	public nint VideoSwapChain { get; private set; }

	public double Volume
	{
		get => (_mpv?.Volume ?? 100) / 100d;
		set
		{
			if (_mpv is not null)
			{
				_mpv.Volume = Math.Clamp(value, 0, 1) * 100;
			}
		}
	}

	public double PlaybackSpeed
	{
		get => _mpv?.GetPropertyDouble("speed") ?? 1;
		set
		{
			if (_mpv is not null)
			{
				_mpv.SetPropertyDouble("speed", Math.Clamp(value, 0.25, 4));
			}
		}
	}

	public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;
	public event EventHandler<PositionChangedEventArgs>? PositionChanged;
	public event EventHandler<DurationChangedEventArgs>? DurationChanged;
	public event EventHandler? MediaEnded;
	public event EventHandler? MediaLoaded;
	public event EventHandler<PlaybackErrorEventArgs>? PlaybackFailed;
	public event EventHandler? VideoOutputChanged;
	public event EventHandler? AudioTracksChanged;

	public async Task OpenAsync(MediaSource source, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		await _gate.WaitAsync(cancellationToken);
		try
		{
			ThrowIfDisposed();
			var mpv = await EnsureMpvAsync(cancellationToken);
			SetState(PlaybackState.Opening);
			_position = TimeSpan.Zero;
			_duration = TimeSpan.Zero;
			_isMediaLoaded = false;
			_pendingSeekAfterLoad = null;
			_audioTrackIndex = source.DefaultAudioStreamIndex;
			_pendingDefaultAudioTrackIndex = source.DefaultAudioStreamIndex;
			_subtitleTrackIndex = null;

			logger.LogInformation("mpv opening media source. Uri={Uri}, MediaSourceId={MediaSourceId}, PlayMethod={PlayMethod}",
				source.Uri,
				source.MediaSourceId,
				source.PlayMethod);
			await mpv.LoadAsync(source.Uri.ToString(), cancellationToken);
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task PlayAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var mpv = await EnsureMpvAsync(cancellationToken);
		logger.LogInformation("mpv play requested");
		mpv.Paused = false;
		SetState(PlaybackState.Playing);
	}

	public async Task PauseAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var mpv = await EnsureMpvAsync(cancellationToken);
		logger.LogInformation("mpv pause requested");
		mpv.Paused = true;
		SetState(PlaybackState.Paused);
	}

	public async Task StopAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (_mpv is null)
		{
			SetState(PlaybackState.Stopped);
			return;
		}

		logger.LogInformation("mpv stop requested");
		await _mpv.StopAsync(cancellationToken);
		_position = TimeSpan.Zero;
		SetState(PlaybackState.Stopped);
	}

	public async Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var mpv = await EnsureMpvAsync(cancellationToken);
		logger.LogInformation("mpv seek requested. Position={Position}", position);
		if (!_isMediaLoaded)
		{
			_pendingSeekAfterLoad = position;
			_position = position;
			PositionChanged?.Invoke(this, new PositionChangedEventArgs(_position));
			logger.LogInformation("mpv seek deferred until media is loaded. Position={Position}", position);
			return;
		}

		await mpv.SeekAsync(position, cancellationToken);
		_position = position;
		PositionChanged?.Invoke(this, new PositionChangedEventArgs(_position));
	}

	public void OpenExternalSubtitleTrack(string url)
	{
		if (_mpv is null)
		{
			return;
		}

		logger.LogInformation("mpv external subtitle requested. Url={Url}", url);
		_ = _mpv.CommandAsync("sub-add", url, "select");
		_subtitleTrackIndex = null;
	}

	public void OpenInternalSubtitleTrack(int trackIndex, int subtitleIndex)
	{
		if (_mpv is null)
		{
			return;
		}

		var mpvTrackId = trackMapper.FindMpvTrackId(_mpv.GetTracks(), Jellyfin.Sdk.Generated.Models.MediaStream_Type.Subtitle, trackIndex);
		if (mpvTrackId is null)
		{
			return;
		}

		logger.LogInformation("mpv internal subtitle requested. JellyfinTrackIndex={JellyfinTrackIndex}, MpvTrackId={MpvTrackId}", trackIndex, mpvTrackId);
		_mpv.SetPropertyInt64("sid", mpvTrackId.Value);
		_subtitleTrackIndex = trackIndex;
	}

	public void DisableSubtitles()
	{
		if (_mpv is null)
		{
			return;
		}

		logger.LogInformation("mpv subtitles disabled");
		_mpv.SetPropertyString("sid", "no");
		_subtitleTrackIndex = null;
	}

	public IEnumerable<AudioTrack> GetAudioTracks() => _mpv is null ? [] : trackMapper.ToAudioTracks(_mpv.GetTracks());

	public void OpenAudioTrack(int index)
	{
		if (_mpv is null)
		{
			return;
		}

		var mpvTrackId = trackMapper.FindMpvTrackId(_mpv.GetTracks(), Jellyfin.Sdk.Generated.Models.MediaStream_Type.Audio, index);
		if (mpvTrackId is null)
		{
			return;
		}

		logger.LogInformation("mpv audio track requested. JellyfinTrackIndex={JellyfinTrackIndex}, MpvTrackId={MpvTrackId}", index, mpvTrackId);
		_mpv.SetPropertyInt64("aid", mpvTrackId.Value);
		_audioTrackIndex = index;
	}

	public async Task SetVideoOutputSizeAsync(uint width, uint height, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (_mpv is null || width == 0 || height == 0)
		{
			_videoOutputWidth = width;
			_videoOutputHeight = height;
			return;
		}

		_videoOutputWidth = width;
		_videoOutputHeight = height;
		var size = $"{width}x{height}";
		logger.LogDebug("mpv video output size changed. Size={Size}", size);
		_mpv.SetPropertyString("d3d11-composition-size", size);
		await Task.CompletedTask;
	}

	public async ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		logger.LogInformation("mpv playback engine disposing");
		VideoSwapChain = nint.Zero;
		VideoOutputChanged?.Invoke(this, EventArgs.Empty);
		if (_mpv is not null)
		{
			await _mpv.DisposeAsync();
			_mpv = null;
		}

		_gate.Dispose();
	}

	private async Task<MpvClient> EnsureMpvAsync(CancellationToken cancellationToken)
	{
		ThrowIfDisposed();
		if (_mpv is not null)
		{
			return _mpv;
		}

		logger.LogInformation("Initializing mpv playback engine");
		var client = new MpvClient();
		client.FileLoaded += OnMpvFileLoaded;
		client.EndFile += OnMpvEndFile;
		client.PropertyChanged += OnMpvPropertyChanged;
		client.VideoReconfigured += OnMpvVideoReconfigured;
		client.PlaybackRestarted += OnMpvPlaybackRestarted;
		await client.InitializeAsync(cancellationToken);
		logger.LogInformation("mpv initialized. Version={Version}", client.GetPropertyString("mpv-version"));
		_mpv = client;
		ApplyKnownVideoOutputSize();
		return _mpv;
	}

	private void OnMpvFileLoaded(object? sender, EventArgs e)
	{
		_isMediaLoaded = true;
		logger.LogInformation("mpv media loaded. Duration={Duration}", _duration);
		if (_pendingDefaultAudioTrackIndex is { } defaultAudioTrackIndex)
		{
			_pendingDefaultAudioTrackIndex = null;
			OpenAudioTrack(defaultAudioTrackIndex);
		}

		if (_pendingSeekAfterLoad is { } pendingSeek)
		{
			_pendingSeekAfterLoad = null;
			_ = ApplyDeferredSeekAsync(pendingSeek);
		}

		RefreshVideoSwapChain("file-loaded");
		StartSwapChainPolling();
		MediaLoaded?.Invoke(this, EventArgs.Empty);
	}

	private void OnMpvEndFile(object? sender, MpvEndFileEventArgs e)
	{
		logger.LogInformation("mpv media ended. Reason={Reason}, ErrorCode={ErrorCode}", e.Reason, e.ErrorCode);
		switch (e.Reason)
		{
			case MpvEndFileReason.Eof:
				SetState(PlaybackState.Ended);
				MediaEnded?.Invoke(this, EventArgs.Empty);
				break;

			case MpvEndFileReason.Error:
				SetState(PlaybackState.Error);
				PlaybackFailed?.Invoke(this, new PlaybackErrorEventArgs(null, $"mpv ended with error code {e.ErrorCode}."));
				break;

			case MpvEndFileReason.Stop:
			case MpvEndFileReason.Quit:
				SetState(PlaybackState.Stopped);
				break;

			case MpvEndFileReason.Redirect:
				logger.LogDebug("mpv end-file redirect ignored for playback completion");
				break;

			default:
				logger.LogWarning("mpv ended with unknown end-file reason. Reason={Reason}, ErrorCode={ErrorCode}", e.Reason, e.ErrorCode);
				SetState(PlaybackState.Stopped);
				break;
		}
	}

	private void OnMpvPropertyChanged(object? sender, MpvPropertyChangedEventArgs e)
	{
		switch (e.Name)
		{
			case "pause" when e.Value is bool paused:
				SetState(paused ? PlaybackState.Paused : PlaybackState.Playing);
				break;
			case "time-pos" when e.Value is double seconds:
				_position = TimeSpan.FromSeconds(Math.Max(0, seconds));
				PositionChanged?.Invoke(this, new PositionChangedEventArgs(_position));
				break;
			case "duration" when e.Value is double seconds:
				_duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
				DurationChanged?.Invoke(this, new DurationChangedEventArgs(_duration));
				break;
			case "display-swapchain" when e.Value is long swapChain:
				VideoSwapChain = (nint)swapChain;
				logger.LogInformation("mpv video output changed. HasSwapChain={HasSwapChain}", VideoSwapChain != nint.Zero);
				VideoOutputChanged?.Invoke(this, EventArgs.Empty);
				break;
			case "track-list":
				logger.LogDebug("mpv track list changed. TrackCount={TrackCount}", _mpv?.GetTracks().Count ?? 0);
				AudioTracksChanged?.Invoke(this, EventArgs.Empty);
				break;
		}
	}

	private void OnMpvVideoReconfigured(object? sender, EventArgs e)
	{
		logger.LogInformation("mpv video reconfigured");
		ApplyKnownVideoOutputSize();
		RefreshVideoSwapChain("video-reconfig");
		StartSwapChainPolling();
	}

	private void OnMpvPlaybackRestarted(object? sender, EventArgs e)
	{
		logger.LogDebug("mpv playback restarted");
		RefreshVideoSwapChain("playback-restart");
	}

	private void SetState(PlaybackState state)
	{
		if (_state == state)
		{
			return;
		}

		_state = state;
		logger.LogDebug("mpv playback state changed. State={State}", state);
		StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(state));
	}

	private void ApplyKnownVideoOutputSize()
	{
		if (_mpv is null || _videoOutputWidth == 0 || _videoOutputHeight == 0)
		{
			return;
		}

		var size = $"{_videoOutputWidth}x{_videoOutputHeight}";
		logger.LogInformation("mpv applying known video output size. Size={Size}", size);
		_mpv.SetPropertyString("d3d11-composition-size", size);
	}

	private void RefreshVideoSwapChain(string reason)
	{
		if (_mpv is null)
		{
			return;
		}

		var swapChain = _mpv.DisplaySwapChain;
		logger.LogInformation("mpv display swapchain refresh. Reason={Reason}, HasSwapChain={HasSwapChain}", reason, swapChain != nint.Zero);
		if (swapChain == VideoSwapChain)
		{
			return;
		}

		VideoSwapChain = swapChain;
		VideoOutputChanged?.Invoke(this, EventArgs.Empty);
	}

	private void StartSwapChainPolling()
	{
		var version = Interlocked.Increment(ref _swapChainPollVersion);
		_ = Task.Run(async () =>
		{
			for (var attempt = 0; attempt < 20; attempt++)
			{
				if (_disposed || version != _swapChainPollVersion || VideoSwapChain != nint.Zero)
				{
					return;
				}

				await Task.Delay(250);
				RefreshVideoSwapChain($"poll-{attempt + 1}");
			}
		});
	}

	private async Task ApplyDeferredSeekAsync(TimeSpan position)
	{
		try
		{
			if (_mpv is null)
			{
				return;
			}

			logger.LogInformation("mpv deferred seek requested. Position={Position}", position);
			await _mpv.SeekAsync(position);
			_position = position;
			PositionChanged?.Invoke(this, new PositionChangedEventArgs(_position));
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "mpv deferred seek failed. Position={Position}", position);
		}
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}
