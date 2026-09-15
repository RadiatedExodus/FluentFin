using System.Runtime.InteropServices.WindowsRuntime;
using Blurhash;
using FluentFin.Contracts.Services;
using FluentFin.Core;
using FluentFin.Core.ViewModels;
using Flurl;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FluentFin.Converters;

public static class BaseItemDtoConverters
{
	private static IImageSourceCache ImageCache => App.GetService<IImageSourceCache>();
	private static IBlurHashCache BlurHashCache => App.GetService<IBlurHashCache>();
	private static IJellyfinImageUriProvider ImageUriProvider => App.GetService<IJellyfinImageUriProvider>();

	public static string GetCardTitle(this BaseItemDto? dto)
	{
		if (dto is null)
		{
			return string.Empty;
		}

		if (dto.Type == BaseItemDto_Type.Episode)
		{
			return dto.SeriesName ?? "";
		}

		return dto.Name ?? "";
	}

	public static string GetCardSubtitle(this BaseItemDto? dto)
	{
		if (dto is null)
		{
			return string.Empty;
		}

		if (dto.Type == BaseItemDto_Type.Episode)
		{
			return $"S{dto.ParentIndexNumber}:E{dto.IndexNumber} - {dto.Name}";
		}
		if (dto.Type == BaseItemDto_Type.Movie)
		{
			return $"{dto.ProductionYear}";
		}
		if (dto.Type == BaseItemDto_Type.Series)
		{
			return $"{dto.ProductionYear} - {(string.Equals(dto.Status, "Continuing", StringComparison.OrdinalIgnoreCase) ? "Present" : dto.EndDate?.Year)}";
		}
		if (dto.Type == BaseItemDto_Type.MusicAlbum)
		{
			return string.Join(", ", new[] { dto.AlbumArtist, dto.ProductionYear?.ToString() }.Where(x => !string.IsNullOrWhiteSpace(x)));
		}
		if (dto.Type == BaseItemDto_Type.Audio)
		{
			return string.Join(", ", new[] { string.Join(", ", dto.Artists ?? []), dto.Album }.Where(x => !string.IsNullOrWhiteSpace(x)));
		}
		if (dto.Type == BaseItemDto_Type.Playlist)
		{
			return "Playlist";
		}

		return "";
	}

	public static string ToSessionInfoItemName(this BaseItemDto? dto)
	{
		if (dto is null)
		{
			return string.Empty;
		}

		if (dto.Type == BaseItemDto_Type.Episode)
		{
			return $"S{dto.ParentIndexNumber}:E{dto.IndexNumber} - {dto.Name}";
		}
		if (dto.Type == BaseItemDto_Type.Movie)
		{
			return $"{dto.Name}";
		}

		return "";
	}

	public static string GetSeasonAndEpisodeNumber(this BaseItemDto? dto)
	{
		if (dto is null)
		{
			return string.Empty;
		}

		if (dto.Type is not BaseItemDto_Type.Episode)
		{
			return string.Empty;
		}

		return $"S{dto.ParentIndexNumber}:E{dto.IndexNumber}";
	}

	public static BitmapImage? GetImage(BaseItemPerson personDto, double height)
	{
		var uri = GetImageUri(personDto, height);
		return uri is null ? null : ImageCache.Get(uri);
	}

	public static BitmapImage? GetImage(VirtualFolderInfo folderInfo)
	{
		var uri = GetImageUri(folderInfo);
		return uri is null ? null : ImageCache.Get(uri);
	}

	public static Uri? GetImageUri(BaseItemPerson personDto, double height) => ImageUriProvider.GetImageUri(personDto, height);

	public static Uri? GetImageUri(VirtualFolderInfo folderInfo) => ImageUriProvider.GetImageUri(folderInfo);


	public static WriteableBitmap? GetBlurHash(BaseItemDto? dto, ImageType imageType, double height)
	{
		return BlurHashCache.Get(dto, imageType, height);
	}

	public static BitmapImage? GetImage(BaseItemDto? dto, ImageType imageType, double height)
	{
		var uri = GetImageUri(dto, imageType, height);
		return uri is null ? null : ImageCache.Get(uri);
	}

	public static Uri? GetImageUri(BaseItemDto? dto, ImageType imageType, double height) => ImageUriProvider.GetImageUri(dto, imageType, height);

	public static int GetCardBadgeValue(BaseItemViewModel vm)
	{
		if (vm is null)
		{
			return -1;
		}

		var unwatchedCount = vm.UserData?.UnplayedItemCount ?? 0;

		return unwatchedCount == 0 ? -1 : unwatchedCount;
	}

	public static IconSource? CardBadgeSource(BaseItemViewModel vm)
	{
		if (vm is null)
		{
			return null;
		}

		var unwatchedCount = vm.UserData?.UnplayedItemCount ?? 0;
		var played = vm.UserData?.Played ?? false;

		if (played && unwatchedCount == 0)
		{
			return new FontIconSource { Glyph = "\uF78C" };
		}

		return null;
	}

	public static Brush CardBadgeBackground(BaseItemViewModel vm)
	{
		if (vm is null)
		{
			return (Brush)Application.Current.Resources["InfoBadgeBackground"];
		}

		var unwatchedCount = vm.UserData?.UnplayedItemCount ?? 0;
		var played = vm.UserData?.Played ?? false;

		if (played && unwatchedCount == 0)
		{
			return (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
		}

		return (Brush)Application.Current.Resources["InfoBadgeBackground"];
	}

	public static Visibility IsCardBadgeVisible(BaseItemViewModel vm)
	{
		if (vm is null)
		{
			return Visibility.Collapsed;
		}

		var unwatchedCount = vm.UserData?.UnplayedItemCount ?? 0;
		var played = vm.UserData?.Played ?? false;

		if (played && unwatchedCount == 0)
		{
			return Visibility.Visible;
		}

		if (unwatchedCount > 0)
		{
			return Visibility.Visible;
		}


		return Visibility.Collapsed;
	}
}
