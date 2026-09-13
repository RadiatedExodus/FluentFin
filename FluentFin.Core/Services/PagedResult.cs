namespace FluentFin.Core.Services;

public sealed class PagedResult<T>
{
	public IReadOnlyList<T> Items { get; init; } = [];
	public int StartIndex { get; init; }
	public int TotalRecordCount { get; init; }
}

