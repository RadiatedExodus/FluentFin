using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentFin.Contracts.ViewModels;
using FluentFin.Core.Contracts.Services;
using FluentFin.Core.Playback;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Extensions.Logging;

namespace FluentFin.Core.ViewModels;

public partial class MusicAlbumViewModel(
	IJellyfinClient jellyfinClient,
	IMusicPlaybackController musicPlaybackController,
	ILogger<MusicAlbumViewModel> logger) : ObservableObject, INavigationAware
{
	private CancellationTokenSource? _loadCts;

	public ObservableCollection<BaseItemDto> Tracks { get; } = [];

	[ObservableProperty]
	public partial BaseItemDto? Album { get; set; }

	[ObservableProperty]
	public partial bool IsLoading { get; set; }

	[ObservableProperty]
	public partial long? TotalRunTimeTicks { get; set; }

	public IJellyfinClient JellyfinClient => jellyfinClient;

	public string ArtistsText => Album?.AlbumArtist ?? Album?.Artists?.FirstOrDefault() ?? "";

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
		OnPropertyChanged(nameof(AlbumName));
		OnPropertyChanged(nameof(ArtistsText));
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

		await PlayFromTrack(Tracks[0]);
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
				Tracks.Add(track);
			}

			TotalRunTimeTicks = Tracks.Sum(x => x.RunTimeTicks ?? 0);
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
}
