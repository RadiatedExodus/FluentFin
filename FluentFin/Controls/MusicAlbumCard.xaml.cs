using CommunityToolkit.WinUI;
using FluentFin.Core.Contracts.Services;
using FluentFin.Core.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FluentFin.Controls;

public sealed partial class MusicAlbumCard : UserControl
{
	[GeneratedDependencyProperty]
	public partial BaseItemViewModel? Model { get; set; }

	[GeneratedDependencyProperty]
	public partial IJellyfinClient? JellyfinClient { get; set; }

	[GeneratedDependencyProperty(DefaultValue = 220d)]
	public partial double ArtworkSize { get; set; }

	public MusicAlbumCard()
	{
		InitializeComponent();
	}

	private void Artwork_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
	{
		if (Model?.Dto is null)
		{
			return;
		}

		App.Commands.DisplayDto(Model.Dto);
		e.Handled = true;
	}

	private void PlayButton_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
	{
		e.Handled = true;
	}
}
