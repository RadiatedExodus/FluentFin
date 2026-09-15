using FluentFin.MediaPlayers;
using Microsoft.UI.Xaml.Controls;

namespace FluentFin.Controls;

public sealed partial class FlyleafVideoView : UserControl
{
	private static bool _flyleafStarted;

	public FlyleafVideoView()
	{
		InitializeComponent();
		if (!_flyleafStarted)
		{
			App.StartFlyleaf();
			_flyleafStarted = true;
		}

		Host.Player = App.GetService<FlyleafPlaybackEngine>().Player;
	}
}
