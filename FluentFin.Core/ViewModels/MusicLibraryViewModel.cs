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

public partial class MusicLibraryViewModel(
	IJellyfinClient jellyfinClient,
	INavigationServiceCore navigationService,
	ILogger<MusicLibraryViewModel> logger) : ObservableObject, INavigationAware
{
	private const int AlbumRailLimit = 20;
	private CancellationTokenSource? _loadCts;
	private Guid? _parentId;

	public ObservableCollection<BaseItemViewModel> RecentlyAddedAlbums { get; } = [];

	[ObservableProperty]
	public partial string Title { get; set; } = "Audio";

	[ObservableProperty]
	public partial bool IsLoading { get; set; }

	public IJellyfinClient JellyfinClient => jellyfinClient;

	public Task OnNavigatedFrom()
	{
		_loadCts?.Cancel();
		_loadCts?.Dispose();
		_loadCts = null;
		return Task.CompletedTask;
	}

	public async Task OnNavigatedTo(object parameter)
	{
		_parentId = parameter switch
		{
			BaseItemDto { Id: { } id } => id,
			Guid id => id,
			_ => null
		};
		Title = parameter is BaseItemDto { Name: not null } library ? library.Name! : "Audio";
		await LoadRecentlyAddedAlbums();
	}

	[RelayCommand]
	private void ShowRecentlyAddedAlbums()
	{
		logger.LogInformation("Music recently-added category requested. ParentId={ParentId}", _parentId);
		navigationService.NavigateTo<MusicAlbumListViewModel>(new MusicAlbumCategoryListParameter("Recently added", MusicAlbumCategoryKind.RecentlyAdded, _parentId));
	}

	[RelayCommand]
	private void OpenAlbum(BaseItemDto album)
	{
		logger.LogInformation("Music album navigation requested. AlbumId={AlbumId}, AlbumName={AlbumName}", album.Id, album.Name);
		navigationService.NavigateTo<MusicAlbumViewModel>(album);
	}

	private async Task LoadRecentlyAddedAlbums()
	{
		var elapsed = Stopwatch.StartNew();
		IsLoading = true;
		_loadCts?.Cancel();
		_loadCts?.Dispose();
		_loadCts = new CancellationTokenSource();

		try
		{
			var query = CreateRecentlyAddedAlbumsQuery(_parentId, 0, AlbumRailLimit);
			logger.LogInformation("Music library recently-added albums load started. ParentId={ParentId}, Limit={Limit}", _parentId, AlbumRailLimit);
			var result = await jellyfinClient.GetItems(query, _loadCts.Token);
			RecentlyAddedAlbums.Clear();
			foreach (var item in result?.Items.Select(BaseItemViewModel.FromDto) ?? [])
			{
				RecentlyAddedAlbums.Add(item);
			}
			logger.LogInformation("Music library recently-added albums load completed. ParentId={ParentId}, Count={Count}, ElapsedMs={ElapsedMs}", _parentId, RecentlyAddedAlbums.Count, elapsed.ElapsedMilliseconds);
		}
		catch (OperationCanceledException)
		{
			logger.LogInformation("Music library recently-added albums load cancelled. ParentId={ParentId}, ElapsedMs={ElapsedMs}", _parentId, elapsed.ElapsedMilliseconds);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Music library recently-added albums load failed. ParentId={ParentId}, ElapsedMs={ElapsedMs}", _parentId, elapsed.ElapsedMilliseconds);
		}
		finally
		{
			IsLoading = false;
		}
	}

	public static ItemQuery CreateRecentlyAddedAlbumsQuery(Guid? parentId, int startIndex, int limit) =>
		new()
		{
			ParentId = parentId,
			Recursive = true,
			StartIndex = startIndex,
			Limit = limit,
			SortBy = ItemSortBy.DateCreated,
			SortOrder = SortOrder.Descending,
			IncludeItemTypes = [BaseItemKind.MusicAlbum]
		};
}
