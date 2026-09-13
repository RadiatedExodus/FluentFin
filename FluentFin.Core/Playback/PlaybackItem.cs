using Jellyfin.Sdk.Generated.Models;

namespace FluentFin.Core.Playback;

public sealed record PlaybackItem
{
	public required Guid JellyfinId { get; init; }
	public required PlaybackKind Kind { get; init; }
	public required string Title { get; init; }
	public TimeSpan? Duration { get; init; }
	public required BaseItemDto Item { get; init; }
	public PlaybackMetadata? Metadata { get; init; }

	public static PlaybackItem FromDto(BaseItemDto item, PlaybackKind kind)
	{
		if (item.Id is not { } id)
		{
			throw new ArgumentException("Playback items must have a Jellyfin id.", nameof(item));
		}

		return new PlaybackItem
		{
			JellyfinId = id,
			Kind = kind,
			Title = item.Name ?? "",
			Duration = item.RunTimeTicks is { } ticks ? TimeSpan.FromTicks(ticks) : null,
			Item = item
		};
	}
}
