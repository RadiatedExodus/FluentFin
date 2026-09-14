using System.Collections.ObjectModel;
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

	public ObservableCollection<MusicArtistLinkViewModel> ArtistLinks { get; } = [];

	public bool HasArtistLinks => ArtistLinks.Count > 0;

	public bool HasNoArtistLinks => !HasArtistLinks;

	public MusicAlbumCard()
	{
		InitializeComponent();
		Loaded += (_, _) => RefreshArtistLinks();
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

	private void RefreshArtistLinks()
	{
		ArtistLinks.Clear();
		if (Model?.Dto is not { } album)
		{
			UpdateArtistLinkProperties();
			return;
		}

		var links = new List<(string Name, Guid? Id)>();
		AddArtistLinks(links, album.AlbumArtists);
		AddArtistLinks(links, album.ArtistItems);
		if (links.Count == 0)
		{
			AddArtistNames(links, album.Artists);
			if (!string.IsNullOrWhiteSpace(album.AlbumArtist))
			{
				links.Add((album.AlbumArtist, null));
			}
		}

		foreach (var artist in links
			.Where(x => !string.IsNullOrWhiteSpace(x.Name))
			.GroupBy(x => x.Id?.ToString() ?? x.Name, StringComparer.OrdinalIgnoreCase)
			.Select(x => x.First()))
		{
			ArtistLinks.Add(new MusicArtistLinkViewModel(artist.Name, artist.Id, OpenArtist));
		}

		UpdateArtistLinkProperties();
	}

	private async Task OpenArtist(string name, Guid? artistId)
	{
		if (artistId is null && JellyfinClient is not null)
		{
			var artist = await JellyfinClient.FindMusicArtistByName(name);
			artistId = artist?.Id;
		}

		if (artistId is { } id)
		{
			App.Commands.DisplayDto(new Jellyfin.Sdk.Generated.Models.BaseItemDto
			{
				Id = id,
				Name = name,
				Type = Jellyfin.Sdk.Generated.Models.BaseItemDto_Type.MusicArtist
			});
		}
	}

	private void UpdateArtistLinkProperties()
	{
		Bindings.Update();
	}

	private static void AddArtistLinks(List<(string Name, Guid? Id)> links, IReadOnlyList<Jellyfin.Sdk.Generated.Models.NameGuidPair>? artists)
	{
		foreach (var artist in artists ?? [])
		{
			if (!string.IsNullOrWhiteSpace(artist.Name))
			{
				links.Add((artist.Name, artist.Id));
			}
		}
	}

	private static void AddArtistNames(List<(string Name, Guid? Id)> links, IReadOnlyList<string>? artists)
	{
		foreach (var artist in artists ?? [])
		{
			if (!string.IsNullOrWhiteSpace(artist))
			{
				links.Add((artist, null));
			}
		}
	}
}
