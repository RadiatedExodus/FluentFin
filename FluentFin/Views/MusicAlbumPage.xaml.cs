using FluentFin.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace FluentFin.Views;

public sealed partial class MusicAlbumPage : Page
{
	public MusicAlbumViewModel ViewModel { get; } = App.GetService<MusicAlbumViewModel>();

	public MusicAlbumPage()
	{
		InitializeComponent();
	}

	private async void Track_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
	{
		if ((sender as FrameworkElement)?.DataContext is not MusicAlbumTrackViewModel track)
		{
			return;
		}

		await ViewModel.PlayFromTrack(track.Dto);
	}
}
