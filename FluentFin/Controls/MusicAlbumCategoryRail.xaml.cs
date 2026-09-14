using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.WinUI;
using FluentFin.Core.Contracts.Services;
using FluentFin.Core.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FluentFin.Controls;

public sealed partial class MusicAlbumCategoryRail : UserControl
{
	[GeneratedDependencyProperty(DefaultValue = "")]
	public partial string Title { get; set; }

	[GeneratedDependencyProperty(DefaultValueCallback = nameof(Empty))]
	public partial ObservableCollection<BaseItemViewModel> Albums { get; set; }

	[GeneratedDependencyProperty(DefaultValueCallback = nameof(Empty))]
	public partial ObservableCollection<BaseItemViewModel> Items { get; set; }

	[GeneratedDependencyProperty]
	public partial ICommand? HeaderCommand { get; set; }

	[GeneratedDependencyProperty]
	public partial object? HeaderCommandParameter { get; set; }

	[GeneratedDependencyProperty]
	public partial IJellyfinClient? JellyfinClient { get; set; }

	[GeneratedDependencyProperty]
	public partial bool IsLoading { get; set; }

	public MusicAlbumCategoryRail()
	{
		InitializeComponent();
	}

	private static ObservableCollection<BaseItemViewModel> Empty() => [];
}
