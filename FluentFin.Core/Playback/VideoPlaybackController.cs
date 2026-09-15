using System.Web;
using FluentFin.Core.Contracts.Services;
using Flurl;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Extensions.Logging;

namespace FluentFin.Core.Playback;

public sealed class VideoPlaybackController(
	IJellyfinClient jellyfinClient,
	ILogger<VideoPlaybackController> logger) : IVideoPlaybackController
{
	private IMediaPlaybackEngine? _engine;
	private ISubtitleTextPlayback? _subtitleTextPlayback;
	private PlaybackRequest? _request;
	private MediaResponse? _mediaResponse;
	private MediaSource? _currentSource;
	private PlaybackProgressInfo_PlayMethod _playMethod;
	private IReadOnlyList<AudioTrack> _audioTracks = [];
	private IReadOnlyList<SubtitleTrack> _subtitleTracks = [];
	private int? _selectedSubtitleTrackIndex;
	private string _subtitleText = "";

	public event EventHandler? TracksChanged;
	public event EventHandler<string>? SubtitleTextChanged;

	public PlaybackKind Kind => PlaybackKind.Video;
	public PlaybackState State => _engine?.State ?? PlaybackState.Stopped;
	public TimeSpan Position => _engine?.Position ?? TimeSpan.Zero;
	public TimeSpan Duration => _engine?.Duration ?? TimeSpan.Zero;
	public MediaSource? CurrentSource => _currentSource;
	public IReadOnlyList<AudioTrack> AudioTracks => _audioTracks;
	public IReadOnlyList<SubtitleTrack> SubtitleTracks => _subtitleTracks;
	public int? AudioTrackIndex => (_engine as IAudioTrackPlayback)?.AudioTrackIndex;
	public int? SubtitleTrackIndex => _selectedSubtitleTrackIndex ?? (_engine as ISubtitlePlayback)?.SubtitleTrackIndex;
	public string SubtitleText => _subtitleText;

	public async Task PrepareAsync(PlaybackRequest request, IMediaPlaybackEngine engine, CancellationToken cancellationToken = default)
	{
		DetachEngine();
		logger.LogInformation("VideoPlaybackController preparing item. ItemId={ItemId}, Title={Title}", request.StartItem.JellyfinId, request.StartItem.Title);
		var playbackItem = request.StartItem;
		if (playbackItem.Item.Id != playbackItem.JellyfinId || playbackItem.Item.MediaSources is null)
		{
			var hydrated = await jellyfinClient.GetItem(playbackItem.JellyfinId);
			if (hydrated is not null)
			{
				playbackItem = PlaybackItem.FromDto(hydrated, PlaybackKind.Video);
				request = new PlaybackRequest
				{
					Kind = request.Kind,
					StartItem = playbackItem,
					QueueItems = request.QueueItems,
					StartIndex = request.StartIndex,
					StartPosition = request.StartPosition
				};
				logger.LogInformation("VideoPlaybackController hydrated playback item. ItemId={ItemId}", playbackItem.JellyfinId);
			}
		}

		_request = request;
		_engine = engine;
		AttachEngine(engine);

		_mediaResponse = await jellyfinClient.GetMediaUrl(playbackItem.Item, cancellationToken);
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

		BuildSubtitleTracks();
		await _engine.OpenAsync(_currentSource, cancellationToken);
		RefreshAudioTracks();
	}

	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		if (_engine is null || _request is not { } request)
		{
			return;
		}

		logger.LogInformation("VideoPlaybackController starting item. ItemId={ItemId}", request.StartItem.JellyfinId);
		await _engine.PlayAsync(cancellationToken);

		if (!ReferenceEquals(_request, request))
		{
			logger.LogInformation("VideoPlaybackController start abandoned because playback was stopped or replaced. ItemId={ItemId}", request.StartItem.JellyfinId);
			return;
		}

		if (request.StartPosition is { } startPosition && startPosition > TimeSpan.Zero)
		{
			await _engine.SeekAsync(startPosition, cancellationToken);
		}

		SelectDefaultSubtitle();
		await jellyfinClient.Playing(request.StartItem.Item);
	}

	public async Task PauseAsync(CancellationToken cancellationToken = default)
	{
		if (_engine is not null)
		{
			await _engine.PauseAsync(cancellationToken);
		}
	}

	public async Task ResumeAsync(CancellationToken cancellationToken = default)
	{
		if (_engine is not null)
		{
			await _engine.PlayAsync(cancellationToken);
		}
	}

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
		_audioTracks = [];
		_subtitleTracks = [];
		_selectedSubtitleTrackIndex = null;
		_subtitleText = "";
		DetachEngine();
		TracksChanged?.Invoke(this, EventArgs.Empty);
		SubtitleTextChanged?.Invoke(this, _subtitleText);
	}

	public async Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
	{
		if (_engine is not null)
		{
			await _engine.SeekAsync(position, cancellationToken);
		}
	}

	public async Task SelectAudioTrackAsync(int index, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (_engine is not IAudioTrackPlayback audio)
		{
			return;
		}

		logger.LogInformation("VideoPlaybackController audio track requested. Index={Index}, ItemId={ItemId}", index, _request?.StartItem.JellyfinId);
		audio.OpenAudioTrack(index);
		await ReportProgressAsync(cancellationToken);
		TracksChanged?.Invoke(this, EventArgs.Empty);
	}

	public async Task SelectSubtitleTrackAsync(int index, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (_engine is not ISubtitlePlayback subtitles || _mediaResponse?.MediaSourceInfo.MediaStreams is not { } streams)
		{
			logger.LogWarning("VideoPlaybackController subtitle track request ignored because subtitle playback is unavailable. Index={Index}, ItemId={ItemId}, HasEngine={HasEngine}, HasMediaStreams={HasMediaStreams}",
				index, _request?.StartItem.JellyfinId, _engine is not null, _mediaResponse?.MediaSourceInfo.MediaStreams is not null);
			return;
		}

		var stream = streams.FirstOrDefault(x => x.Type is MediaStream_Type.Subtitle && x.Index == index);
		if (stream is null)
		{
			logger.LogWarning("VideoPlaybackController subtitle track request could not find matching stream. Index={Index}, ItemId={ItemId}, SubtitleStreamIndexes={SubtitleStreamIndexes}",
				index,
				_request?.StartItem.JellyfinId,
				string.Join(",", streams.Where(x => x.Type is MediaStream_Type.Subtitle).Select(x => x.Index?.ToString() ?? "<null>")));
			return;
		}

		logger.LogInformation("VideoPlaybackController subtitle track requested. Index={Index}, IsExternal={IsExternal}, ItemId={ItemId}",
			index, stream.IsExternal, _request?.StartItem.JellyfinId);

		if (stream.IsExternal == true)
		{
			if (string.IsNullOrWhiteSpace(stream.DeliveryUrl))
			{
				logger.LogWarning("VideoPlaybackController external subtitle has no delivery URL. Index={Index}, ItemId={ItemId}",
					index, _request?.StartItem.JellyfinId);
				return;
			}

			var url = HttpUtility.UrlDecode(jellyfinClient.BaseUrl.AppendPathSegment(stream.DeliveryUrl).ToString());
			subtitles.OpenExternalSubtitleTrack(url);
		}
		else
		{
			var internalSubtitles = streams.Where(x => x.Type is MediaStream_Type.Subtitle && x.IsExternal is false).ToList();
			var subtitleIndex = internalSubtitles.IndexOf(stream);
			if (subtitleIndex >= 0 && stream.Index is { } trackIndex)
			{
				subtitles.OpenInternalSubtitleTrack(trackIndex, subtitleIndex);
			}
			else
			{
				logger.LogWarning("VideoPlaybackController internal subtitle index could not be resolved. Index={Index}, ItemId={ItemId}",
					index, _request?.StartItem.JellyfinId);
				return;
			}
		}

		_selectedSubtitleTrackIndex = index;
		await ReportProgressAsync(cancellationToken);
		TracksChanged?.Invoke(this, EventArgs.Empty);
	}

	public async Task DisableSubtitlesAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (_engine is ISubtitlePlayback subtitles)
		{
			logger.LogInformation("VideoPlaybackController subtitles disabled. ItemId={ItemId}", _request?.StartItem.JellyfinId);
			subtitles.DisableSubtitles();
			_selectedSubtitleTrackIndex = null;
			await ReportProgressAsync(cancellationToken);
			TracksChanged?.Invoke(this, EventArgs.Empty);
		}
	}

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
			MediaSourceId = _mediaResponse.MediaSourceId,
			AudioStreamIndex = AudioTrackIndex,
			SubtitleStreamIndex = SubtitleTrackIndex
		});
	}

	private void AttachEngine(IMediaPlaybackEngine engine)
	{
		engine.MediaLoaded += OnEngineMediaLoaded;
		if (engine is IAudioTrackPlayback audioTrackPlayback)
		{
			audioTrackPlayback.AudioTracksChanged += OnAudioTracksChanged;
		}

		if (engine is ISubtitleTextPlayback subtitleText)
		{
			_subtitleTextPlayback = subtitleText;
			_subtitleText = subtitleText.SubtitleText;
			subtitleText.SubtitleTextChanged += OnSubtitleTextChanged;
		}
	}

	private void DetachEngine()
	{
		if (_engine is not null)
		{
			_engine.MediaLoaded -= OnEngineMediaLoaded;
			if (_engine is IAudioTrackPlayback audioTrackPlayback)
			{
				audioTrackPlayback.AudioTracksChanged -= OnAudioTracksChanged;
			}
		}

		if (_subtitleTextPlayback is not null)
		{
			_subtitleTextPlayback.SubtitleTextChanged -= OnSubtitleTextChanged;
			_subtitleTextPlayback = null;
		}

		_engine = null;
	}

	private void OnEngineMediaLoaded(object? sender, EventArgs e)
	{
		RefreshAudioTracks();
		SelectDefaultSubtitle();
	}

	private void OnAudioTracksChanged(object? sender, EventArgs e) => RefreshAudioTracks();

	private void OnSubtitleTextChanged(object? sender, string text)
	{
		_subtitleText = text;
		SubtitleTextChanged?.Invoke(this, text);
	}

	private void RefreshAudioTracks()
	{
		_audioTracks = (_engine as IAudioTrackPlayback)?.GetAudioTracks().ToList() ?? [];
		TracksChanged?.Invoke(this, EventArgs.Empty);
	}

	private void BuildSubtitleTracks()
	{
		_subtitleTracks = _mediaResponse?.MediaSourceInfo.MediaStreams?
			.Where(x => x.Type is MediaStream_Type.Subtitle && x.Index is not null)
			.Select(x => new SubtitleTrack(
				x.Index!.Value,
				x.Language,
				string.IsNullOrWhiteSpace(x.DisplayTitle) ? x.Title : x.DisplayTitle,
				x.IsExternal == true))
			.ToList() ?? [];
		TracksChanged?.Invoke(this, EventArgs.Empty);
	}

	private void SelectDefaultSubtitle()
	{
		if (_mediaResponse?.MediaSourceInfo.DefaultSubtitleStreamIndex is not { } subtitleIndex)
		{
			return;
		}

		_ = SelectSubtitleTrackAsync(subtitleIndex);
	}
}
