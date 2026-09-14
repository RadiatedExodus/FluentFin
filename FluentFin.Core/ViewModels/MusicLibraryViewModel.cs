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
	private const int MaxConcurrentCategoryLoads = 4;
	private CancellationTokenSource? _loadCts;
	private Guid? _parentId;

	public ObservableCollection<MusicCategoryViewModel> Categories { get; } = [];

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
		await LoadCategories();
	}

	[RelayCommand]
	private void ShowCategory(MusicCategoryViewModel category)
	{
		if (category.IsLoading)
		{
			logger.LogDebug("Music category request ignored while loading. Category={Category}, ParentId={ParentId}", category.Kind, category.ParentId);
			return;
		}

		logger.LogInformation("Music category requested. Category={Category}, ParentId={ParentId}", category.Kind, category.ParentId);
		navigationService.NavigateTo<MusicAlbumListViewModel>(new MusicAlbumCategoryListParameter(category.Title, category.Kind, category.ParentId));
	}

	[RelayCommand]
	private void OpenAlbum(BaseItemDto album)
	{
		logger.LogInformation("Music album navigation requested. AlbumId={AlbumId}, AlbumName={AlbumName}", album.Id, album.Name);
		navigationService.NavigateTo<MusicAlbumViewModel>(album);
	}

	private async Task LoadCategories()
	{
		var elapsed = Stopwatch.StartNew();
		IsLoading = true;
		_loadCts?.Cancel();
		_loadCts?.Dispose();
		_loadCts = new CancellationTokenSource();

		try
		{
			logger.LogInformation("Music library categories load started. ParentId={ParentId}, CategoryCount={CategoryCount}, Limit={Limit}, MaxDegreeOfParallelism={MaxDegreeOfParallelism}",
				_parentId,
				CategoryDefinitions.Length,
				AlbumRailLimit,
				MaxConcurrentCategoryLoads);

			Categories.Clear();
			var categories = CategoryDefinitions
				.Select(definition => new MusicCategoryViewModel(
					definition.Title,
					definition.Kind,
					_parentId,
					ShowCategoryCommand,
					definition.ShowWhenEmpty)
				{
					IsLoading = true
				})
				.ToArray();

			foreach (var category in categories)
			{
				Categories.Add(category);
			}

			using var gate = new SemaphoreSlim(MaxConcurrentCategoryLoads);
			var tasks = categories.Select(category => LoadCategory(category, gate, _loadCts.Token)).ToArray();
			await Task.WhenAll(tasks);

			logger.LogInformation("Music library categories load completed. ParentId={ParentId}, VisibleCategoryCount={VisibleCategoryCount}, ElapsedMs={ElapsedMs}", _parentId, Categories.Count, elapsed.ElapsedMilliseconds);
		}
		catch (OperationCanceledException)
		{
			logger.LogInformation("Music library categories load cancelled. ParentId={ParentId}, ElapsedMs={ElapsedMs}", _parentId, elapsed.ElapsedMilliseconds);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Music library categories load failed. ParentId={ParentId}, ElapsedMs={ElapsedMs}", _parentId, elapsed.ElapsedMilliseconds);
		}
		finally
		{
			IsLoading = false;
		}
	}

	private async Task LoadCategory(MusicCategoryViewModel category, SemaphoreSlim gate, CancellationToken cancellationToken)
	{
		var elapsed = Stopwatch.StartNew();

		await gate.WaitAsync(cancellationToken);
		try
		{
			logger.LogInformation("Music category rail load started. Category={Category}, ParentId={ParentId}", category.Kind, _parentId);
			var result = await jellyfinClient.GetItems(CreateCategoryQuery(category.Kind, _parentId, 0, AlbumRailLimit), cancellationToken);
			foreach (var item in result?.Items.Select(BaseItemViewModel.FromDto) ?? [])
			{
				category.Items.Add(item);
			}

			logger.LogInformation("Music category rail load completed. Category={Category}, ParentId={ParentId}, Count={Count}, TotalRecordCount={TotalRecordCount}, ElapsedMs={ElapsedMs}",
				category.Kind,
				_parentId,
				category.Items.Count,
				result?.TotalRecordCount,
				elapsed.ElapsedMilliseconds);
		}
		catch (OperationCanceledException)
		{
			logger.LogInformation("Music category rail load cancelled. Category={Category}, ParentId={ParentId}, ElapsedMs={ElapsedMs}", category.Kind, _parentId, elapsed.ElapsedMilliseconds);
			throw;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Music category rail load failed. Category={Category}, ParentId={ParentId}, ElapsedMs={ElapsedMs}", category.Kind, _parentId, elapsed.ElapsedMilliseconds);
		}
		finally
		{
			category.IsLoading = false;
			if (!category.ShowWhenEmpty && !category.HasItems)
			{
				Categories.Remove(category);
			}

			gate.Release();
		}
	}

	public static ItemQuery CreateRecentlyAddedAlbumsQuery(Guid? parentId, int startIndex, int limit) =>
		CreateCategoryQuery(MusicAlbumCategoryKind.RecentlyAdded, parentId, startIndex, limit);

	public static ItemQuery CreateCategoryQuery(MusicAlbumCategoryKind kind, Guid? parentId, int startIndex, int limit) =>
		kind switch
		{
			MusicAlbumCategoryKind.RecentlyAdded => CreateMusicAlbumQuery(parentId, startIndex, limit, ItemSortBy.DateCreated, SortOrder.Descending),
			MusicAlbumCategoryKind.RecentlyReleasedAlbums => CreateMusicAlbumQuery(parentId, startIndex, limit, ItemSortBy.PremiereDate, SortOrder.Descending),
			MusicAlbumCategoryKind.Playlists => new ItemQuery
			{
				ParentId = parentId,
				Recursive = true,
				StartIndex = startIndex,
				Limit = limit,
				SortBy = ItemSortBy.Name,
				SortOrder = SortOrder.Ascending,
				IncludeItemTypes = [BaseItemKind.Playlist]
			},
			MusicAlbumCategoryKind.RecentlyPlayedSongs => CreateAudioQuery(parentId, startIndex, limit, ItemSortBy.DatePlayed, SortOrder.Descending, isPlayed: true),
			MusicAlbumCategoryKind.RecentlyPlayedAlbums => CreateMusicAlbumQuery(parentId, startIndex, limit, ItemSortBy.DatePlayed, SortOrder.Descending, isPlayed: true),
			MusicAlbumCategoryKind.MostPlayedSongs => CreateAudioQuery(parentId, startIndex, limit, ItemSortBy.PlayCount, SortOrder.Descending, isPlayed: true),
			MusicAlbumCategoryKind.FavoriteAlbums => CreateMusicAlbumQuery(parentId, startIndex, limit, ItemSortBy.Name, SortOrder.Ascending, isFavorite: true),
			MusicAlbumCategoryKind.FavoriteSongs => CreateAudioQuery(parentId, startIndex, limit, ItemSortBy.Name, SortOrder.Ascending, isFavorite: true),
			_ => CreateMusicAlbumQuery(parentId, startIndex, limit, ItemSortBy.Name, SortOrder.Ascending)
		};

	private static ItemQuery CreateMusicAlbumQuery(
		Guid? parentId,
		int startIndex,
		int limit,
		ItemSortBy sortBy,
		SortOrder sortOrder,
		bool? isFavorite = null,
		bool? isPlayed = null) =>
		new()
		{
			ParentId = parentId,
			Recursive = true,
			StartIndex = startIndex,
			Limit = limit,
			SortBy = sortBy,
			SortOrder = sortOrder,
			IncludeItemTypes = [BaseItemKind.MusicAlbum],
			IsFavorite = isFavorite,
			IsPlayed = isPlayed
		};

	private static ItemQuery CreateAudioQuery(
		Guid? parentId,
		int startIndex,
		int limit,
		ItemSortBy sortBy,
		SortOrder sortOrder,
		bool? isFavorite = null,
		bool? isPlayed = null) =>
		new()
		{
			ParentId = parentId,
			Recursive = true,
			StartIndex = startIndex,
			Limit = limit,
			SortBy = sortBy,
			SortOrder = sortOrder,
			IncludeItemTypes = [BaseItemKind.Audio],
			MediaTypes = [MediaType.Audio],
			IsFavorite = isFavorite,
			IsPlayed = isPlayed
		};

	private static readonly MusicCategoryDefinition[] CategoryDefinitions =
	[
		new("Recently added", MusicAlbumCategoryKind.RecentlyAdded, true),
		new("Recently released", MusicAlbumCategoryKind.RecentlyReleasedAlbums),
		new("Playlists", MusicAlbumCategoryKind.Playlists),
		new("Recently played songs", MusicAlbumCategoryKind.RecentlyPlayedSongs),
		new("Recently played albums", MusicAlbumCategoryKind.RecentlyPlayedAlbums),
		new("Most played songs", MusicAlbumCategoryKind.MostPlayedSongs),
		new("Favorite albums", MusicAlbumCategoryKind.FavoriteAlbums),
		new("Favorite songs", MusicAlbumCategoryKind.FavoriteSongs)
	];

	private sealed record MusicCategoryDefinition(string Title, MusicAlbumCategoryKind Kind, bool ShowWhenEmpty = false);
}
