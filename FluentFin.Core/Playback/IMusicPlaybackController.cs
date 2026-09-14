using Jellyfin.Sdk.Generated.Models;

namespace FluentFin.Core.Playback;

public interface IMusicPlaybackController
{
	event EventHandler? MusicOptionsChanged;

	bool ShuffleEnabled { get; }
	PlaybackRepeatMode RepeatMode { get; }

	Task PlaySongAsync(BaseItemDto song, CancellationToken cancellationToken = default);
	Task PlayAlbumFromTrackAsync(BaseItemDto album, BaseItemDto selectedTrack, CancellationToken cancellationToken = default);
	Task PlayPlaylistFromTrackAsync(BaseItemDto playlist, BaseItemDto selectedTrack, CancellationToken cancellationToken = default);
	Task NextAsync(CancellationToken cancellationToken = default);
	Task PreviousAsync(CancellationToken cancellationToken = default);
	Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default);
	Task SetRepeatModeAsync(PlaybackRepeatMode repeatMode, CancellationToken cancellationToken = default);
	Task AddToQueueAsync(BaseItemDto item, CancellationToken cancellationToken = default);
	Task PlayNextAsync(BaseItemDto item, CancellationToken cancellationToken = default);
	Task JumpToQueueItemAsync(int index, CancellationToken cancellationToken = default);
	Task RemoveQueueItemAsync(int index, CancellationToken cancellationToken = default);
}
