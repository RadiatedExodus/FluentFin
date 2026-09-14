using FluentFin.Core.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FluentFin.Views;

public sealed partial class MusicArtistPage : Page
{
	public MusicArtistViewModel ViewModel { get; } = App.GetService<MusicArtistViewModel>();

	public MusicArtistPage()
	{
		InitializeComponent();
	}
}
