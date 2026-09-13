using FluentFin.Core.Contracts.Services;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Extensions.Logging;

namespace FluentFin.Core.Playback;

public sealed class VideoPlaybackController(
	IJellyfinClient jellyfinClient,
	ILogger<VideoPlaybackController> logger) : IPlaybackController
{
	private IMediaPlaybackEngine? _engine;
	private PlaybackRequest? _request;
	private MediaResponse? _mediaResponse;
	private MediaSource? _currentSource;
	private PlaybackProgressInfo_PlayMethod _playMethod;

	public PlaybackKind Kind => PlaybackKind.Video;
	public PlaybackState State => _engine?.State ?? PlaybackState.Stopped;
	public TimeSpan Position => _engine?.Position ?? TimeSpan.Zero;
	public TimeSpan Duration => _engine?.Duration ?? TimeSpan.Zero;
	public MediaSource? CurrentSource => _currentSource;

	public async Task PrepareAsync(PlaybackRequest request, IMediaPlaybackEngine engine, CancellationToken cancellationToken = default)
	{
		logger.LogInformation("VideoPlaybackController preparing item. ItemId={ItemId}, Title={Title}", request.StartItem.JellyfinId, request.StartItem.Title);
		_request = request;
		_engine = engine;

		_mediaResponse = await jellyfinClient.GetMediaUrl(request.StartItem.Item, cancellationToken);
		if (_mediaResponse is null)
		{
			throw new InvalidOperationException($"No playable video source was returned for {request.StartItem.JellyfinId}.");
		}

		_playMethod = _mediaResponse.PlayMethod;
		_currentSource = new MediaSource(
			_mediaResponse.Uri,
			_mediaResponse.PlaybackSessionId,
			_mediaResponse.MediaSourceId,
			_mediaResponse.PlayMethod,
			_mediaResponse.MediaSourceInfo,
			_mediaResponse.MediaSourceInfo.DefaultAudioStreamIndex ?? 0);
		await _engine.OpenAsync(_currentSource, cancellationToken);
	}

	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		if (_engine is null || _request is null)
		{
			return;
		}

		logger.LogInformation("VideoPlaybackController starting item. ItemId={ItemId}", _request.StartItem.JellyfinId);
		await _engine.PlayAsync(cancellationToken);

		if (_request.StartPosition is { } startPosition && startPosition > TimeSpan.Zero)
		{
			await _engine.SeekAsync(startPosition, cancellationToken);
		}

		await jellyfinClient.Playing(_request.StartItem.Item);
	}

	public Task PauseAsync(CancellationToken cancellationToken = default) => _engine?.PauseAsync(cancellationToken) ?? Task.CompletedTask;

	public Task ResumeAsync(CancellationToken cancellationToken = default) => _engine?.PlayAsync(cancellationToken) ?? Task.CompletedTask;

	public async Task StopAsync(CancellationToken cancellationToken = default)
	{
		logger.LogInformation("VideoPlaybackController stopping item. ItemId={ItemId}", _request?.StartItem.JellyfinId);
		if (_engine is not null)
		{
			await _engine.StopAsync(cancellationToken);
		}

		await jellyfinClient.Stop();
		_currentSource = null;
		_mediaResponse = null;
		_request = null;
	}

	public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) => _engine?.SeekAsync(position, cancellationToken) ?? Task.CompletedTask;

	public async Task ReportProgressAsync(CancellationToken cancellationToken = default)
	{
		if (_engine is null || _request is null || _mediaResponse is null || _engine.Position.Ticks == 0)
		{
			return;
		}

		await jellyfinClient.Progress(new PlaybackProgressInfo
		{
			ItemId = _request.StartItem.JellyfinId,
			PositionTicks = _engine.Position.Ticks,
			IsPaused = !_engine.IsPlaying,
			IsMuted = _engine.IsMuted,
			PlayMethod = _playMethod,
			PlaybackStartTimeTicks = TimeProvider.System.GetTimestamp(),
			SessionId = _mediaResponse.PlaybackSessionId,
			MediaSourceId = _mediaResponse.MediaSourceId
		});
	}
}
