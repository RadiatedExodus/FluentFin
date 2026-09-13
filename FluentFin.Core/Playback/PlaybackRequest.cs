namespace FluentFin.Core.Playback;

public sealed record PlaybackRequest
{
	public required PlaybackKind Kind { get; init; }
	public required PlaybackItem StartItem { get; init; }
	public IReadOnlyList<PlaybackItem> QueueItems { get; init; } = [];
	public int StartIndex { get; init; }
	public TimeSpan? StartPosition { get; init; }

	public IReadOnlyList<PlaybackItem> EffectiveQueue => QueueItems.Count == 0 ? [StartItem] : QueueItems;
}
