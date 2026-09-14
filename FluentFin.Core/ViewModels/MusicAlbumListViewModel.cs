using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentFin.Contracts.ViewModels;
using FluentFin.Core.Contracts.Services;
using FluentFin.Core.Services;
using Microsoft.Extensions.Logging;
using Jellyfin.Sdk.Generated.Models;

namespace FluentFin.Core.ViewModels;

public partial class MusicAlbumListViewModel(
	IJellyfinClient jellyfinClient,
	INavigationServiceCore navigationService,
	ILogger<MusicAlbumListViewModel> logger) : ObservableObject, INavigationAware
{
	private const int PageSize = 100;
	private CancellationTokenSource? _loadCts;
	private MusicAlbumCategoryListParameter _parameter = new("Albums", MusicAlbumCategoryKind.LibraryAlbums, null);

	public ObservableCollection<BaseItemViewModel> Albums { get; } = [];

	[ObservableProperty]
	public partial string Title { get; set; } = "Albums";

	[ObservableProperty]
	public partial int NumberOfPages { get; set; } = 1;

	[ObservableProperty]
	public partial int SelectedPage { get; set; }

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
		_parameter = parameter switch
		{
			MusicAlbumCategoryListParameter categoryParameter => categoryParameter,
			BaseItemDto { Id: { } id, Name: not null } library => new MusicAlbumCategoryListParameter(library.Name!, MusicAlbumCategoryKind.LibraryAlbums, id),
			Guid id => await CreateLibraryAlbumParameter(id),
			_ => new MusicAlbumCategoryListParameter("Albums", MusicAlbumCategoryKind.LibraryAlbums, null)
		};
		Title = _parameter.Title;
		SelectedPage = 0;
		await LoadPage();
	}

	private async Task<MusicAlbumCategoryListParameter> CreateLibraryAlbumParameter(Guid id)
	{
		try
		{
			var library = await jellyfinClient.GetItem(id);
			return new MusicAlbumCategoryListParameter(library?.Name ?? "Albums", MusicAlbumCategoryKind.LibraryAlbums, id);
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Music album list could not resolve library title. LibraryId={LibraryId}", id);
			return new MusicAlbumCategoryListParameter("Albums", MusicAlbumCategoryKind.LibraryAlbums, id);
		}
	}

	[RelayCommand]
	private void OpenAlbum(BaseItemDto album)
	{
		logger.LogInformation("Music album list navigation requested. AlbumId={AlbumId}, AlbumName={AlbumName}", album.Id, album.Name);
		navigationService.NavigateTo<MusicAlbumViewModel>(album);
	}

	partial void OnSelectedPageChanged(int value)
	{
		if (_parameter is not null)
		{
			_ = LoadPage();
		}
	}

	private async Task LoadPage()
	{
		var elapsed = Stopwatch.StartNew();
		IsLoading = true;
		_loadCts?.Cancel();
		_loadCts?.Dispose();
		_loadCts = new CancellationTokenSource();

		try
		{
			var startIndex = SelectedPage * PageSize;
			var query = CreateAlbumListQuery(_parameter, startIndex, PageSize);
			logger.LogInformation("Music album category page load started. Category={Category}, ParentId={ParentId}, SelectedPage={SelectedPage}, StartIndex={StartIndex}",
				_parameter.Kind,
				_parameter.ParentId,
				SelectedPage,
				query.StartIndex);
			var result = _parameter.Kind is MusicAlbumCategoryKind.ArtistAlbums && _parameter.ArtistId is { } artistId
				? await GetArtistAlbums(jellyfinClient, artistId, _parameter.ParentId, startIndex, PageSize, _loadCts.Token)
				: await jellyfinClient.GetItems(query, _loadCts.Token);

			Albums.Clear();
			foreach (var item in result?.Items.Select(BaseItemViewModel.FromDto) ?? [])
			{
				Albums.Add(item);
			}

			NumberOfPages = Math.Max(1, (int)Math.Ceiling((result?.TotalRecordCount ?? 0) / (double)PageSize));
			logger.LogInformation("Music album category page load completed. Category={Category}, ParentId={ParentId}, Count={Count}, TotalRecordCount={TotalRecordCount}, NumberOfPages={NumberOfPages}, ElapsedMs={ElapsedMs}",
				_parameter.Kind,
				_parameter.ParentId,
				Albums.Count,
				result?.TotalRecordCount,
				NumberOfPages,
				elapsed.ElapsedMilliseconds);
		}
		catch (OperationCanceledException)
		{
			logger.LogInformation("Music album category page load cancelled. Category={Category}, ParentId={ParentId}, SelectedPage={SelectedPage}, ElapsedMs={ElapsedMs}",
				_parameter.Kind,
				_parameter.ParentId,
				SelectedPage,
				elapsed.ElapsedMilliseconds);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Music album category page load failed. Category={Category}, ParentId={ParentId}, SelectedPage={SelectedPage}, ElapsedMs={ElapsedMs}",
				_parameter.Kind,
				_parameter.ParentId,
				SelectedPage,
				elapsed.ElapsedMilliseconds);
		}
		finally
		{
			IsLoading = false;
		}
	}

	private static ItemQuery CreateAlbumListQuery(MusicAlbumCategoryListParameter parameter, int startIndex, int limit) =>
		parameter.Kind switch
		{
			MusicAlbumCategoryKind.RecentlyAdded
				or MusicAlbumCategoryKind.RecentlyReleasedAlbums
				or MusicAlbumCategoryKind.Playlists
				or MusicAlbumCategoryKind.RecentlyPlayedSongs
				or MusicAlbumCategoryKind.RecentlyPlayedAlbums
				or MusicAlbumCategoryKind.MostPlayedSongs
				or MusicAlbumCategoryKind.FavoriteAlbums
				or MusicAlbumCategoryKind.FavoriteSongs => MusicLibraryViewModel.CreateCategoryQuery(parameter.Kind, parameter.ParentId, startIndex, limit),
			MusicAlbumCategoryKind.ArtistAlbums => CreateArtistAlbumsQuery(parameter.ParentId, parameter.ArtistId, startIndex, limit, useAlbumArtistIds: true),
			_ => new ItemQuery
			{
				ParentId = parameter.ParentId,
				Recursive = true,
				StartIndex = startIndex,
				Limit = limit,
				SortBy = ItemSortBy.Name,
				SortOrder = SortOrder.Ascending,
				IncludeItemTypes = [BaseItemKind.MusicAlbum]
			}
		};

	public static async Task<PagedResult<BaseItemDto>?> GetArtistAlbums(
		IJellyfinClient jellyfinClient,
		Guid artistId,
		Guid? parentId,
		int startIndex,
		int limit,
		CancellationToken cancellationToken)
	{
		var requestedCount = startIndex + limit;
		var albumArtistQuery = CreateArtistAlbumsQuery(parentId, artistId, 0, requestedCount, useAlbumArtistIds: true);
		var artistQuery = CreateArtistAlbumsQuery(parentId, artistId, 0, requestedCount, useAlbumArtistIds: false);
		var albumArtistTask = jellyfinClient.GetItems(albumArtistQuery, cancellationToken);
		var artistTask = jellyfinClient.GetItems(artistQuery, cancellationToken);
		await Task.WhenAll(albumArtistTask, artistTask);

		var albumArtistResult = await albumArtistTask;
		var artistResult = await artistTask;
		var merged = MergeArtistAlbumResults(albumArtistResult, artistResult);
		var items = merged
			.OrderBy(GetSortName, StringComparer.CurrentCultureIgnoreCase)
			.Skip(startIndex)
			.Take(limit)
			.ToList();

		var fetchedCount = merged.Count;
		var requestedWindowComplete = fetchedCount < requestedCount;
		var estimatedTotal = requestedWindowComplete
			? fetchedCount
			: Math.Max(fetchedCount, (albumArtistResult?.TotalRecordCount ?? 0) + (artistResult?.TotalRecordCount ?? 0) - CountOverlappingFetchedAlbums(albumArtistResult, artistResult));

		return new PagedResult<BaseItemDto>
		{
			Items = items,
			StartIndex = startIndex,
			TotalRecordCount = estimatedTotal
		};
	}

	private static ItemQuery CreateArtistAlbumsQuery(Guid? parentId, Guid? artistId, int startIndex, int limit, bool useAlbumArtistIds) =>
		new()
		{
			ParentId = parentId,
			Recursive = true,
			StartIndex = startIndex,
			Limit = limit,
			SortBy = ItemSortBy.Name,
			SortOrder = SortOrder.Ascending,
			IncludeItemTypes = [BaseItemKind.MusicAlbum],
			AlbumArtistIds = useAlbumArtistIds && artistId is { } albumArtistId ? [albumArtistId] : [],
			ArtistIds = !useAlbumArtistIds && artistId is { } trackArtistId ? [trackArtistId] : []
		};

	private static List<BaseItemDto> MergeArtistAlbumResults(params PagedResult<BaseItemDto>?[] results)
	{
		Dictionary<Guid, BaseItemDto> albums = [];
		foreach (var item in results.SelectMany(x => x?.Items ?? []))
		{
			if (item.Id is { } id)
			{
				albums.TryAdd(id, item);
			}
		}

		return [.. albums.Values];
	}

	private static int CountOverlappingFetchedAlbums(PagedResult<BaseItemDto>? first, PagedResult<BaseItemDto>? second)
	{
		var firstIds = first?.Items.Select(x => x.Id).Where(x => x is not null).ToHashSet() ?? [];
		return second?.Items.Count(x => x.Id is not null && firstIds.Contains(x.Id)) ?? 0;
	}

	private static string GetSortName(BaseItemDto item) =>
		item.SortName ?? item.Name ?? "";
}
