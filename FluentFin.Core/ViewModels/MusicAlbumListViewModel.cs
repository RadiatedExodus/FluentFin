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
	private MusicAlbumCategoryListParameter _parameter = new("Albums", MusicAlbumCategoryKind.RecentlyAdded, null);

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
		_parameter = parameter as MusicAlbumCategoryListParameter ?? new("Recently added", MusicAlbumCategoryKind.RecentlyAdded, null);
		Title = _parameter.Title;
		SelectedPage = 0;
		await LoadPage();
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
			var query = MusicLibraryViewModel.CreateRecentlyAddedAlbumsQuery(_parameter.ParentId, SelectedPage * PageSize, PageSize);
			logger.LogInformation("Music album category page load started. Category={Category}, ParentId={ParentId}, SelectedPage={SelectedPage}, StartIndex={StartIndex}",
				_parameter.Kind,
				_parameter.ParentId,
				SelectedPage,
				query.StartIndex);
			var result = await jellyfinClient.GetItems(query, _loadCts.Token);

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
}
