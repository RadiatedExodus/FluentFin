using FluentFin.Core.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FluentFin.Views;

public sealed partial class MusicLibraryPage : Page
{
	public MusicLibraryViewModel ViewModel { get; } = App.GetService<MusicLibraryViewModel>();

	public MusicLibraryPage()
	{
		InitializeComponent();
	}
}
