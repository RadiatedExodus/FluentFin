using Flurl.Http;
using Jellyfin.Sdk.Generated.Library.VirtualFolders;
using System.Diagnostics;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Extensions.Logging;

namespace FluentFin.Core.Services;

public partial class JellyfinClient
{
	public async Task<BaseItemDtoQueryResult?> GetItems(BaseItemDto parent, bool recursive = false)
	{
		try
		{
			return await _jellyfinApiClient.Items.GetAsync(x =>
			{
				var query = x.QueryParameters;
				query.SortBy = [ItemSortBy.SortName];
				query.SortOrder = [SortOrder.Ascending];
				query.Recursive = recursive;
				query.Fields = [ItemFields.PrimaryImageAspectRatio, ItemFields.DateCreated, ItemFields.Overview, ItemFields.Tags, ItemFields.Genres];
				query.ImageTypeLimit = 1;
				query.EnableImageTypes = [ImageType.Primary, ImageType.Backdrop, ImageType.Banner, ImageType.Thumb];
				query.ParentId = parent.Id;
				query.IncludeItemTypes = parent.CollectionType switch
				{
					BaseItemDto_CollectionType.Movies => [BaseItemKind.Movie],
					BaseItemDto_CollectionType.Tvshows => [BaseItemKind.Series],
					_ => null
				};
			});
		}
		catch (Exception ex)
		{
			logger.LogError(ex, @"Unhandled exception");
			return null;
		}
	}

	public async Task<PagedResult<BaseItemDto>?> GetItems(ItemQuery itemQuery, CancellationToken cancellationToken = default)
	{
		var elapsed = Stopwatch.StartNew();
		logger.LogInformation("Jellyfin GetItems query started. ParentId={ParentId}, StartIndex={StartIndex}, Limit={Limit}, SortBy={SortBy}, SortOrder={SortOrder}, SearchTerm={SearchTerm}, IncludeItemTypes={IncludeItemTypes}, Genres={Genres}, Tags={Tags}, OfficialRatings={OfficialRatings}, Years={Years}",
			itemQuery.ParentId,
			itemQuery.StartIndex,
			itemQuery.Limit,
			itemQuery.SortBy,
			itemQuery.SortOrder,
			itemQuery.SearchTerm,
			string.Join(",", itemQuery.IncludeItemTypes),
			string.Join(",", itemQuery.Genres),
			string.Join(",", itemQuery.Tags),
			string.Join(",", itemQuery.OfficialRatings),
			string.Join(",", itemQuery.Years));
		try
		{
			var response = await _jellyfinApiClient.Items.GetAsync(x =>
			{
				var query = x.QueryParameters;
				query.StartIndex = itemQuery.StartIndex;
				query.Limit = itemQuery.Limit;
				query.SortBy = itemQuery.SortBy is { } sortBy ? [sortBy] : null;
				query.SortOrder = itemQuery.SortOrder is { } sortOrder ? [sortOrder] : null;
				query.SearchTerm = itemQuery.SearchTerm;
				query.Recursive = itemQuery.Recursive;
				query.Fields = [ItemFields.PrimaryImageAspectRatio, ItemFields.DateCreated, ItemFields.Overview, ItemFields.Tags, ItemFields.Genres];
				query.ImageTypeLimit = 1;
				query.EnableImageTypes = [ImageType.Primary, ImageType.Backdrop, ImageType.Banner, ImageType.Thumb];
				query.ParentId = itemQuery.ParentId;
				query.IncludeItemTypes = itemQuery.IncludeItemTypes.Count > 0 ? [.. itemQuery.IncludeItemTypes] : null;
				query.Genres = itemQuery.Genres.Count > 0 ? [.. itemQuery.Genres] : null;
				query.Years = itemQuery.Years.Count > 0 ? [.. itemQuery.Years] : null;
				query.Tags = itemQuery.Tags.Count > 0 ? [.. itemQuery.Tags] : null;
				query.OfficialRatings = itemQuery.OfficialRatings.Count > 0 ? [.. itemQuery.OfficialRatings] : null;
			}, cancellationToken);

			if (response is null)
			{
				logger.LogWarning("Jellyfin GetItems query returned null. ParentId={ParentId}, StartIndex={StartIndex}, ElapsedMs={ElapsedMs}", itemQuery.ParentId, itemQuery.StartIndex, elapsed.ElapsedMilliseconds);
				return null;
			}

			var result = new PagedResult<BaseItemDto>
			{
				Items = response.Items ?? [],
				StartIndex = response.StartIndex ?? itemQuery.StartIndex,
				TotalRecordCount = response.TotalRecordCount ?? response.Items?.Count ?? 0,
			};
			logger.LogInformation("Jellyfin GetItems query completed. ParentId={ParentId}, StartIndex={StartIndex}, ItemCount={ItemCount}, TotalRecordCount={TotalRecordCount}, ElapsedMs={ElapsedMs}",
				itemQuery.ParentId,
				result.StartIndex,
				result.Items.Count,
				result.TotalRecordCount,
				elapsed.ElapsedMilliseconds);
			return result;
		}
		catch (OperationCanceledException)
		{
			logger.LogInformation("Jellyfin GetItems query cancelled. ParentId={ParentId}, StartIndex={StartIndex}, ElapsedMs={ElapsedMs}", itemQuery.ParentId, itemQuery.StartIndex, elapsed.ElapsedMilliseconds);
			throw;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Jellyfin GetItems query failed. ParentId={ParentId}, StartIndex={StartIndex}, ElapsedMs={ElapsedMs}", itemQuery.ParentId, itemQuery.StartIndex, elapsed.ElapsedMilliseconds);
			return null;
		}
	}

