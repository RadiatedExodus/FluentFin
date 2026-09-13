using FluentFin.Core.Contracts.Services;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace FluentFin.Core.Services;

public partial class JellyfinClient
{
	public async Task<BaseItemDtoQueryResult?> GetContinueWatching()
	{
		var elapsed = Stopwatch.StartNew();
		logger.LogInformation("Jellyfin GetContinueWatching started. UserId={UserId}", UserId);
		try
		{
			var response = await _jellyfinApiClient.UserItems.Resume.GetAsync(x =>
			{
				var query = x.QueryParameters;
				query.Limit = 12;
				query.Fields = [ItemFields.PrimaryImageAspectRatio];
				query.ImageTypeLimit = 1;
				query.EnableImageTypes = [ImageType.Primary, ImageType.Backdrop, ImageType.Thumb];
				query.EnableTotalRecordCount = false;
				query.MediaTypes = [MediaType.Video];
			});
			logger.LogInformation("Jellyfin GetContinueWatching completed. Count={Count}, TotalRecordCount={TotalRecordCount}, ElapsedMs={ElapsedMs}",
				response?.Items?.Count ?? 0,
				response?.TotalRecordCount,
				elapsed.ElapsedMilliseconds);
			return response;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Jellyfin GetContinueWatching failed. ElapsedMs={ElapsedMs}", elapsed.ElapsedMilliseconds);
			return null;
		}
	}

	public async Task<BaseItemDtoQueryResult?> GetNextUp()
	{
		var elapsed = Stopwatch.StartNew();
		logger.LogInformation("Jellyfin GetNextUp started. UserId={UserId}", UserId);
		try
		{
			var response = await _jellyfinApiClient.Shows.NextUp.GetAsync(x =>
			{
				var query = x.QueryParameters;
				query.Limit = 24;
				query.Fields = [ItemFields.PrimaryImageAspectRatio, ItemFields.DateCreated, ItemFields.Path, ItemFields.MediaSourceCount];
				query.UserId = UserId;
				query.ImageTypeLimit = 1;
				query.EnableImageTypes = [ImageType.Primary, ImageType.Backdrop, ImageType.Banner, ImageType.Thumb];
				query.EnableTotalRecordCount = false;
				query.DisableFirstEpisode = true;
				query.NextUpDateCutoff = TimeProvider.System.GetUtcNow();
				query.EnableResumable = false;
				query.EnableRewatching = false;
			});
			logger.LogInformation("Jellyfin GetNextUp completed. Count={Count}, TotalRecordCount={TotalRecordCount}, ElapsedMs={ElapsedMs}",
				response?.Items?.Count ?? 0,
				response?.TotalRecordCount,
				elapsed.ElapsedMilliseconds);
			return response;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Jellyfin GetNextUp failed. ElapsedMs={ElapsedMs}", elapsed.ElapsedMilliseconds);
			return null;
		}
	}

	public async IAsyncEnumerable<RecentItemDtoQueryResult> GetRecentItemsFromUserLibraries([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var elapsed = Stopwatch.StartNew();
		logger.LogInformation("Jellyfin GetRecentItemsFromUserLibraries started. UserId={UserId}", UserId);
		BaseItemDtoQueryResult? views = null;
		try
		{
			views = await _jellyfinApiClient.UserViews.GetAsync(x => x.QueryParameters.UserId = UserId, cancellationToken);
			logger.LogInformation("Jellyfin user views loaded for recent items. Count={Count}, ElapsedMs={ElapsedMs}", views?.Items?.Count ?? 0, elapsed.ElapsedMilliseconds);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Jellyfin user views failed for recent items. ElapsedMs={ElapsedMs}", elapsed.ElapsedMilliseconds);
		}

		if (views is null or { Items: null })
		{
			logger.LogInformation("Jellyfin recent items stopped because user views were empty. ResponseIsNull={ResponseIsNull}, ElapsedMs={ElapsedMs}", views is null, elapsed.ElapsedMilliseconds);
			yield break;
		}

		var libraries = views.Items
			.Where(library => library is not null && library.CollectionType is not BaseItemDto_CollectionType.Music)
			.ToList();
		logger.LogInformation("Jellyfin recent items querying libraries. LibraryCount={LibraryCount}, MaxDegreeOfParallelism={MaxDegreeOfParallelism}, ElapsedMs={ElapsedMs}",
			libraries.Count,
			4,
			elapsed.ElapsedMilliseconds);
		var results = new RecentItemDtoQueryResult?[libraries.Count];

		await Parallel.ForEachAsync(libraries.Index(), new ParallelOptions
		{
			MaxDegreeOfParallelism = 4,
			CancellationToken = cancellationToken,
		}, async (entry, ct) =>
		{
			var library = entry.Item;
			List<BaseItemDto>? info = [];
			try
			{
				logger.LogInformation("Jellyfin latest items request started. LibraryId={LibraryId}, LibraryName={LibraryName}, CollectionType={CollectionType}",
					library.Id,
					library.Name,
					library.CollectionType);
				info = await _jellyfinApiClient.Items.Latest.GetAsync(x =>
				{
					var query = x.QueryParameters;
					query.UserId = UserId;
					query.Limit = 16;
					query.ImageTypeLimit = 1;
					query.EnableImageTypes = [ImageType.Primary, ImageType.Backdrop, ImageType.Thumb];
					query.ParentId = library.Id;
				}, ct);
				logger.LogInformation("Jellyfin latest items request completed. LibraryId={LibraryId}, LibraryName={LibraryName}, Count={Count}, ElapsedMs={ElapsedMs}",
					library.Id,
					library.Name,
					info?.Count ?? 0,
					elapsed.ElapsedMilliseconds);
			}
			catch (OperationCanceledException)
			{
				logger.LogInformation("Jellyfin latest items request cancelled. LibraryId={LibraryId}, LibraryName={LibraryName}, ElapsedMs={ElapsedMs}",
					library.Id,
					library.Name,
					elapsed.ElapsedMilliseconds);
				throw;
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Jellyfin latest items request failed. LibraryId={LibraryId}, LibraryName={LibraryName}, ElapsedMs={ElapsedMs}",
					library.Id,
					library.Name,
					elapsed.ElapsedMilliseconds);
			}

			if (info is not null and { Count: > 0 })
			{
				results[entry.Index] = new(library, [.. info]);
			}
		});

		foreach (var result in results)
		{
			if (result is not null)
			{
				yield return result;
			}
		}

		logger.LogInformation("Jellyfin GetRecentItemsFromUserLibraries completed. ResultRowCount={ResultRowCount}, ElapsedMs={ElapsedMs}",
			results.Count(x => x is not null),
			elapsed.ElapsedMilliseconds);
	}

