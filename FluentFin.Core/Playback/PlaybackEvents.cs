namespace FluentFin.Core.Playback;

public sealed class PlaybackStateChangedEventArgs(PlaybackState state) : EventArgs
{
	public PlaybackState State { get; } = state;
}

public sealed class PositionChangedEventArgs(TimeSpan position) : EventArgs
{
	public TimeSpan Position { get; } = position;
}

public sealed class DurationChangedEventArgs(TimeSpan duration) : EventArgs
{
	public TimeSpan Duration { get; } = duration;
}

public sealed class PlaybackErrorEventArgs(Exception? exception, string? message = null) : EventArgs
{
	public Exception? Exception { get; } = exception;
	public string? Message { get; } = message;
}

public sealed class QueueItemChangedEventArgs(int index) : EventArgs
{
	public int Index { get; } = index;
}

public sealed class PlaybackQueueChangedEventArgs(IReadOnlyList<PlaybackItem> items, int currentIndex) : EventArgs
{
	public IReadOnlyList<PlaybackItem> Items { get; } = items;
	public int CurrentIndex { get; } = currentIndex;
}
