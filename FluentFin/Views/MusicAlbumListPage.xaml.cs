using FluentFin.Core.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FluentFin.Views;

public sealed partial class MusicAlbumListPage : Page
{
	public MusicAlbumListViewModel ViewModel { get; } = App.GetService<MusicAlbumListViewModel>();

	public MusicAlbumListPage()
	{
		InitializeComponent();
	}
}
