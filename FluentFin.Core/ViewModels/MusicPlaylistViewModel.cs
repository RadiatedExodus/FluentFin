using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentFin.Contracts.ViewModels;
using FluentFin.Core.Contracts.Services;
using FluentFin.Core.Playback;
using FluentFin.Core.Services;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Extensions.Logging;

namespace FluentFin.Core.ViewModels;

public partial class MusicPlaylistViewModel(
	IJellyfinClient jellyfinClient,
	IMusicPlaybackController musicPlaybackController,
	INavigationServiceCore navigationService,
	ILogger<MusicPlaylistViewModel> logger) : ObservableObject, INavigationAware
{
	private CancellationTokenSource? _loadCts;

	public ObservableCollection<MusicAlbumTrackViewModel> Tracks { get; } = [];

	[ObservableProperty]
	public partial BaseItemDto? Playlist { get; set; }

	[ObservableProperty]
	public partial bool IsLoading { get; set; }

	public bool IsNotLoading => !IsLoading;

	[ObservableProperty]
	public partial bool CanEditPlaylist { get; set; }

	[ObservableProperty]
	public partial long? TotalRunTimeTicks { get; set; }

	public IJellyfinClient JellyfinClient => jellyfinClient;

	public string PlaylistName => Playlist?.Name ?? "";

	public string OverviewText => Playlist?.Overview ?? "";

	public bool HasOverview => !string.IsNullOrWhiteSpace(OverviewText);

	public string TrackCountText => Tracks.Count == 1 ? "1 track" : $"{Tracks.Count} tracks";

	public Task OnNavigatedFrom()
	{
		_loadCts?.Cancel();
		_loadCts?.Dispose();
		_loadCts = null;
		return Task.CompletedTask;
	}

	public async Task OnNavigatedTo(object parameter)
	{
		var playlist = parameter switch
		{
			BaseItemDto dto => dto,
			Guid id => await jellyfinClient.GetItem(id),
			_ => null
		};

		if (playlist is null)
		{
			logger.LogWarning("Music playlist page received unsupported navigation parameter. ParameterType={ParameterType}", parameter?.GetType().FullName ?? "<null>");
			return;
		}

		Playlist = playlist;
		CanEditPlaylist = playlist.CanDelete == true || SessionInfo.CurrentUser?.Policy?.IsAdministrator == true;
		RefreshHeaderProperties();
		await LoadTracks();
	}

	[RelayCommand]
	private async Task PlayPlaylist()
	{
		if (Tracks.FirstOrDefault() is { } firstTrack)
		{
			await PlayFromTrack(firstTrack.Dto);
		}
	}

	[RelayCommand]
	private async Task PlayTrack(MusicAlbumTrackViewModel? track)
	{
		if (track is not null)
		{
			await PlayFromTrack(track.Dto);
		}
	}

	public async Task PlayFromTrack(BaseItemDto track)
	{
		if (Playlist is null)
		{
			return;
		}

		logger.LogInformation("Music playlist playback requested. PlaylistId={PlaylistId}, TrackId={TrackId}, TrackName={TrackName}", Playlist.Id, track.Id, track.Name);
		await musicPlaybackController.PlayPlaylistFromTrackAsync(Playlist, track);
	}

	public async Task RenamePlaylist(string newName)
	{
		if (Playlist is null || string.IsNullOrWhiteSpace(newName))
		{
			return;
		}

		if (!CanEditPlaylist)
		{
			logger.LogWarning("Music playlist rename skipped because edit controls are not allowed. PlaylistId={PlaylistId}", Playlist.Id);
			return;
		}

		var trimmed = newName.Trim();
		if (string.Equals(Playlist.Name, trimmed, StringComparison.CurrentCulture))
		{
			return;
		}

		logger.LogInformation("Music playlist rename requested. PlaylistId={PlaylistId}, NewName={NewName}", Playlist.Id, trimmed);
		await jellyfinClient.RenamePlaylist(Playlist, trimmed);
		Playlist = Playlist.Id is { } id ? await jellyfinClient.GetItem(id) ?? Playlist : Playlist;
		RefreshHeaderProperties();
	}

	public async Task DeletePlaylist()
	{
		if (Playlist is null)
		{
			return;
		}

		if (!CanEditPlaylist)
		{
			logger.LogWarning("Music playlist delete skipped because edit controls are not allowed. PlaylistId={PlaylistId}", Playlist.Id);
			return;
		}

		logger.LogInformation("Music playlist delete requested. PlaylistId={PlaylistId}, PlaylistName={PlaylistName}", Playlist.Id, Playlist.Name);
		await jellyfinClient.DeletePlaylist(Playlist);
		navigationService.GoBack();
	}

	[RelayCommand]
	private async Task RemoveTrack(MusicAlbumTrackViewModel? track)
	{
		if (Playlist?.Id is not { } playlistId || track is null)
		{
			return;
		}

		if (!CanEditPlaylist)
		{
			logger.LogWarning("Music playlist track removal skipped because edit controls are not allowed. PlaylistId={PlaylistId}, TrackId={TrackId}", playlistId, track.Dto.Id);
			return;
		}

		if (string.IsNullOrWhiteSpace(track.Dto.PlaylistItemId))
		{
			logger.LogWarning("Music playlist track removal skipped because playlist item id is missing. PlaylistId={PlaylistId}, TrackId={TrackId}", playlistId, track.Dto.Id);
			return;
		}

		logger.LogInformation("Music playlist track removal requested. PlaylistId={PlaylistId}, TrackId={TrackId}, PlaylistItemId={PlaylistItemId}", playlistId, track.Dto.Id, track.Dto.PlaylistItemId);
		await jellyfinClient.RemovePlaylistItems(playlistId, [track.Dto.PlaylistItemId]);
		await LoadTracks();
	}

	public Task OpenArtist(string name, Guid? artistId) => OpenArtist(name, artistId, CancellationToken.None);

	public async Task OpenArtist(string name, Guid? artistId, CancellationToken cancellationToken)
	{
		if (artistId is null)
		{
			var artist = await jellyfinClient.FindMusicArtistByName(name, cancellationToken);
			artistId = artist?.Id;
		}

		if (artistId is null)
		{
			logger.LogWarning("Music playlist artist navigation skipped because artist id could not be resolved. PlaylistId={PlaylistId}, ArtistName={ArtistName}", Playlist?.Id, name);
			return;
		}

		logger.LogInformation("Music playlist artist navigation requested. PlaylistId={PlaylistId}, ArtistId={ArtistId}, ArtistName={ArtistName}", Playlist?.Id, artistId, name);
		navigationService.NavigateTo<MusicArtistViewModel>(artistId);
	}

	private async Task LoadTracks()
	{
		if (Playlist?.Id is not { } playlistId)
		{
			return;
		}

		var elapsed = Stopwatch.StartNew();
		IsLoading = true;
		_loadCts?.Cancel();
		_loadCts?.Dispose();
		_loadCts = new CancellationTokenSource();

		try
		{
			logger.LogInformation("Music playlist tracks load started. PlaylistId={PlaylistId}, PlaylistName={PlaylistName}", playlistId, Playlist.Name);
			var result = await jellyfinClient.GetItems(new ItemQuery
			{
				PlaylistId = playlistId,
				StartIndex = 0,
				Limit = null,
				IncludeItemTypes = [BaseItemKind.Audio],
				MediaTypes = [MediaType.Audio]
			}, _loadCts.Token);

			Tracks.Clear();
			foreach (var track in result?.Items ?? [])
			{
				Tracks.Add(new MusicAlbumTrackViewModel(track, OpenArtist, Tracks.Count + 1));
			}

			TotalRunTimeTicks = Tracks.Sum(x => x.Dto.RunTimeTicks ?? 0);
			RefreshHeaderProperties();
			logger.LogInformation("Music playlist tracks load completed. PlaylistId={PlaylistId}, TrackCount={TrackCount}, TotalRunTimeTicks={TotalRunTimeTicks}, ElapsedMs={ElapsedMs}",
				playlistId,
				Tracks.Count,
				TotalRunTimeTicks,
				elapsed.ElapsedMilliseconds);
		}
		catch (OperationCanceledException)
		{
			logger.LogInformation("Music playlist tracks load cancelled. PlaylistId={PlaylistId}, ElapsedMs={ElapsedMs}", playlistId, elapsed.ElapsedMilliseconds);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Music playlist tracks load failed. PlaylistId={PlaylistId}, ElapsedMs={ElapsedMs}", playlistId, elapsed.ElapsedMilliseconds);
		}
		finally
		{
			IsLoading = false;
		}
	}

	private void RefreshHeaderProperties()
	{
		OnPropertyChanged(nameof(PlaylistName));
		OnPropertyChanged(nameof(OverviewText));
		OnPropertyChanged(nameof(HasOverview));
		OnPropertyChanged(nameof(TrackCountText));
	}

	partial void OnIsLoadingChanged(bool value)
	{
		OnPropertyChanged(nameof(IsNotLoading));
	}
}
