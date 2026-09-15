using FluentFin.MediaPlayers;
using Microsoft.UI.Xaml.Controls;

namespace FluentFin.Controls;

public sealed partial class WindowsMediaVideoView : UserControl
{
	public WindowsMediaVideoView()
	{
		InitializeComponent();
		Element.SetMediaPlayer(App.GetService<WindowsVideoPlaybackEngine>().Player);
	}
}
