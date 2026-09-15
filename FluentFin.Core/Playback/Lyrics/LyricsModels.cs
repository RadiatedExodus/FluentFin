namespace FluentFin.Core.Playback.Lyrics;

public enum LyricsSyncKind
{
	Unsynced,
	Synced
}

public sealed record LyricsLine(string Text, TimeSpan? Start);

public sealed record LyricsMetadata
{
	public string? Title { get; init; }
	public string? Artist { get; init; }
	public string? Album { get; init; }
	public string? Author { get; init; }
	public string? Creator { get; init; }
	public string? Provider { get; init; }
}

public sealed record LyricsDocument
{
	public required Guid ItemId { get; init; }
	public required LyricsSyncKind SyncKind { get; init; }
	public required IReadOnlyList<LyricsLine> Lines { get; init; }
	public LyricsMetadata? Metadata { get; init; }
}
