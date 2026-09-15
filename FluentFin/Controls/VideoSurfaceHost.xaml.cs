using CommunityToolkit.WinUI;
using FluentFin.Core.Playback;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentFin.Controls;

public sealed partial class VideoSurfaceHost : UserControl
{
	[GeneratedDependencyProperty]
	public partial MediaPlayerType? Backend { get; set; }

	public VideoSurfaceHost()
	{
		InitializeComponent();
		RegisterPropertyChangedCallback(BackendProperty, (_, _) => RefreshSurface());
	}

	private void RefreshSurface()
	{
		Root.Children.Clear();
		if (Backend is null)
		{
			return;
		}

		UIElement surface = Backend switch
		{
			MediaPlayerType.Mpv => new MpvVideoView(),
			MediaPlayerType.Flyleaf => new FlyleafVideoView(),
			MediaPlayerType.Vlc => new VlcVideoView(),
			MediaPlayerType.WindowsMediaPlayer => new WindowsMediaVideoView(),
			_ => throw new NotSupportedException($"Unsupported video backend {Backend}.")
		};

		Root.Children.Add(surface);
	}
}
