namespace FluentFin.Core.Playback;

public sealed class PlaybackQueue
{
	private readonly List<PlaybackItem> _items = [];

	public IReadOnlyList<PlaybackItem> Items => _items;
	public int CurrentIndex { get; private set; } = -1;
	public PlaybackItem? Current => CurrentIndex >= 0 && CurrentIndex < _items.Count ? _items[CurrentIndex] : null;
	public bool CanMoveNext => CurrentIndex >= 0 && CurrentIndex < _items.Count - 1;
	public bool CanMovePrevious => CurrentIndex > 0 && CurrentIndex < _items.Count;

	public void Replace(IReadOnlyList<PlaybackItem> items, int startIndex)
	{
		_items.Clear();
		_items.AddRange(items);
		CurrentIndex = _items.Count == 0 ? -1 : Math.Clamp(startIndex, 0, _items.Count - 1);
	}

	public PlaybackItem? Next()
	{
		if (!CanMoveNext)
		{
			return null;
		}

		CurrentIndex++;
		return Current;
	}

	public PlaybackItem? Previous()
	{
		if (!CanMovePrevious)
		{
			return null;
		}

		CurrentIndex--;
		return Current;
	}

	public void Clear()
	{
		_items.Clear();
		CurrentIndex = -1;
	}
}
