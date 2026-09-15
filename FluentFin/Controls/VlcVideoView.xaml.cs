using FluentFin.MediaPlayers;
using LibVLCSharp.Platforms.Windows;
using Microsoft.UI.Xaml.Controls;

namespace FluentFin.Controls;

public sealed partial class VlcVideoView : UserControl
{
	public VlcVideoView()
	{
		InitializeComponent();
		VideoView.Initialized += OnInitialized;
	}

	private void OnInitialized(object? sender, InitializedEventArgs e)
	{
		App.GetService<VlcPlaybackEngine>().AttachVideoView(VideoView, e.SwapChainOptions);
	}
}
