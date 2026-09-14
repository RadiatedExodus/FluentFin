namespace FluentFin.Core.ViewModels;

public enum MusicAlbumCategoryKind
{
	RecentlyAdded
}

public sealed record MusicAlbumCategoryListParameter(
	string Title,
	MusicAlbumCategoryKind Kind,
	Guid? ParentId);
