using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentFin.Contracts.ViewModels;
using FluentFin.Core.Contracts.Services;
using FluentFin.Core.Services;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Extensions.Logging;

namespace FluentFin.Core.ViewModels;

public partial class MusicArtistViewModel(
	IJellyfinClient jellyfinClient,
	INavigationServiceCore navigationService,
	ILogger<MusicArtistViewModel> logger) : ObservableObject, INavigationAware
{
	private const int AlbumRailLimit = 20;
	private CancellationTokenSource? _loadCts;

	public ObservableCollection<BaseItemViewModel> ArtistAlbums { get; } = [];

	[ObservableProperty]
	public partial BaseItemDto? Artist { get; set; }

	[ObservableProperty]
	public partial bool IsLoading { get; set; }

	public IJellyfinClient JellyfinClient => jellyfinClient;

	public string ArtistName => Artist?.Name ?? "";

	public string OverviewText => Artist?.Overview ?? "";

	public string MetadataText => string.Join(" - ", new[]
	{
		Artist?.Genres is { Count: > 0 } genres ? string.Join(", ", genres) : null,
		Artist?.Tags is { Count: > 0 } tags ? string.Join(", ", tags) : null
	}.Where(x => !string.IsNullOrWhiteSpace(x)));

	public Task OnNavigatedFrom()
	{
		_loadCts?.Cancel();
		_loadCts?.Dispose();
		_loadCts = null;
		return Task.CompletedTask;
	}

	public async Task OnNavigatedTo(object parameter)
	{
		var artist = parameter switch
		{
			BaseItemDto dto => dto,
			Guid id => await jellyfinClient.GetItem(id),
			_ => null
		};

		if (artist is null)
		{
			logger.LogWarning("Music artist page received unsupported navigation parameter. ParameterType={ParameterType}", parameter?.GetType().FullName ?? "<null>");
			return;
		}

		Artist = artist;
		OnPropertyChanged(nameof(ArtistName));
		OnPropertyChanged(nameof(OverviewText));
		OnPropertyChanged(nameof(MetadataText));
		await LoadArtistAlbums(artist);
	}

	[RelayCommand]
	private void ShowArtistAlbums()
	{
		if (Artist?.Id is not { } artistId)
		{
			return;
		}

		var title = string.IsNullOrWhiteSpace(ArtistName)
			? "Albums"
			: $"Albums by {ArtistName}";
		logger.LogInformation("Music artist albums category requested. ArtistId={ArtistId}, ArtistName={ArtistName}", artistId, ArtistName);
		navigationService.NavigateTo<MusicAlbumListViewModel>(new MusicAlbumCategoryListParameter(title, MusicAlbumCategoryKind.ArtistAlbums, null, artistId));
	}

	private async Task LoadArtistAlbums(BaseItemDto artist)
	{
		if (artist.Id is not { } artistId)
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
			logger.LogInformation("Music artist albums rail load started. ArtistId={ArtistId}, ArtistName={ArtistName}, Limit={Limit}", artistId, artist.Name, AlbumRailLimit);
			var result = await MusicAlbumListViewModel.GetArtistAlbums(jellyfinClient, artistId, null, 0, AlbumRailLimit, _loadCts.Token);

			ArtistAlbums.Clear();
			foreach (var item in result?.Items.Select(BaseItemViewModel.FromDto) ?? [])
			{
				ArtistAlbums.Add(item);
			}

			logger.LogInformation("Music artist albums rail load completed. ArtistId={ArtistId}, Count={Count}, TotalRecordCount={TotalRecordCount}, ElapsedMs={ElapsedMs}",
				artistId,
				ArtistAlbums.Count,
				result?.TotalRecordCount,
				elapsed.ElapsedMilliseconds);
		}
		catch (OperationCanceledException)
		{
			logger.LogInformation("Music artist albums rail load cancelled. ArtistId={ArtistId}, ElapsedMs={ElapsedMs}", artistId, elapsed.ElapsedMilliseconds);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Music artist albums rail load failed. ArtistId={ArtistId}, ElapsedMs={ElapsedMs}", artistId, elapsed.ElapsedMilliseconds);
		}
		finally
		{
			IsLoading = false;
		}
	}
}