	public async Task<BaseItemDto?> GetItem(Guid id)
	{
		try
		{
			return await _jellyfinApiClient.Items[id].GetAsync(x => x.QueryParameters.UserId = UserId);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, @"Unhandled exception");
			return null;
		}
	}

	public async Task ResetProgress(Guid id)
	{
		var dto = await GetItem(id);
		if (dto is null or { UserData: null})
		{
			return;
		}

		dto.UserData.PlayedPercentage = 0;
		dto.UserData.PlaybackPositionTicks = 0;
		dto.UserData.LastPlayedDate = null;

		await _jellyfinApiClient.Items[id].PostAsync(dto);
	}

	public async Task<BaseItemDtoQueryResult?> GetSimilarItems(BaseItemDto dto)
	{
		if (dto.Id is not { } id)
		{
			return null;
		}

		try
		{
			return await _jellyfinApiClient.Items[id].Similar.GetAsync(x =>
			{
				x.QueryParameters.UserId = UserId;
				x.QueryParameters.Limit = 12;
			});
		}
		catch (Exception ex)
		{
			logger.LogError(ex, @"Unhandled exception");
			return null;
		}
	}

	public async Task<BaseItemDtoQueryResult?> GetMediaFolders()
	{
		try
		{
			return await _jellyfinApiClient.Library.MediaFolders.GetAsync(x => x.QueryParameters.IsHidden = false);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, @"Unhandled exception");
			return null;
		}
	}

	public async Task<UserDto?> CreateUser(string username, string password)
	{
		try
		{
			return await _jellyfinApiClient.Users.New.PostAsync(new CreateUserByName
			{
				Name = username,
				Password = password
			});
		}
		catch (Exception ex)
		{
			logger.LogError(ex, @"Unhandled exception");
			return null;
		}
	}

	public async Task<List<VirtualFolderInfo>> GetVirtualFolders()
	{
		try
		{
			return await _jellyfinApiClient.Library.VirtualFolders.GetAsync() ?? [];
		}
		catch (Exception ex)
		{
			logger.LogError(ex, @"Unhandled exception");
			return [];
		}
	}

	public async Task CreateLibrary(string name, CollectionTypeOptions collectionType, LibraryOptions options)
	{
		try
		{
			await _jellyfinApiClient.Library.VirtualFolders.PostAsync(new AddVirtualFolderDto
			{
				LibraryOptions = options,
			}, x =>
			{
				var query = x.QueryParameters;
				query.RefreshLibrary = true;
				query.Name = name;
				query.CollectionType = collectionType;
			});
		}
		catch (Exception ex)
		{
			logger.LogError(ex, @"Unhandled exception");
			return;
		}
	}

	public async Task DeleteLibrary(string name)
	{
		try
		{
			await _jellyfinApiClient.Library.VirtualFolders.DeleteAsync(x =>
			{
				var query = x.QueryParameters;
				query.RefreshLibrary = true;
				query.Name = name;
			});
		}
		catch (Exception ex)
		{
			logger.LogError(ex, @"Unhandled exception");
			return;
		}
	}

	public async Task RenameLibrary(string name, string newName)
	{
		try
		{
			await _jellyfinApiClient.Library.VirtualFolders.Name.PostAsync(x =>
			{
				var query = x.QueryParameters;
				query.RefreshLibrary = true;
				query.Name = name;
				query.NewName = newName;
			});
		}
		catch (Exception ex)
		{
			logger.LogError(ex, @"Unhandled exception");
			return;
		}
	}

	public async Task DeleteItem(BaseItemDto dto)
	{
		if (dto?.Id is not { } id)
		{
			return;
		}

		try
		{
			await _jellyfinApiClient.Items[id].DeleteAsync();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, @"Unhandled exception");
			return;
		}
	}

	public async Task<Uri?> GetSplashScreen()
	{
		var endoint = _jellyfinApiClient.System.Configuration["branding"].ToGetRequestInformation();
		var options =  await AddApiKey(endoint.URI).GetJsonAsync<BrandingOptions>();

		if(options.SplashscreenEnabled is not true)
		{
			return null;
		}

		return AddApiKey(_jellyfinApiClient.Branding.Splashscreen.ToGetRequestInformation(x =>
		{
			var query = x.QueryParameters;
			query.Blur = 20;
			query.Height = 1080;
			query.Width = 1920;
			query.Format = Jellyfin.Sdk.Generated.Branding.Splashscreen.ImageFormat.Jpg;
		}).URI);
	}
}
