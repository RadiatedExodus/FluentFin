namespace FluentFin.Core.Playback;

public sealed class MusicQueuePolicy(PlaybackQueue queue)
{
	private readonly Random _random = new();

	public bool ShuffleEnabled { get; private set; }
	public PlaybackRepeatMode RepeatMode { get; private set; } = PlaybackRepeatMode.Off;

	public PlaybackItem? Current => queue.Current;
	public PlaybackItem? NextItem => queue.CanMoveNext ? queue.Items[queue.CurrentIndex + 1] : null;

	public void SetShuffle(bool enabled)
	{
		if (ShuffleEnabled == enabled)
		{
			return;
		}

		ShuffleEnabled = enabled;
		if (enabled)
		{
			ShuffleUpcoming();
		}
	}

	public void SetRepeatMode(PlaybackRepeatMode repeatMode) => RepeatMode = repeatMode;

	public PlaybackItem? MoveNext(bool automatic)
	{
		if (automatic && RepeatMode is PlaybackRepeatMode.One)
		{
			return queue.Current;
		}

		if (queue.CanMoveNext)
		{
			return queue.Next();
		}

		if (RepeatMode is PlaybackRepeatMode.All && queue.Items.Count > 0)
		{
			queue.Replace(queue.Items, 0);
			return queue.Current;
		}

		return null;
	}

	public PlaybackItem? MovePrevious()
	{
		if (queue.CanMovePrevious)
		{
			return queue.Previous();
		}

		if (RepeatMode is PlaybackRepeatMode.All && queue.Items.Count > 0)
		{
			queue.Replace(queue.Items, queue.Items.Count - 1);
			return queue.Current;
		}

		return null;
	}

	public PlaybackItem? JumpTo(int index) => queue.MoveTo(index);

	public void AddToQueue(IEnumerable<PlaybackItem> items)
	{
		queue.AddRange(items);
	}

	public void PlayNext(IEnumerable<PlaybackItem> items)
	{
		queue.InsertRange(queue.CurrentIndex + 1, items);
	}

	public PlaybackItem? RemoveAt(int index) => queue.RemoveAt(index);

	private void ShuffleUpcoming()
	{
		if (queue.CurrentIndex < 0 || queue.CurrentIndex >= queue.Items.Count - 2)
		{
			return;
		}

		var currentIndex = queue.CurrentIndex;
		var ordered = queue.Items.Take(currentIndex + 1).ToList();
		var upcoming = queue.Items.Skip(currentIndex + 1).ToList();
		for (var i = upcoming.Count - 1; i > 0; i--)
		{
			var j = _random.Next(i + 1);
			(upcoming[i], upcoming[j]) = (upcoming[j], upcoming[i]);
		}

		ordered.AddRange(upcoming);
		queue.Replace(ordered, currentIndex);
	}
}
