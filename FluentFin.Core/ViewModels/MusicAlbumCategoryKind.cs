namespace FluentFin.Core.ViewModels;

public enum MusicAlbumCategoryKind
{
	RecentlyAdded,
	LibraryAlbums,
	ArtistAlbums
}

public sealed record MusicAlbumCategoryListParameter(
	string Title,
	MusicAlbumCategoryKind Kind,
	Guid? ParentId,
	Guid? ArtistId = null);
