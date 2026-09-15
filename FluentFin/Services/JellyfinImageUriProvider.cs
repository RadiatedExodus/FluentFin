using FluentFin.Contracts.Services;
using FluentFin.Core;
using Flurl;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Extensions.Logging;

namespace FluentFin.Services;

public sealed class JellyfinImageUriProvider(ILogger<JellyfinImageUriProvider> logger) : IJellyfinImageUriProvider
{
	public Uri? GetImageUri(BaseItemPerson? person, double height)
	{
		if (person?.Id is not { } id || string.IsNullOrWhiteSpace(SessionInfo.BaseUrl))
		{
			logger.LogDebug("Jellyfin person image URI skipped. HasId={HasId}, HasBaseUrl={HasBaseUrl}", person?.Id is not null, !string.IsNullOrWhiteSpace(SessionInfo.BaseUrl));
			return null;
		}

		return SessionInfo.BaseUrl.AppendPathSegment($"/Items/{id}/Images/Primary").SetQueryParam("fillHeight", height).ToUri();
	}

	public Uri? GetImageUri(VirtualFolderInfo? folder)
	{
		if (folder?.ItemId is not { } id || string.IsNullOrWhiteSpace(SessionInfo.BaseUrl))
		{
			logger.LogDebug("Jellyfin folder image URI skipped. HasItemId={HasItemId}, HasBaseUrl={HasBaseUrl}", folder?.ItemId is not null, !string.IsNullOrWhiteSpace(SessionInfo.BaseUrl));
			return null;
		}

		return SessionInfo.BaseUrl.AppendPathSegment($"/Items/{id}/Images/Primary").ToUri();
	}

	public Uri? GetImageUri(BaseItemDto? item, ImageType imageType, double height)
	{
		if (item?.Id is not { } id || string.IsNullOrWhiteSpace(SessionInfo.BaseUrl))
		{
			logger.LogInformation("Jellyfin item image URI skipped. ItemId={ItemId}, ItemType={ItemType}, ImageType={ImageType}, HasBaseUrl={HasBaseUrl}",
				item?.Id,
				item?.Type,
				imageType,
				!string.IsNullOrWhiteSpace(SessionInfo.BaseUrl));
			return null;
		}

		var imageItemId = id.ToString();
		var tag = GetImageTag(item, imageType);

		if (imageType == ImageType.Backdrop && item.Type is BaseItemDto_Type.Season or BaseItemDto_Type.Episode && item.SeriesId is { } seriesIdForBackdrop)
		{
			imageItemId = seriesIdForBackdrop.ToString();
		}
		else if (imageType == ImageType.Backdrop && string.IsNullOrEmpty(tag))
		{
			tag = item.ParentBackdropImageTags?.FirstOrDefault() ?? "";
			if (!string.IsNullOrEmpty(tag) && item.ParentBackdropItemId is { } parentBackdropItemId)
			{
				imageItemId = parentBackdropItemId.ToString();
			}
		}

		if (imageType == ImageType.Thumb && string.IsNullOrEmpty(tag))
		{
			var thumbTag = item.ParentThumbImageTag;
			if (!string.IsNullOrEmpty(thumbTag) && item.ParentThumbItemId is { } parentThumbItemId)
			{
				imageItemId = parentThumbItemId.ToString();
				tag = thumbTag;
			}
			else if (TryUsePrimaryFallback(item, ref imageItemId, out var primaryTag))
			{
				imageType = ImageType.Primary;
				tag = primaryTag;
			}
		}
		else if (imageType == ImageType.Primary && string.IsNullOrEmpty(tag))
		{
			if (!TryUsePrimaryFallback(item, ref imageItemId, out tag))
			{
				if (item.Type is BaseItemDto_Type.Audio)
				{
					logger.LogInformation("Jellyfin audio image URI skipped because no item or album primary image target was available. ItemId={ItemId}, Name={Name}, AlbumId={AlbumId}, AlbumPrimaryImageTag={AlbumPrimaryImageTag}",
						item.Id,
						item.Name,
						item.AlbumId,
						item.AlbumPrimaryImageTag);
					return null;
				}
			}
		}
		else if (imageType == ImageType.Logo && string.IsNullOrEmpty(tag))
		{
			if (!string.IsNullOrEmpty(item.ParentLogoImageTag) && item.SeriesId is { } seriesId)
			{
				imageItemId = seriesId.ToString();
				tag = $"{item.ParentLogoImageTag}";
			}
		}

		var uri = SessionInfo.BaseUrl.AppendPathSegment($"/Items/{imageItemId}/Images/{imageType}");
		if (height > 0)
		{
			uri.SetQueryParam("fillHeight", height);
		}

		if (!string.IsNullOrEmpty(tag))
		{
			uri.SetQueryParam("tag", tag);
		}

		var result = uri.ToUri();
		logger.LogDebug("Jellyfin item image URI built. ItemId={ItemId}, ItemType={ItemType}, ImageType={ImageType}, ImageItemId={ImageItemId}, HasTag={HasTag}, Uri={Uri}",
			item.Id,
			item.Type,
			imageType,
			imageItemId,
			!string.IsNullOrEmpty(tag),
			result);
		return result;
	}

	private static string GetImageTag(BaseItemDto item, ImageType imageType)
	{
		if (item.ImageTags?.AdditionalData.TryGetValue($"{imageType}", out object? requestTag) == true)
		{
			return $"{requestTag}";
		}

		return imageType switch
		{
			ImageType.Backdrop => item.BackdropImageTags?.FirstOrDefault() ?? "",
			_ => ""
		};
	}

	private static bool TryUsePrimaryFallback(BaseItemDto item, ref string imageItemId, out string tag)
	{
		if (item.Type is BaseItemDto_Type.Episode && !string.IsNullOrEmpty(item.SeriesPrimaryImageTag) && item.SeriesId is { } seriesId)
		{
			imageItemId = seriesId.ToString();
			tag = item.SeriesPrimaryImageTag;
			return true;
		}

		if (item.Type is BaseItemDto_Type.Audio && item.AlbumId is { } albumId)
		{
			imageItemId = albumId.ToString();
			tag = item.AlbumPrimaryImageTag ?? "";
			return true;
		}

		if (!string.IsNullOrEmpty(item.ParentPrimaryImageItemId))
		{
			imageItemId = item.ParentPrimaryImageItemId;
			tag = item.ParentPrimaryImageTag ?? "";
			return true;
		}

		tag = "";
		return false;
	}
}
