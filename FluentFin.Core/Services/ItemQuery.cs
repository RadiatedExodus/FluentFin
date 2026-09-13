using Jellyfin.Sdk.Generated.Models;

namespace FluentFin.Core.Services;

public sealed class ItemQuery
{
	public Guid? ParentId { get; init; }
	public bool Recursive { get; init; }
	public int StartIndex { get; init; }
	public int? Limit { get; init; }
	public ItemSortBy? SortBy { get; init; }
	public SortOrder? SortOrder { get; init; }
	public string? SearchTerm { get; init; }
	public IReadOnlyList<string> Genres { get; init; } = [];
	public IReadOnlyList<int> Years { get; init; } = [];
	public IReadOnlyList<string> Tags { get; init; } = [];
	public IReadOnlyList<string> OfficialRatings { get; init; } = [];
	public IReadOnlyList<BaseItemKind> IncludeItemTypes { get; init; } = [];
}
