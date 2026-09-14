using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using FluentFin.Contracts.ViewModels;
using FluentFin.Core.Contracts.Services;
using FluentFin.Core.Services;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Extensions.Logging;

namespace FluentFin.Core.ViewModels;

public partial class LibraryViewModel : ObservableObject, INavigationAware
{
	private const int PageSize = 100;
	private BaseItemDto? _library;
	private CancellationTokenSource? _loadCts;
	private readonly ILogger<LibraryViewModel> _logger;
	private readonly INavigationServiceCore _navigationService;

	public LibraryViewModel(IJellyfinClient jellyfinClient, INavigationServiceCore navigationService, ILogger<LibraryViewModel> logger)
	{
		JellyfinClient = jellyfinClient;
		_navigationService = navigationService;
		_logger = logger;

		Filter.Changed += (_, _) => ReloadFromFirstPage();
	}

	public ObservableCollection<BaseItemViewModel> Items { get; } = [];

	[ObservableProperty]
	public partial int NumberOfPages { get; set; }

	[ObservableProperty]
	public partial int SelectedPage { get; set; }

	[ObservableProperty]
	public partial ItemSortBy SortBy { get; set; } = ItemSortBy.Name;

	[ObservableProperty]
	public partial SortOrder Order { get; set; } = SortOrder.Ascending;

	[ObservableProperty]
	public partial List<string> TagsSource { get; set; } = new();

	[ObservableProperty]
	public partial List<string> GenresSource { get; set; } = new();

	[ObservableProperty]
	public partial List<string> OfficialRatingsSource { get; set; } = new();

	[ObservableProperty]
	public partial List<string> YearsSource { get; set; } = new();

	[ObservableProperty]
	public partial bool IsLoading { get; set; }

	public IJellyfinClient JellyfinClient { get; }

	public LibraryFilter Filter { get; set; } = new();

	[RelayCommand]
	public void UpdateSortBy(ItemSortBy sortBy)
	{
		_logger.LogInformation("Library sort-by changed. SortBy={SortBy}", sortBy);
		SortBy = sortBy;
		ReloadFromFirstPage();
	}

	[RelayCommand]
	public void UpdateSortOrder(SortOrder order)
	{
		_logger.LogInformation("Library sort-order changed. SortOrder={SortOrder}", order);
		Order = order;
		ReloadFromFirstPage();
	}


	public Task OnNavigatedFrom()
	{
		_logger.LogInformation("LibraryViewModel navigated from. Cancelling active load. LibraryId={LibraryId}, LibraryName={LibraryName}", _library?.Id, _library?.Name);
		_loadCts?.Cancel();
		_loadCts?.Dispose();
		_loadCts = null;
		return Task.CompletedTask;
	}

	public async Task OnNavigatedTo(object parameter)
	{
		_logger.LogInformation("LibraryViewModel OnNavigatedTo started. ParameterType={ParameterType}", parameter?.GetType().FullName ?? "<null>");
		if (parameter is BaseItemDto libraryDto)
		{
			if (RedirectMusicLibrary(libraryDto))
			{
				return;
			}

			await Initialize(libraryDto);
		}
		else if(parameter is Guid id)
		{
			var dto = await JellyfinClient.GetItem(id);
			if(dto is null)
			{
				_logger.LogWarning("LibraryViewModel could not resolve library id. LibraryId={LibraryId}", id);
				return;
			}

			if (RedirectMusicLibrary(dto))
			{
				return;
			}

			await Initialize(dto);
		}
		else
		{
			_logger.LogWarning("LibraryViewModel received unsupported navigation parameter. ParameterType={ParameterType}", parameter?.GetType().FullName ?? "<null>");
		}
	}

	private bool RedirectMusicLibrary(BaseItemDto dto)
	{
		if (dto.Type is BaseItemDto_Type.CollectionFolder && dto.CollectionType is BaseItemDto_CollectionType.Music)
		{
			_logger.LogInformation("Redirecting music library from generic library page to album list. LibraryId={LibraryId}, LibraryName={LibraryName}", dto.Id, dto.Name);
			_navigationService.NavigateTo<MusicAlbumListViewModel>(GlobalCommands.CreateMusicLibraryAlbumListParameter(dto), true);
			return true;
		}

		return false;
	}

