using FluentFin.Contracts.Services;
using FluentFin.Core;
using Flurl;
using Jellyfin.Sdk.Generated.Models;

namespace FluentFin.Services;

public sealed class JellyfinImageUriProvider : IJellyfinImageUriProvider
{
	public Uri? GetImageUri(BaseItemPerson? person, double height)
	{
		if (person?.Id is not { } id || string.IsNullOrWhiteSpace(SessionInfo.BaseUrl))
		{
			return null;
		}

		return SessionInfo.BaseUrl.AppendPathSegment($"/Items/{id}/Images/Primary").SetQueryParam("fillHeight", height).ToUri();
	}

	public Uri? GetImageUri(VirtualFolderInfo? folder)
	{
		if (folder?.ItemId is not { } id || string.IsNullOrWhiteSpace(SessionInfo.BaseUrl))
		{
			return null;
		}

		return SessionInfo.BaseUrl.AppendPathSegment($"/Items/{id}/Images/Primary").ToUri();
	}

	public Uri? GetImageUri(BaseItemDto? item, ImageType imageType, double height)
	{
		if (item?.Id is not { } id || item.ImageTags is null || string.IsNullOrWhiteSpace(SessionInfo.BaseUrl))
		{
			return null;
		}

		var hasRequestTag = item.ImageTags.AdditionalData.TryGetValue($"{imageType}", out object? requestTag);
		var backdropTag = item.BackdropImageTags?.FirstOrDefault();
		var parentBackdropTag = item.ParentBackdropImageTags?.FirstOrDefault();

		var tag = "";
		if (hasRequestTag)
		{
			tag = $"{requestTag}";
		}
		else if (!string.IsNullOrEmpty(backdropTag))
		{
			tag = backdropTag;
		}
		else if (!string.IsNullOrEmpty(parentBackdropTag))
		{
			tag = parentBackdropTag;
		}

		if (imageType == ImageType.Backdrop && item.Type is BaseItemDto_Type.Season or BaseItemDto_Type.Episode && item.SeriesId is { } seriesIdForBackdrop)
		{
			id = seriesIdForBackdrop;
		}

		if (imageType == ImageType.Thumb && !hasRequestTag)
		{
			imageType = ImageType.Primary;
			if (item.ImageTags.AdditionalData.TryGetValue($"{ImageType.Primary}", out var primaryTag))
			{
				tag = $"{primaryTag}";
			}
		}
		else if (imageType == ImageType.Primary && item.Type == BaseItemDto_Type.Episode)
		{
			if (!string.IsNullOrEmpty(item.SeriesPrimaryImageTag) && item.SeriesId is { } seriesId)
			{
				id = seriesId;
				tag = $"{item.SeriesPrimaryImageTag}";
			}
		}
		else if (imageType == ImageType.Logo && item.Type == BaseItemDto_Type.Episode)
		{
			if (!string.IsNullOrEmpty(item.ParentLogoImageTag) && item.SeriesId is { } seriesId)
			{
				id = seriesId;
				tag = $"{item.ParentLogoImageTag}";
			}
		}

		var uri = SessionInfo.BaseUrl.AppendPathSegment($"/Items/{id}/Images/{imageType}");
		if (height > 0)
		{
			uri.SetQueryParam("fillHeight", height);
		}

		if (!string.IsNullOrEmpty(tag))
		{
			uri.SetQueryParam("tag", tag);
		}

		return uri.ToUri();
	}
}
