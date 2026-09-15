namespace FluentFin.Mpv;

public sealed record MpvTrack(
	long Id,
	long? FfmpegIndex,
	string Type,
	string? Language,
	string? Title);