	private async Task Initialize(BaseItemDto dto)
	{
		var elapsed = Stopwatch.StartNew();
		_logger.LogInformation("Library initialization started. LibraryId={LibraryId}, LibraryName={LibraryName}, CollectionType={CollectionType}",
			dto.Id,
			dto.Name,
			dto.CollectionType);
		_library = dto;
		IsLoading = true;

		try
		{
			var cts = StartNewLoad();
			var itemsTask = LoadPage(cts.Token);
			var filtersTask = JellyfinClient.GetFilters(dto);

			await itemsTask;
			_logger.LogInformation("Library initial page loaded. LibraryId={LibraryId}, ItemCount={ItemCount}, NumberOfPages={NumberOfPages}, ElapsedMs={ElapsedMs}",
				dto.Id,
				Items.Count,
				NumberOfPages,
				elapsed.ElapsedMilliseconds);

			var filters = await filtersTask;

			if (filters is null)
			{
				_logger.LogWarning("Library filters returned null. LibraryId={LibraryId}, ElapsedMs={ElapsedMs}", dto.Id, elapsed.ElapsedMilliseconds);
				return;
			}

			TagsSource = filters.Tags?.ToList() ?? [];
			GenresSource = filters.Genres?.ToList() ?? [];
			OfficialRatingsSource = filters.OfficialRatings?.ToList() ?? [];
			YearsSource = filters.Years?.Where(x => x.HasValue).Select(x => x!.Value.ToString()).ToList() ?? [];
			_logger.LogInformation("Library filters loaded. LibraryId={LibraryId}, Tags={Tags}, Genres={Genres}, OfficialRatings={OfficialRatings}, Years={Years}, ElapsedMs={ElapsedMs}",
				dto.Id,
				TagsSource.Count,
				GenresSource.Count,
				OfficialRatingsSource.Count,
				YearsSource.Count,
				elapsed.ElapsedMilliseconds);
		}
		catch (OperationCanceledException)
		{
			_logger.LogInformation("Library initialization cancelled. LibraryId={LibraryId}, ElapsedMs={ElapsedMs}", dto.Id, elapsed.ElapsedMilliseconds);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Library initialization failed. LibraryId={LibraryId}, ElapsedMs={ElapsedMs}", dto.Id, elapsed.ElapsedMilliseconds);
		}
		finally
		{
			IsLoading = false;
			_logger.LogInformation("Library initialization completed. LibraryId={LibraryId}, IsLoading={IsLoading}, ElapsedMs={ElapsedMs}", dto.Id, IsLoading, elapsed.ElapsedMilliseconds);
		}
	}

	private CancellationTokenSource StartNewLoad()
	{
		_loadCts?.Cancel();
		_loadCts?.Dispose();
		_loadCts = new CancellationTokenSource();
		return _loadCts;
	}

	private void ReloadFromFirstPage()
	{
		if (_library is null)
		{
			_logger.LogInformation("Library reload skipped because no library is loaded");
			return;
		}

		_logger.LogInformation("Library reload requested. LibraryId={LibraryId}, LibraryName={LibraryName}, SelectedPage={SelectedPage}, SortBy={SortBy}, SortOrder={SortOrder}, TagFilters={TagFilters}, GenreFilters={GenreFilters}, RatingFilters={RatingFilters}, YearFilters={YearFilters}",
			_library.Id,
			_library.Name,
			SelectedPage,
			SortBy,
			Order,
			Filter.Tags.Count,
			Filter.Genres.Count,
			Filter.OfficialRatings.Count,
			Filter.Years.Count);

		if (SelectedPage == 0)
		{
			_ = LoadPageWithLoading();
		}
		else
		{
			SelectedPage = 0;
		}
	}

	partial void OnSelectedPageChanged(int value)
	{
		if (_library is not null)
		{
			_logger.LogInformation("Library selected page changed. LibraryId={LibraryId}, SelectedPage={SelectedPage}", _library.Id, value);
			_ = LoadPageWithLoading();
		}
	}

	private async Task LoadPageWithLoading()
	{
		IsLoading = true;
		try
		{
			var cts = StartNewLoad();
			await LoadPage(cts.Token);
		}
		catch (OperationCanceledException)
		{
			_logger.LogInformation("Library page load cancelled. LibraryId={LibraryId}, SelectedPage={SelectedPage}", _library?.Id, SelectedPage);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Library page load failed. LibraryId={LibraryId}, SelectedPage={SelectedPage}", _library?.Id, SelectedPage);
		}
		finally
		{
			IsLoading = false;
		}
	}

