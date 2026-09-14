using System.Runtime.InteropServices;
using FluentFin.Core;
using FluentFin.Core.Playback;
using Flurl;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Extensions.Logging;
using WinRT;
using Windows.Media;
using Windows.Storage.Streams;

namespace FluentFin.Services;

public sealed class WindowsMediaSessionService(
	IPlaybackService playbackService,
	IMusicPlaybackController musicPlaybackController,
	ILogger<WindowsMediaSessionService> logger) : IWindowsMediaSessionService
{
	private static readonly TimeSpan TimelineThrottle = TimeSpan.FromSeconds(1);
	private static readonly Guid SystemMediaTransportControlsGuid = Guid.Parse("99FA3FF4-1742-42A6-902E-087D41F965EC");
	private SystemMediaTransportControls? _controls;
	private PlaybackItem? _lastItem;
	private TimeSpan _lastTimelinePosition = TimeSpan.MinValue;
	private PlaybackState? _lastTimelineState;
	private DateTimeOffset _lastTimelineUpdate = DateTimeOffset.MinValue;
	private int _metadataVersion;
	private bool _initialized;
	private bool _disposed;

	public void Initialize()
	{
		if (_initialized || _disposed)
		{
			return;
		}

		_initialized = true;
		try
		{
			_controls = GetForWindow(App.MainWindow.GetWindowHandle());
			_controls.IsEnabled = true;
			_controls.ButtonPressed += OnButtonPressed;
			_controls.PlaybackPositionChangeRequested += OnPlaybackPositionChangeRequested;
			_controls.PlaybackRate = 1;
			DisableUnsupportedControls(_controls);

			playbackService.PlaybackChanged += OnPlaybackChanged;
			playbackService.QueueChanged += OnQueueChanged;
			playbackService.VolumeChanged += OnVolumeChanged;
			logger.LogInformation("Windows media session service initialized");
			UpdateNowPlaying(forceTimeline: true);
		}
		catch (Exception ex)
		{
			_controls = null;
			logger.LogWarning(ex, "Windows media session service could not initialize and will remain disabled");
		}
	}

	public void Clear()
	{
		if (_controls is null)
		{
			return;
		}

		try
		{
			_controls.PlaybackStatus = MediaPlaybackStatus.Closed;
			_controls.IsPlayEnabled = false;
			_controls.IsPauseEnabled = false;
			_controls.IsStopEnabled = false;
			_controls.IsNextEnabled = false;
			_controls.IsPreviousEnabled = false;
			_controls.IsEnabled = false;
			_controls.DisplayUpdater.ClearAll();
			_controls.DisplayUpdater.Update();
			_lastItem = null;
			_lastTimelinePosition = TimeSpan.MinValue;
			_lastTimelineState = null;
			Interlocked.Increment(ref _metadataVersion);
			logger.LogInformation("Windows media session cleared");
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Windows media session clear failed");
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		playbackService.PlaybackChanged -= OnPlaybackChanged;
		playbackService.QueueChanged -= OnQueueChanged;
		playbackService.VolumeChanged -= OnVolumeChanged;

		if (_controls is not null)
		{
			_controls.ButtonPressed -= OnButtonPressed;
			_controls.PlaybackPositionChangeRequested -= OnPlaybackPositionChangeRequested;
			Clear();
		}
	}

	private void OnPlaybackChanged(object? sender, EventArgs e) => QueueUpdate(forceTimeline: false);
	private void OnQueueChanged(object? sender, PlaybackQueueChangedEventArgs e) => QueueUpdate(forceTimeline: true);
	private void OnVolumeChanged(object? sender, EventArgs e) => QueueUpdate(forceTimeline: true);

	private void QueueUpdate(bool forceTimeline)
	{
		if (_disposed || _controls is null)
		{
			return;
		}

		if (!App.MainWindow.DispatcherQueue.TryEnqueue(() => UpdateNowPlaying(forceTimeline)))
		{
			logger.LogWarning("Windows media session update could not be queued to the UI thread");
		}
	}

	private void UpdateNowPlaying(bool forceTimeline)
	{
		if (_controls is null)
		{
			return;
		}

		var item = playbackService.CurrentItem;
		var state = playbackService.State;
		if (item is null || state is PlaybackState.Stopped or PlaybackState.Ended)
		{
			Clear();
			return;
		}

		try
		{
			_controls.IsEnabled = true;
			_controls.PlaybackStatus = ToWindowsStatus(state);
			UpdateEnabledControls(_controls, item.Kind);

			if (_lastItem?.JellyfinId != item.JellyfinId || _lastItem.Kind != item.Kind)
			{
				_lastItem = item;
				UpdateMetadata(item);
			}

			UpdateTimeline(forceTimeline);
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Windows media session update failed. Kind={Kind}, ItemId={ItemId}", item.Kind, item.JellyfinId);
		}
	}

	private void UpdateMetadata(PlaybackItem item)
	{
		if (_controls is null)
		{
			return;
		}

		var version = Interlocked.Increment(ref _metadataVersion);
		var updater = _controls.DisplayUpdater;
		updater.ClearAll();
		updater.AppMediaId = item.JellyfinId.ToString();

		if (item.Kind is PlaybackKind.Music)
		{
			updater.Type = MediaPlaybackType.Music;
			updater.MusicProperties.Title = item.Title;
			updater.MusicProperties.AlbumTitle = item.Item.Album ?? (item.Metadata as MusicPlaybackMetadata)?.Album ?? "";
			updater.MusicProperties.Artist = GetArtists(item);
			updater.MusicProperties.TrackNumber = (uint)Math.Max(0, item.Item.IndexNumber ?? (item.Metadata as MusicPlaybackMetadata)?.TrackNumber ?? 0);
		}
		else
		{
			updater.Type = MediaPlaybackType.Video;
			updater.VideoProperties.Title = item.Title;
			updater.VideoProperties.Subtitle = GetVideoSubtitle(item.Item);
		}

		updater.Update();
		logger.LogInformation("Windows media session metadata updated. Kind={Kind}, ItemId={ItemId}, Title={Title}", item.Kind, item.JellyfinId, item.Title);

		if (TryGetImageUri(item.Item, item.Kind, out var imageUri))
		{
			_ = UpdateArtworkAsync(version, item, imageUri);
		}
	}

	private async Task UpdateArtworkAsync(int version, PlaybackItem item, Uri imageUri)
	{
		try
		{
			await Task.Yield();
			if (_controls is null || _metadataVersion != version)
			{
				return;
			}

			_controls.DisplayUpdater.Thumbnail = RandomAccessStreamReference.CreateFromUri(imageUri);
			_controls.DisplayUpdater.Update();
			logger.LogInformation("Windows media session artwork updated. Kind={Kind}, ItemId={ItemId}", item.Kind, item.JellyfinId);
		}
		catch (Exception ex)
		{
			logger.LogDebug(ex, "Windows media session artwork update failed. Kind={Kind}, ItemId={ItemId}", item.Kind, item.JellyfinId);
		}
	}

	private void UpdateTimeline(bool force)
	{
		if (_controls is null)
		{
			return;
		}

		var now = DateTimeOffset.UtcNow;
		var position = ClampTimeSpan(playbackService.Position);
		var duration = ClampTimeSpan(playbackService.Duration);
		if (duration <= TimeSpan.Zero)
		{
			duration = ClampTimeSpan(playbackService.CurrentItem?.Duration ?? TimeSpan.Zero);
		}

		if (!force &&
			_lastTimelineState == playbackService.State &&
			(position - _lastTimelinePosition).Duration() < TimelineThrottle &&
			now - _lastTimelineUpdate < TimelineThrottle)
		{
			return;
		}

		var end = duration > TimeSpan.Zero ? duration : position;
		_controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
		{
			StartTime = TimeSpan.Zero,
			MinSeekTime = TimeSpan.Zero,
			Position = position > end && end > TimeSpan.Zero ? end : position,
			MaxSeekTime = end,
			EndTime = end
		});

		_controls.PlaybackRate = playbackService.State is PlaybackState.Playing ? 1 : 0;
		_lastTimelinePosition = position;
		_lastTimelineState = playbackService.State;
		_lastTimelineUpdate = now;
	}

	private async void OnButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
	{
		try
		{
			logger.LogInformation("Windows media session button pressed. Button={Button}, Kind={Kind}", args.Button, playbackService.CurrentKind);
			switch (args.Button)
			{
				case SystemMediaTransportControlsButton.Play:
					await playbackService.ResumeAsync();
					break;
				case SystemMediaTransportControlsButton.Pause:
					await playbackService.PauseAsync();
					break;
				case SystemMediaTransportControlsButton.Stop:
					await playbackService.StopAsync();
					break;
				case SystemMediaTransportControlsButton.Next when playbackService.CurrentKind is PlaybackKind.Music:
					await musicPlaybackController.NextAsync();
					break;
				case SystemMediaTransportControlsButton.Previous when playbackService.CurrentKind is PlaybackKind.Music:
					await musicPlaybackController.PreviousAsync();
					break;
			}
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Windows media session command failed. Button={Button}", args.Button);
		}
	}

	private async void OnPlaybackPositionChangeRequested(SystemMediaTransportControls sender, PlaybackPositionChangeRequestedEventArgs args)
	{
		try
		{
			logger.LogInformation("Windows media session seek requested. Position={Position}, Kind={Kind}", args.RequestedPlaybackPosition, playbackService.CurrentKind);
			await playbackService.SeekAsync(args.RequestedPlaybackPosition);
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Windows media session seek command failed. Position={Position}", args.RequestedPlaybackPosition);
		}
	}

	private static void UpdateEnabledControls(SystemMediaTransportControls controls, PlaybackKind kind)
	{
		controls.IsPlayEnabled = true;
		controls.IsPauseEnabled = true;
		controls.IsStopEnabled = true;
		controls.IsNextEnabled = kind is PlaybackKind.Music;
		controls.IsPreviousEnabled = kind is PlaybackKind.Music;
	}

	private static void DisableUnsupportedControls(SystemMediaTransportControls controls)
	{
		controls.IsChannelDownEnabled = false;
		controls.IsChannelUpEnabled = false;
		controls.IsFastForwardEnabled = false;
		controls.IsRecordEnabled = false;
		controls.IsRewindEnabled = false;
	}

	private static MediaPlaybackStatus ToWindowsStatus(PlaybackState state)
	{
		return state switch
		{
			PlaybackState.Opening => MediaPlaybackStatus.Changing,
			PlaybackState.Playing => MediaPlaybackStatus.Playing,
			PlaybackState.Paused => MediaPlaybackStatus.Paused,
			PlaybackState.Error => MediaPlaybackStatus.Closed,
			PlaybackState.Ended => MediaPlaybackStatus.Stopped,
			_ => MediaPlaybackStatus.Stopped
		};
	}

	private static string GetArtists(PlaybackItem item)
	{
		if (item.Item.Artists is { Count: > 0 } artists)
		{
			return string.Join(", ", artists);
		}

		if (item.Metadata is MusicPlaybackMetadata { Artists.Count: > 0 } metadata)
		{
			return string.Join(", ", metadata.Artists);
		}

		return item.Item.AlbumArtist ?? "";
	}

	private static string GetVideoSubtitle(BaseItemDto item)
	{
		if (item.Type is BaseItemDto_Type.Episode)
		{
			var prefix = string.Join(" ", new[]
			{
				item.ParentIndexNumber is { } season ? $"S{season}" : null,
				item.IndexNumber is { } episode ? $"E{episode}" : null
			}.Where(x => !string.IsNullOrWhiteSpace(x)));

			return string.Join(" - ", new[] { item.SeriesName, prefix }.Where(x => !string.IsNullOrWhiteSpace(x)));
		}

		return item.ProductionYear?.ToString() ?? "";
	}

	private static bool TryGetImageUri(BaseItemDto item, PlaybackKind kind, out Uri uri)
	{
		uri = null!;
		if (item.Id is not { } id)
		{
			return false;
		}

		var imageType = kind is PlaybackKind.Video ? ImageType.Backdrop : ImageType.Primary;
		var tag = "";
		if (imageType is ImageType.Backdrop)
		{
			tag = item.BackdropImageTags?.FirstOrDefault()
				?? item.ParentBackdropImageTags?.FirstOrDefault()
				?? "";

			if (string.IsNullOrEmpty(tag))
			{
				imageType = ImageType.Primary;
			}
		}

		if (imageType is ImageType.Primary)
		{
			if (item.Type is BaseItemDto_Type.Episode && item.SeriesId is { } seriesId && !string.IsNullOrEmpty(item.SeriesPrimaryImageTag))
			{
				id = seriesId;
				tag = item.SeriesPrimaryImageTag;
			}
			else if (item.ImageTags?.AdditionalData.TryGetValue(ImageType.Primary.ToString(), out var primaryTag) == true)
			{
				tag = $"{primaryTag}";
			}
		}

		if (string.IsNullOrWhiteSpace(SessionInfo.BaseUrl))
		{
			return false;
		}

		var url = SessionInfo.BaseUrl.AppendPathSegment($"/Items/{id}/Images/{imageType}").SetQueryParam("fillHeight", kind is PlaybackKind.Video ? 480 : 512);
		if (!string.IsNullOrEmpty(tag))
		{
			url.SetQueryParam("tag", tag);
		}

		uri = url.ToUri();
		return true;
	}

	private static TimeSpan ClampTimeSpan(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;

	private static SystemMediaTransportControls GetForWindow(IntPtr windowHandle)
	{
		var runtimeClassId = IntPtr.Zero;
		var factoryPtr = IntPtr.Zero;
		try
		{
			ThrowIfFailed(WindowsCreateString("Windows.Media.SystemMediaTransportControls", "Windows.Media.SystemMediaTransportControls".Length, out runtimeClassId));
			var interopGuid = typeof(ISystemMediaTransportControlsInterop).GUID;
			ThrowIfFailed(RoGetActivationFactory(runtimeClassId, ref interopGuid, out factoryPtr));

			var controlsGuid = SystemMediaTransportControlsGuid;
			ThrowIfFailed(ISystemMediaTransportControlsInterop.GetForWindow(factoryPtr, windowHandle, ref controlsGuid, out var controlsPtr));
			return ComWrappersSupport.CreateRcwForComObject<SystemMediaTransportControls>(controlsPtr);
		}
		finally
		{
			if (factoryPtr != IntPtr.Zero)
			{
				Marshal.Release(factoryPtr);
			}

			if (runtimeClassId != IntPtr.Zero)
			{
				WindowsDeleteString(runtimeClassId);
			}
		}
	}

	private static void ThrowIfFailed(int hresult)
	{
		if (hresult < 0)
		{
			Marshal.ThrowExceptionForHR(hresult);
		}
	}

	[DllImport("api-ms-win-core-winrt-l1-1-0.dll", ExactSpelling = true)]
	private static extern int RoGetActivationFactory(IntPtr activatableClassId, [In] ref Guid iid, out IntPtr factory);

	[DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
	private static extern int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

	[DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", ExactSpelling = true)]
	private static extern int WindowsDeleteString(IntPtr hstring);

	[Guid("ddb0472d-c911-4a1f-86d9-dc3d71a95f5a")]
	private unsafe struct ISystemMediaTransportControlsInterop
	{
		public static int GetForWindow(IntPtr interop, IntPtr appWindow, ref Guid riid, out IntPtr mediaTransportControl)
		{
			fixed (Guid* riidPtr = &riid)
			fixed (IntPtr* resultPtr = &mediaTransportControl)
			{
				var vtable = *(void***)interop;
				var getForWindow = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)vtable[6];
				return getForWindow(interop, appWindow, riidPtr, resultPtr);
			}
		}
	}
}