	public async IAsyncEnumerable<BaseItemDto> GetUserLibraries()
	{
		BaseItemDtoQueryResult? views = null;
		try
		{
			views = await _jellyfinApiClient.UserViews.GetAsync(x => x.QueryParameters.UserId = UserId);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, @"Unhandled exception");
		}

		if (views is null or { Items: null })
		{
			yield break;
		}

		foreach (var item in views.Items)
		{
			yield return item;
		}
	}

	public async Task SetIsFavorite(BaseItemDto dto, bool isFavorite)
	{
		try
		{
			if (dto.Id is null)
			{
				return;
			}

			if (isFavorite)
			{
				await _jellyfinApiClient.UserFavoriteItems[dto.Id.Value].PostAsync();
			}
			else
			{
				await _jellyfinApiClient.UserFavoriteItems[dto.Id.Value].DeleteAsync();
			}
		}
		catch (Exception ex)
		{
			logger.LogError(ex, @"Unhandled exception");
			return;
		}
	}

	public async Task SetPlayed(BaseItemDto dto, bool played)
	{
		try
		{
			if (dto.Id is null)
			{
				return;
			}

			if (played)
			{
				await _jellyfinApiClient.UserPlayedItems[dto.Id.Value].PostAsync(x => x.QueryParameters.DatePlayed = TimeProvider.System.GetUtcNow());
			}
			else
			{
				await _jellyfinApiClient.UserPlayedItems[dto.Id.Value].DeleteAsync();
			}
		}
		catch (Exception ex)
		{
			logger.LogError(ex, @"Unhandled exception");
			return;
		}
	}

	public async Task<List<UserDto>> GetUsers()
	{
		try
		{
			return await _jellyfinApiClient.Users.GetAsync() ?? [];
		}
		catch (Exception ex)
		{
			logger.LogError(ex, @"Unhandled exception");
			return [];
		}
	}

	public async Task<UserDto?> GetUser(Guid id)
	{
		try
		{
			return await _jellyfinApiClient.Users[id].GetAsync();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, @"Unhandled exception");
			return null;
		}
	}

	public async Task DeleteUser(UserDto user)
	{
		if (user.Id is not { } id)
		{
			return;
		}

		try
		{
			await _jellyfinApiClient.Users[id].DeleteAsync();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, @"Unhandled exception");
			return;
		}
	}

	public async Task UpdatePolicy(UserDto user, UserPolicy policy)
	{
		if (user.Id is not { } id)
		{
			return;
		}

		try
		{
			await _jellyfinApiClient.Users[id].Policy.PostAsync(policy);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, @"Unhandled exception");
			return;
		}
	}

	public async Task ChangePassword(UserDto user, string currentPassword, string newPassword)
	{
		if (user.Id is not { } id)
		{
			return;
		}

		try
		{
			await _jellyfinApiClient.Users.Password.PostAsync(new UpdateUserPassword
			{
				CurrentPassword = currentPassword,
				NewPw = newPassword,
			}, x => x.QueryParameters.UserId = id);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, @"Unhandled exception");
			return;
		}
	}

	public async Task ResetPassword(UserDto user)
	{
		if (user.Id is not { } id)
		{
			return;
		}

		try
		{
			await _jellyfinApiClient.Users.Password.PostAsync(new UpdateUserPassword
			{
				ResetPassword = true,
			}, x => x.QueryParameters.UserId = id);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, @"Unhandled exception");
			return;
		}
	}

	public async Task<bool> Authenticate(string code)
	{
		try
		{
			return await _jellyfinApiClient.QuickConnect.Authorize.PostAsync(x =>
			{
				var query = x.QueryParameters;
				query.UserId = UserId;
				query.Code = code;
			}) ?? false;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, @"Unhandled exception");
			return false;
		}
	}
}
