namespace FluentFin.Core.Playback;

public sealed class PlaybackQueue
{
	private readonly List<PlaybackItem> _items = [];

	public event EventHandler<PlaybackQueueChangedEventArgs>? Changed;

	public IReadOnlyList<PlaybackItem> Items => _items;
	public int CurrentIndex { get; private set; } = -1;
	public PlaybackItem? Current => CurrentIndex >= 0 && CurrentIndex < _items.Count ? _items[CurrentIndex] : null;
	public bool CanMoveNext => CurrentIndex >= 0 && CurrentIndex < _items.Count - 1;
	public bool CanMovePrevious => CurrentIndex > 0 && CurrentIndex < _items.Count;

	public void Replace(IReadOnlyList<PlaybackItem> items, int startIndex)
	{
		var replacement = items.ToList();
		_items.Clear();
		_items.AddRange(replacement);
		CurrentIndex = _items.Count == 0 ? -1 : Math.Clamp(startIndex, 0, _items.Count - 1);
		OnChanged();
	}

	public PlaybackItem? Next()
	{
		if (!CanMoveNext)
		{
			return null;
		}

		CurrentIndex++;
		OnChanged();
		return Current;
	}

	public PlaybackItem? Previous()
	{
		if (!CanMovePrevious)
		{
			return null;
		}

		CurrentIndex--;
		OnChanged();
		return Current;
	}

	public PlaybackItem? MoveTo(int index)
	{
		if (index < 0 || index >= _items.Count)
		{
			return null;
		}

		CurrentIndex = index;
		OnChanged();
		return Current;
	}

	public void Add(PlaybackItem item)
	{
		_items.Add(item);
		if (CurrentIndex < 0)
		{
			CurrentIndex = 0;
		}

		OnChanged();
	}

	public void AddRange(IEnumerable<PlaybackItem> items)
	{
		var hadItems = _items.Count > 0;
		_items.AddRange(items);
		if (!hadItems && _items.Count > 0)
		{
			CurrentIndex = 0;
		}

		OnChanged();
	}

	public void InsertRange(int index, IEnumerable<PlaybackItem> items)
	{
		var insertIndex = Math.Clamp(index, 0, _items.Count);
		var inserted = items.ToList();
		if (inserted.Count == 0)
		{
			return;
		}

		_items.InsertRange(insertIndex, inserted);
		if (CurrentIndex < 0)
		{
			CurrentIndex = 0;
		}
		else if (insertIndex <= CurrentIndex)
		{
			CurrentIndex += inserted.Count;
		}

		OnChanged();
	}

	public PlaybackItem? RemoveAt(int index)
	{
		if (index < 0 || index >= _items.Count)
		{
			return null;
		}

		var removed = _items[index];
		_items.RemoveAt(index);

		if (_items.Count == 0)
		{
			CurrentIndex = -1;
		}
		else if (index < CurrentIndex)
		{
			CurrentIndex--;
		}
		else if (index == CurrentIndex)
		{
			CurrentIndex = Math.Min(index, _items.Count - 1);
		}

		OnChanged();
		return removed;
	}

	public void Clear()
	{
		_items.Clear();
		CurrentIndex = -1;
		OnChanged();
	}

	private void OnChanged() => Changed?.Invoke(this, new PlaybackQueueChangedEventArgs(_items.ToList(), CurrentIndex));
}
