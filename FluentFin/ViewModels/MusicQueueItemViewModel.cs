using CommunityToolkit.Mvvm.ComponentModel;
using FluentFin.Core.Playback;

namespace FluentFin.ViewModels;

public partial class MusicQueueItemViewModel(PlaybackItem item, int index, bool isCurrent) : ObservableObject
{
	public PlaybackItem Item { get; } = item;
	public int Index { get; } = index;

	[ObservableProperty]
	public partial bool IsCurrent { get; set; } = isCurrent;

	public string Title => Item.Title;

	public string ArtistText => Item.Metadata is MusicPlaybackMetadata music
		? string.Join(", ", music.Artists.Where(x => !string.IsNullOrWhiteSpace(x)))
		: "";

	public string AlbumTitle => Item.Metadata is MusicPlaybackMetadata music ? music.Album ?? "" : "";

	public string DurationText => Item.Duration is { } duration
		? FluentFin.Converters.Converters.TimeSpanToString(duration)
		: "";
}
