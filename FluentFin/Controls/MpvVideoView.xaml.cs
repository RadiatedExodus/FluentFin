using System.Runtime.InteropServices;
using FluentFin.MediaPlayers;
using FluentFin.Playback;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentFin.Controls;

public sealed partial class MpvVideoView : UserControl
{
	private readonly MpvPlaybackEngine _engine;
	private readonly ILogger<MpvVideoView> _logger;
	private nint _attachedSwapChain;

	public MpvVideoView()
	{
		InitializeComponent();
		_engine = App.GetService<MpvPlaybackEngine>();
		_logger = App.GetService<ILogger<MpvVideoView>>();
		Loaded += OnLoaded;
	}

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		_engine.VideoOutputChanged -= OnVideoOutputChanged;
		_engine.VideoOutputChanged += OnVideoOutputChanged;

		if (_engine.VideoSwapChain != nint.Zero)
		{
			AttachCurrentSwapChain();
		}

		_ = UpdateVideoOutputSizeAsync();
	}

	private void OnVideoOutputChanged(object? sender, EventArgs e)
	{
		DispatcherQueue.TryEnqueue(AttachCurrentSwapChain);
	}

	private void AttachCurrentSwapChain()
	{
		var swapChain = _engine.VideoSwapChain;
		if (swapChain == nint.Zero && _attachedSwapChain == nint.Zero)
		{
			return;
		}

		if (_attachedSwapChain == swapChain)
		{
			return;
		}

		try
		{
			SetSwapChain(swapChain);
			_attachedSwapChain = swapChain;
			_logger.LogInformation("mpv swapchain attached to WinUI surface. HasSwapChain={HasSwapChain}", swapChain != nint.Zero);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to attach mpv swapchain to WinUI surface. HasSwapChain={HasSwapChain}", swapChain != nint.Zero);
		}
	}

	private async void VideoSurface_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		await UpdateVideoOutputSizeAsync();
	}

	private async Task UpdateVideoOutputSizeAsync()
	{
		var scale = XamlRoot?.RasterizationScale ?? 1d;
		var width = (uint)Math.Max(0, Math.Round(VideoSurface.ActualWidth * scale));
		var height = (uint)Math.Max(0, Math.Round(VideoSurface.ActualHeight * scale));
		await _engine.SetVideoOutputSizeAsync(width, height);
	}

	private void VideoSurface_Unloaded(object sender, RoutedEventArgs e)
	{
		_engine.VideoOutputChanged -= OnVideoOutputChanged;
		if (_attachedSwapChain == nint.Zero)
		{
			return;
		}

		try
		{
			SetSwapChain(nint.Zero);
			_attachedSwapChain = nint.Zero;
			_logger.LogInformation("mpv swapchain detached from WinUI surface");
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to detach mpv swapchain from WinUI surface");
		}
	}

	private void SetSwapChain(nint swapChain)
	{
		var unknown = Marshal.GetIUnknownForObject(VideoSurface);
		nint nativePtr = nint.Zero;
		try
		{
			var iid = typeof(ISwapChainPanelNative).GUID;
			Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, ref iid, out nativePtr));
			ISwapChainPanelNative.SetSwapChain(nativePtr, swapChain);
		}
		finally
		{
			if (nativePtr != nint.Zero)
			{
				Marshal.Release(nativePtr);
			}

			Marshal.Release(unknown);
		}
	}

	[Guid("63AAD0B8-7C24-40FF-85A8-640D944CC325")]
	private unsafe readonly struct ISwapChainPanelNative
	{
		public static void SetSwapChain(nint nativePtr, nint swapChain)
		{
			var vtable = *(nint**)nativePtr;
			var setSwapChain = (delegate* unmanaged[Stdcall]<nint, nint, int>)vtable[3];
			Marshal.ThrowExceptionForHR(setSwapChain(nativePtr, swapChain));
		}
	}
}
