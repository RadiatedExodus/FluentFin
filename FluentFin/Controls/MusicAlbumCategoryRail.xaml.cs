using System.Collections.ObjectModel;
using System.Windows.Input;
using FluentFin.Core.Contracts.Services;
using FluentFin.Core.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FluentFin.Controls;

public sealed partial class MusicAlbumCategoryRail : UserControl
{
	public string Title { get; set; } = "";

	public ObservableCollection<BaseItemViewModel> Albums { get; set; } = [];

	public ICommand? HeaderCommand { get; set; }

	public IJellyfinClient? JellyfinClient { get; set; }

	public MusicAlbumCategoryRail()
	{
		InitializeComponent();
	}
}
