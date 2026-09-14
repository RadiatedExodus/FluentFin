namespace FluentFin.Core.ViewModels;

public enum MusicAlbumCategoryKind
{
	RecentlyAdded,
	RecentlyReleasedAlbums,
	Playlists,
	RecentlyPlayedSongs,
	RecentlyPlayedAlbums,
	MostPlayedSongs,
	FavoriteAlbums,
	FavoriteSongs,
	LibraryAlbums,
	ArtistAlbums
}

public sealed record MusicAlbumCategoryListParameter(
	string Title,
	MusicAlbumCategoryKind Kind,
	Guid? ParentId,
	Guid? ArtistId = null);