	private async Task LoadPage(CancellationToken cancellationToken)
	{
		if (_library?.Id is not { } parentId)
		{
			_logger.LogWarning("Library page load skipped because library id is missing");
			return;
		}

		var query = new ItemQuery
		{
			ParentId = parentId,
			StartIndex = SelectedPage * PageSize,
			Limit = PageSize,
			SortBy = SortBy,
			SortOrder = Order,
			IncludeItemTypes = GetIncludeItemTypes(_library),
			Genres = [.. Filter.Genres],
			Tags = [.. Filter.Tags],
			OfficialRatings = [.. Filter.OfficialRatings],
			Years = [.. Filter.Years.Select(x => int.TryParse(x, out var year) ? year : (int?)null).Where(x => x.HasValue).Select(x => x!.Value)],
		};

		var elapsed = Stopwatch.StartNew();
		_logger.LogInformation("Library page load started. LibraryId={LibraryId}, LibraryName={LibraryName}, StartIndex={StartIndex}, Limit={Limit}, SortBy={SortBy}, SortOrder={SortOrder}, IncludeItemTypes={IncludeItemTypes}, Genres={Genres}, Tags={Tags}, OfficialRatings={OfficialRatings}, Years={Years}",
			_library.Id,
			_library.Name,
			query.StartIndex,
			query.Limit,
			query.SortBy,
			query.SortOrder,
			string.Join(",", query.IncludeItemTypes),
			string.Join(",", query.Genres),
			string.Join(",", query.Tags),
			string.Join(",", query.OfficialRatings),
			string.Join(",", query.Years));

		var result = await JellyfinClient.GetItems(query, cancellationToken);

		if (result is null)
		{
			_logger.LogWarning("Library page load returned null. LibraryId={LibraryId}, StartIndex={StartIndex}, ElapsedMs={ElapsedMs}", _library.Id, query.StartIndex, elapsed.ElapsedMilliseconds);
			return;
		}

		Items.Clear();
		foreach (var item in result.Items.Select(BaseItemViewModel.FromDto))
		{
			Items.Add(item);
		}

		NumberOfPages = Math.Max(1, (int)Math.Ceiling(result.TotalRecordCount / (double)PageSize));
		_logger.LogInformation("Library page load completed. LibraryId={LibraryId}, StartIndex={StartIndex}, ItemCount={ItemCount}, TotalRecordCount={TotalRecordCount}, NumberOfPages={NumberOfPages}, ElapsedMs={ElapsedMs}",
			_library.Id,
			result.StartIndex,
			result.Items.Count,
			result.TotalRecordCount,
			NumberOfPages,
			elapsed.ElapsedMilliseconds);
	}

	private static IReadOnlyList<BaseItemKind> GetIncludeItemTypes(BaseItemDto parent) =>
		parent.CollectionType switch
		{
			BaseItemDto_CollectionType.Movies => [BaseItemKind.Movie],
			BaseItemDto_CollectionType.Tvshows => [BaseItemKind.Series],
			_ => []
		};
}

public partial class LibraryFilter : ObservableObject
{
	public event EventHandler? Changed;

	public LibraryFilter()
	{
		Tags.CollectionChanged += (_, _) => NotifyChanged(nameof(Tags));
		Genres.CollectionChanged += (_, _) => NotifyChanged(nameof(Genres));
		OfficialRatings.CollectionChanged += (_, _) => NotifyChanged(nameof(OfficialRatings));
		Years.CollectionChanged += (_, _) => NotifyChanged(nameof(Years));
	}

	private void NotifyChanged(string propertyName)
	{
		OnPropertyChanged(propertyName);
		Changed?.Invoke(this, EventArgs.Empty);
	}

	public ObservableCollection<string> Tags { get; set; } = [];

	[ObservableProperty]
	public partial ObservableCollection<string> Genres { get; set; } = new();

	[ObservableProperty]
	public partial ObservableCollection<string> OfficialRatings { get; set; } = new();

	[ObservableProperty]
	public partial ObservableCollection<string> Years { get; set; } = new();

	public bool IsEmptyFilter() => this is { Tags.Count: 0, Genres.Count: 0, OfficialRatings.Count: 0, Years.Count: 0 };

	public bool IsVisible(BaseItemViewModel dto)
	{
		bool hasMatchingTags = true;
		bool hasMatchingGenres = true;
		bool hasMatchingOfficialRatings = true;
		bool hasMatchingYears = true;

		if (Tags is { Count: > 0 })
		{
			hasMatchingTags = dto.Tags?.Intersect(Tags).Count() == Tags.Count;
		}

		if (Genres is { Count: > 0 })
		{
			hasMatchingGenres = dto.Genres?.Intersect(Genres).Count() == Genres.Count;
		}

		if (OfficialRatings is { Count: > 0 })
		{
			hasMatchingOfficialRatings = OfficialRatings.Contains(dto.OfficialRating ?? "");
		}

		if (dto.ProductionYear is > 0 && Years is { Count: > 0 })
		{
			hasMatchingYears = Years.Contains(dto.ProductionYear.Value.ToString());
		}

		return hasMatchingTags && hasMatchingGenres && hasMatchingOfficialRatings && hasMatchingYears;
	}
}
