namespace FluentFin.Core.Playback;

public sealed class SubtitleTrack
{
	public SubtitleTrack()
	{
	}

	public SubtitleTrack(int id, string? language, string? name, bool isExternal)
	{
		Id = id;
		Language = language;
		Name = name;
		IsExternal = isExternal;
	}

	public int Id { get; set; }
	public string? Language { get; set; }
	public string? Name { get; set; }
	public bool IsExternal { get; set; }
}
