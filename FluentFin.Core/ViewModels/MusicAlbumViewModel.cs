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

public partial class MusicAlbumViewModel(
	IJellyfinClient jellyfinClient,
	IMusicPlaybackController musicPlaybackController,
	INavigationServiceCore navigationService,
	ILogger<MusicAlbumViewModel> logger) : ObservableObject, INavigationAware
{
	private CancellationTokenSource? _loadCts;

	public ObservableCollection<MusicAlbumTrackViewModel> Tracks { get; } = [];
	public ObservableCollection<MusicArtistLinkViewModel> ArtistLinks { get; } = [];

	[ObservableProperty]
	public partial BaseItemDto? Album { get; set; }

	[ObservableProperty]
	public partial bool IsLoading { get; set; }

	[ObservableProperty]
	public partial long? TotalRunTimeTicks { get; set; }

	public IJellyfinClient JellyfinClient => jellyfinClient;

	public string ArtistsText => Album?.AlbumArtist ?? Album?.Artists?.FirstOrDefault() ?? "";

	public bool HasArtistLinks => ArtistLinks.Count > 0;

	public bool HasNoArtistLinks => !HasArtistLinks;

	public string AlbumName => Album?.Name ?? "";

	public string YearText => Album?.ProductionYear?.ToString() ?? "";

	public Task OnNavigatedFrom()
	{
		_loadCts?.Cancel();
		_loadCts?.Dispose();
		_loadCts = null;
		return Task.CompletedTask;
	}

	public async Task OnNavigatedTo(object parameter)
	{
		var album = parameter switch
		{
			BaseItemDto dto => dto,
			Guid id => await jellyfinClient.GetItem(id),
			_ => null
		};

		if (album is null)
		{
			logger.LogWarning("Music album page received unsupported navigation parameter. ParameterType={ParameterType}", parameter?.GetType().FullName ?? "<null>");
			return;
		}

		Album = album;
		LoadArtistLinks(album);
		OnPropertyChanged(nameof(AlbumName));
		OnPropertyChanged(nameof(ArtistsText));
		OnPropertyChanged(nameof(HasArtistLinks));
		OnPropertyChanged(nameof(HasNoArtistLinks));
		OnPropertyChanged(nameof(YearText));
		await LoadTracks(album);
	}

	[RelayCommand]
	private async Task PlayAlbum()
	{
		if (Album is null || Tracks.Count == 0)
		{
			return;
		}

		await PlayFromTrack(Tracks[0].Dto);
	}

	public async Task PlayFromTrack(BaseItemDto track)
	{
		if (Album is null)
		{
			return;
		}

		logger.LogInformation("Music album playback requested. AlbumId={AlbumId}, TrackId={TrackId}, TrackName={TrackName}", Album.Id, track.Id, track.Name);
		await musicPlaybackController.PlayAlbumFromTrackAsync(Album, track);
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
			logger.LogWarning("Music album artist navigation skipped because artist id could not be resolved. AlbumId={AlbumId}, ArtistName={ArtistName}", Album?.Id, name);
			return;
		}

		logger.LogInformation("Music album artist navigation requested. AlbumId={AlbumId}, ArtistId={ArtistId}, ArtistName={ArtistName}", Album?.Id, artistId, name);
		navigationService.NavigateTo<MusicArtistViewModel>(artistId);
	}

	private async Task LoadTracks(BaseItemDto album)
	{
		var elapsed = Stopwatch.StartNew();
		IsLoading = true;
		_loadCts?.Cancel();
		_loadCts?.Dispose();
		_loadCts = new CancellationTokenSource();

		try
		{
			logger.LogInformation("Music album tracks load started. AlbumId={AlbumId}, AlbumName={AlbumName}", album.Id, album.Name);
			var tracks = await jellyfinClient.GetPlayableAudioItems(album, _loadCts.Token);

			Tracks.Clear();
			foreach (var track in tracks)
			{
				Tracks.Add(new MusicAlbumTrackViewModel(track, OpenArtist));
			}

			TotalRunTimeTicks = Tracks.Sum(x => x.Dto.RunTimeTicks ?? 0);
			logger.LogInformation("Music album tracks load completed. AlbumId={AlbumId}, TrackCount={TrackCount}, TotalRunTimeTicks={TotalRunTimeTicks}, ElapsedMs={ElapsedMs}",
				album.Id,
				Tracks.Count,
				TotalRunTimeTicks,
				elapsed.ElapsedMilliseconds);
		}
		catch (OperationCanceledException)
		{
			logger.LogInformation("Music album tracks load cancelled. AlbumId={AlbumId}, ElapsedMs={ElapsedMs}", album.Id, elapsed.ElapsedMilliseconds);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Music album tracks load failed. AlbumId={AlbumId}, ElapsedMs={ElapsedMs}", album.Id, elapsed.ElapsedMilliseconds);
		}
		finally
		{
			IsLoading = false;
		}
	}

	private void LoadArtistLinks(BaseItemDto album)
	{
		ArtistLinks.Clear();
		HashSet<Guid> seen = [];
		foreach (var artist in (album.AlbumArtists ?? []).Concat(album.ArtistItems ?? []))
		{
			if (string.IsNullOrWhiteSpace(artist.Name))
			{
				continue;
			}

			if (artist.Id is { } id && !seen.Add(id))
			{
				continue;
			}

			ArtistLinks.Add(new MusicArtistLinkViewModel(artist.Name, artist.Id, OpenArtist));
		}

		if (ArtistLinks.Count > 0)
		{
			return;
		}

		foreach (var artistName in album.Artists ?? [])
		{
			if (string.IsNullOrWhiteSpace(artistName))
			{
				continue;
			}

			ArtistLinks.Add(new MusicArtistLinkViewModel(artistName, null, OpenArtist));
		}
	}
}
