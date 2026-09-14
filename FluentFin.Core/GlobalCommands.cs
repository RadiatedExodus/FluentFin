using CommunityToolkit.Mvvm.Input;
using FluentFin.Core.Contracts.Services;
using FluentFin.Core.Playback;
using FluentFin.Core.Services;
using FluentFin.Core.Settings;
using FluentFin.Core.ViewModels;
using FluentFin.ViewModels;
using Jellyfin.Sdk.Generated.Models;

namespace FluentFin.Core;

public partial class GlobalCommands(INavigationServiceCore navigationService,
									IJellyfinClient jellyfinClient,
									IMusicPlaybackController musicPlaybackController,
									INavigationViewServiceCore navigationViewService)
{
	[RelayCommand]
	public async Task PlayDto(BaseItemDto dto)
	{
		if (IsMusicItem(dto))
		{
			await PlayMusicDto(dto);
			return;
		}

		if (SessionInfo.GroupId is not null)
		{
			var ids = await GetItemIds(dto);
			var info = await jellyfinClient.GetItem(ids[0]!.Value);

			var request = new PlayRequestDto
			{
				PlayingItemPosition = 0,
				PlayingQueue = ids,
				StartPositionTicks = info?.UserData?.PlaybackPositionTicks ?? 0,
			};
			await jellyfinClient.SignalNewPlaylist(request);
		}
		else if (!string.IsNullOrEmpty(SessionInfo.RemoteSessionId) && SessionInfo.SessionId != SessionInfo.RemoteSessionId)
		{
			await jellyfinClient.PlayOnSession(SessionInfo.RemoteSessionId, await GetItemIds(dto));
		}
		else
		{
			navigationService.NavigateTo("FluentFin.ViewModels.VideoPlayerViewModel", dto);
		}
	}

	[RelayCommand]
	public async Task ResetWatchProgress(Guid id)
	{
		await jellyfinClient.ResetProgress(id);
	}

	[RelayCommand]
	public async Task ToggleWatched(BaseItemDto dto)
	{
		await jellyfinClient.SetPlayed(dto, !(dto.UserData?.Played ?? false));
	}

	[RelayCommand]
	public async Task ToggleFavorite(BaseItemDto dto)
	{
		await jellyfinClient.SetIsFavorite(dto, !(dto.UserData?.IsFavorite ?? false));
	}

	[RelayCommand]
	public async Task PlayNextDto(BaseItemDto dto)
	{
		if (IsMusicItem(dto))
		{
			await musicPlaybackController.PlayNextAsync(dto);
		}
	}

	[RelayCommand]
	public async Task AddToQueueDto(BaseItemDto dto)
	{
		if (IsMusicItem(dto))
		{
			await musicPlaybackController.AddToQueueAsync(dto);
		}
	}

	[RelayCommand]
	public void DisplayDto(BaseItemDto dto)
	{
		switch (dto.Type)
		{
			case BaseItemDto_Type.Movie:
				navigationService.NavigateTo<MovieViewModel>(dto);
				break;
			case BaseItemDto_Type.Series:
				navigationService.NavigateTo<SeriesViewModel>(dto);
				break;
			case BaseItemDto_Type.Season:
				navigationService.NavigateTo<SeasonViewModel>(dto);
				break;
			case BaseItemDto_Type.Episode:
				navigationService.NavigateTo<EpisodeViewModel>(dto);
				break;
			case BaseItemDto_Type.MusicAlbum:
				navigationService.NavigateTo<MusicAlbumViewModel>(dto);
				break;
			case BaseItemDto_Type.CollectionFolder when dto.CollectionType is BaseItemDto_CollectionType.Music:
				navigationService.NavigateTo<MusicLibraryViewModel>(dto);
				break;
			default:
				break;
		}
	}

	[RelayCommand]
	public void NavigateToSegmentsEditor(BaseItemDto dto)
	{
		navigationService.NavigateTo("FluentFin.ViewModels.MediaSegmentsEditorViewModel", dto);
	}

	[RelayCommand]
	public async Task DeleteItem(BaseItemDto dto)
	{
		await jellyfinClient.DeleteItem(dto);
	}

	[RelayCommand]
	private void PinToSideBar(BaseItemDto library)
	{
		if (library is not { Id: not null, CollectionType: not null })
		{
			return;
		}

		var folder = new CustomNavigationViewItem
		{
			Key = typeof(LibraryViewModel).FullName!,
			Name = library.Name!,
			Parameter = library.Id.Value,
			Glyph = library.CollectionType.Value switch
			{
				BaseItemDto_CollectionType.Tvshows => "\uE7F4",
				BaseItemDto_CollectionType.Movies => "\uE8B2",
				BaseItemDto_CollectionType.Music => "\uE189",
				_ => null
			},
			Commands = [new CommandModel() { Name = "Unpin", DisplayName = "Unpin", Glyph = "\uE77A" }],
			Persistent = true,
		};

		navigationViewService.AddNavigationItem(folder);
		navigationViewService.SaveCustomViews();
	}

	[RelayCommand]
	private void UnPinFromSideBar(CustomNavigationViewItem item)
	{
		navigationViewService.RemoveNavigationItem(item);
	}

	private async Task<List<Guid?>> GetItemIds(BaseItemDto dto)
	{
		var playlist = dto.Type switch
		{
			BaseItemDto_Type.Movie => PlaylistViewModel.FromMovie(dto),
			BaseItemDto_Type.Episode => await PlaylistViewModel.FromEpisode(jellyfinClient, dto),
			BaseItemDto_Type.Series => await PlaylistViewModel.FromSeries(jellyfinClient, dto),
			BaseItemDto_Type.Season => await PlaylistViewModel.FromSeason(jellyfinClient, dto),
			_ => new PlaylistViewModel()
		};

		playlist.AutoSelect();

		if (playlist.SelectedItem is null)
		{
			return [];
		}

		var index = playlist.Items.IndexOf(playlist.SelectedItem);
		return [.. playlist.Items.Skip(index).Select(x => x.Dto.Id)];
	}

	private async Task PlayMusicDto(BaseItemDto dto)
	{
		switch (dto.Type)
		{
			case BaseItemDto_Type.Audio:
				await musicPlaybackController.PlaySongAsync(dto);
				break;
			case BaseItemDto_Type.MusicAlbum:
				if ((await jellyfinClient.GetPlayableAudioItems(dto)).FirstOrDefault() is { } firstTrack)
				{
					await musicPlaybackController.PlayAlbumFromTrackAsync(dto, firstTrack);
				}
				break;
			case BaseItemDto_Type.Playlist:
				if ((await jellyfinClient.GetPlayableAudioItems(dto)).FirstOrDefault() is { } firstPlaylistTrack)
				{
					await musicPlaybackController.PlayPlaylistFromTrackAsync(dto, firstPlaylistTrack);
				}
				break;
		}
	}

	private static bool IsMusicItem(BaseItemDto dto) =>
		dto.Type is BaseItemDto_Type.Audio or BaseItemDto_Type.MusicAlbum or BaseItemDto_Type.Playlist;

}
