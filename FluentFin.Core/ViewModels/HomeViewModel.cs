using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Diagnostics;
using System.Reactive.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentFin.Contracts.ViewModels;
using FluentFin.Core.Contracts.Services;
using FluentFin.Core.WebSockets;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace FluentFin.Core.ViewModels;

public partial class HomeViewModel(IJellyfinClient jellyfinClient,
								   IObservable<IInboundSocketMessage> webSocketMessages,
								   GlobalCommands commands,
								   ILogger<HomeViewModel> logger) : ObservableObject, INavigationAware
{
	private readonly CompositeDisposable _disposable = [];
	private CancellationTokenSource? _loadCts;


	[ObservableProperty]
	public partial ObservableCollection<BaseItemViewModel> ContinueItems { get; set; } = [];

	[ObservableProperty]
	public partial ObservableCollection<BaseItemViewModel> NextUpItems { get; set; } = [];

	[ObservableProperty]
	public partial bool HasNextUpItems { get; set; }

	[ObservableProperty]
	public partial bool HasContinueItems { get; set; }

	[ObservableProperty]
	public partial bool IsLoading { get; set; }

	public ObservableCollection<NamedQueryResult> RecentItems { get; } = [];

	public IJellyfinClient JellyfinClient { get; } = jellyfinClient;

	public Task OnNavigatedFrom()
	{
		logger.LogInformation("HomeViewModel navigated from. Cancelling active home load.");
		_loadCts?.Cancel();
		_loadCts?.Dispose();
		_loadCts = null;
		_disposable.Dispose();
		return Task.CompletedTask;
	}

	public async Task OnNavigatedTo(object parameter)
	{
		var elapsed = Stopwatch.StartNew();
		logger.LogInformation("HomeViewModel OnNavigatedTo started. ParameterType={ParameterType}", parameter?.GetType().FullName ?? "<null>");

		_loadCts?.Cancel();
		_loadCts?.Dispose();
		_loadCts = new CancellationTokenSource();
		var cancellationToken = _loadCts.Token;

		IsLoading = true;

		try
		{
			await Task.WhenAll(UpdateContinueItems(), UpdateNextUpItems());
			logger.LogInformation("Home primary rows loaded. ContinueCount={ContinueCount}, NextUpCount={NextUpCount}, ElapsedMs={ElapsedMs}",
				ContinueItems.Count,
				NextUpItems.Count,
				elapsed.ElapsedMilliseconds);
		}
		catch (OperationCanceledException)
		{
			logger.LogInformation("Home primary row load cancelled. ElapsedMs={ElapsedMs}", elapsed.ElapsedMilliseconds);
			return;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Home primary row load failed. ElapsedMs={ElapsedMs}", elapsed.ElapsedMilliseconds);
			return;
		}
		finally
		{
			IsLoading = false;
			logger.LogInformation("Home IsLoading reset. ElapsedMs={ElapsedMs}", elapsed.ElapsedMilliseconds);
		}

		HasContinueItems = ContinueItems.Count > 0;
		HasNextUpItems = NextUpItems.Count > 0;
		ContinueItems.CollectionChanged += (_, _) => HasContinueItems = ContinueItems.Count > 0;
		NextUpItems.CollectionChanged += (_, _) => HasNextUpItems = NextUpItems.Count > 0;

		webSocketMessages
			.Select(x => x as UserDataChangeMessage)
			.WhereNotNull()
			.ObserveOn(RxApp.MainThreadScheduler)
			.Subscribe(async msg =>
			{
				foreach (var item in msg.Data?.UserDataList ?? [])
				{
					if (item.ItemId is not { } guid)
					{
						continue;
					}

					ProcessContinueWatchingItemChanged(item, guid);
					await ProcessNextUpItemChanged(item, guid);
					ProcessRecentItemChanged(item, guid);
				}
			})
			.DisposeWith(_disposable);

		_ = UpdateRecentItems(cancellationToken);
		logger.LogInformation("HomeViewModel OnNavigatedTo completed. Recent items continue in background. ElapsedMs={ElapsedMs}", elapsed.ElapsedMilliseconds);
	}

	private async Task UpdateContinueItems()
	{
		var elapsed = Stopwatch.StartNew();
		logger.LogInformation("Loading Continue Watching row");
		var response = await JellyfinClient.GetContinueWatching();

		if (response is null or { Items: null } or { Items.Count: 0 })
		{
			logger.LogInformation("Continue Watching returned no items. ResponseIsNull={ResponseIsNull}, ElapsedMs={ElapsedMs}", response is null, elapsed.ElapsedMilliseconds);
			return;
		}

		ContinueItems = [.. response.Items.Select(BaseItemViewModel.FromDto)];
		logger.LogInformation("Continue Watching loaded. Count={Count}, ElapsedMs={ElapsedMs}", ContinueItems.Count, elapsed.ElapsedMilliseconds);
	}

	private async Task UpdateNextUpItems()
	{
		var elapsed = Stopwatch.StartNew();
		logger.LogInformation("Loading Next Up row");
		var response = await JellyfinClient.GetNextUp();

		if (response is null or { Items: null } or { Items.Count: 0 })
		{
			logger.LogInformation("Next Up returned no items. ResponseIsNull={ResponseIsNull}, ElapsedMs={ElapsedMs}", response is null, elapsed.ElapsedMilliseconds);
			return;
		}

		NextUpItems = [.. response.Items.Select(BaseItemViewModel.FromDto)];
		logger.LogInformation("Next Up loaded. Count={Count}, ElapsedMs={ElapsedMs}", NextUpItems.Count, elapsed.ElapsedMilliseconds);
	}

	private async Task UpdateRecentItems(CancellationToken cancellationToken)
	{
		var elapsed = Stopwatch.StartNew();
		var rowCount = 0;
		logger.LogInformation("Loading recent items rows in background");
		try
		{
			await foreach (var list in JellyfinClient.GetRecentItemsFromUserLibraries(cancellationToken))
			{
				cancellationToken.ThrowIfCancellationRequested();
				RecentItems.Add(new(list.Library, [.. list.Items.Select(BaseItemViewModel.FromDto)]));
				rowCount++;
				logger.LogInformation("Recent items row added. LibraryId={LibraryId}, LibraryName={LibraryName}, ItemCount={ItemCount}, RowCount={RowCount}, ElapsedMs={ElapsedMs}",
					list.Library.Id,
					list.Library.Name,
					list.Items.Count,
					rowCount,
					elapsed.ElapsedMilliseconds);
			}

			logger.LogInformation("Recent items background load completed. RowCount={RowCount}, ElapsedMs={ElapsedMs}", rowCount, elapsed.ElapsedMilliseconds);
		}
		catch (OperationCanceledException)
		{
			logger.LogInformation("Recent items background load cancelled. RowCount={RowCount}, ElapsedMs={ElapsedMs}", rowCount, elapsed.ElapsedMilliseconds);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Recent items background load failed. RowCount={RowCount}, ElapsedMs={ElapsedMs}", rowCount, elapsed.ElapsedMilliseconds);
		}
	}

	private void ProcessRecentItemChanged(UserItemDataDto userData, Guid guid)
	{
		foreach (var library in RecentItems)
		{
			if (library.Items.FirstOrDefault(x => x.Id == guid) is not BaseItemViewModel { UserData: not null } dto)
			{
				continue;
			}

			dto.UserData.IsFavorite = userData.IsFavorite;
			dto.UserData.Played = userData.Played;
			dto.UserData.PlayedPercentage = userData.PlayedPercentage;
			dto.UserData.UnplayedItemCount = userData.UnplayedItemCount;
		}
		;
	}

	private void ProcessContinueWatchingItemChanged(UserItemDataDto userData, Guid guid)
	{
		if (ContinueItems.FirstOrDefault(x => x.Id == guid) is not { } item)
		{
			return;
		}

		if (userData.PlaybackPositionTicks is null or 0 ||
			userData.Played == true)
		{
			ContinueItems.Remove(item);
		}
		else
		{
			item.UserData!.PlayedPercentage = userData.PlayedPercentage;
		}
	}

	private async Task ProcessNextUpItemChanged(UserItemDataDto userData, Guid guid)
	{
		if (NextUpItems.FirstOrDefault(x => x.Id == guid) is not { } item)
		{
			return;
		}

		if (userData.Played == true)
		{
			await UpdateNextUpItems();
		}
	}

	[RelayCommand]
	private async Task Continue() => await PlayFirstItem(ContinueItems);

	[RelayCommand]
	private async Task NextUp() => await PlayFirstItem(NextUpItems);

	private Task PlayFirstItem(ObservableCollection<BaseItemViewModel> items)
	{
		if (items.Count == 0)
		{
			return Task.CompletedTask;
		}

		return commands.PlayDto(items.First().Dto);
	}
}

public record NamedQueryResult(BaseItemDto Library, ObservableCollection<BaseItemViewModel> Items);
