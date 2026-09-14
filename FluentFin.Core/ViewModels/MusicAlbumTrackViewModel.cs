using System.Collections.ObjectModel;
using Jellyfin.Sdk.Generated.Models;

namespace FluentFin.Core.ViewModels;

public sealed class MusicAlbumTrackViewModel
{
	public MusicAlbumTrackViewModel(BaseItemDto dto, Func<string, Guid?, Task> openArtist)
	{
		Dto = dto;
		foreach (var artist in CreateArtistLinks(dto, openArtist))
		{
			ArtistLinks.Add(artist);
		}
	}

	public BaseItemDto Dto { get; }

	public ObservableCollection<MusicArtistLinkViewModel> ArtistLinks { get; } = [];

	public string ArtistsText => string.Join(", ", Dto.Artists ?? []);

	public bool HasArtistLinks => ArtistLinks.Count > 0;

	public bool HasNoArtistLinks => !HasArtistLinks;

	private static IEnumerable<MusicArtistLinkViewModel> CreateArtistLinks(BaseItemDto dto, Func<string, Guid?, Task> openArtist)
	{
		var links = new List<(string Name, Guid? Id)>();

		foreach (var artist in dto.ArtistItems ?? [])
		{
			if (!string.IsNullOrWhiteSpace(artist.Name))
			{
				links.Add((artist.Name, artist.Id));
			}
		}

		if (links.Count == 0)
		{
			foreach (var artistName in dto.Artists ?? [])
			{
				if (!string.IsNullOrWhiteSpace(artistName))
				{
					links.Add((artistName, null));
				}
			}
		}

		return links
			.GroupBy(x => x.Id?.ToString() ?? x.Name, StringComparer.OrdinalIgnoreCase)
			.Select(x => x.First())
			.Select(x => new MusicArtistLinkViewModel(x.Name, x.Id, openArtist));
	}
}
