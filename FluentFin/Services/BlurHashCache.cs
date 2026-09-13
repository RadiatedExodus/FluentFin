using System.Runtime.InteropServices.WindowsRuntime;
using Blurhash;
using FluentFin.Contracts.Services;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FluentFin.Services;

public sealed class BlurHashCache : IBlurHashCache
{
	private readonly Dictionary<string, WriteableBitmap?> _cache = [];
	private readonly Lock _lock = new();

	public WriteableBitmap? Get(BaseItemDto? item, ImageType imageType, double height)
	{
		var key = CreateKey(item, imageType, height, out var blurHash);
		if (key is null)
		{
			return null;
		}

		lock (_lock)
		{
			if (_cache.TryGetValue(key, out var cached))
			{
				return cached;
			}
		}

		var bitmap = Decode(blurHash!);

		lock (_lock)
		{
			_cache[key] = bitmap;
		}

		return bitmap;
	}

	public void Clear()
	{
		lock (_lock)
		{
			_cache.Clear();
		}
	}

	private static string? CreateKey(BaseItemDto? dto, ImageType imageType, double height, out string? blurHash)
	{
		blurHash = null;

		if (dto?.ImageTags is null)
		{
			return null;
		}

		var hasRequestTag = dto.ImageTags.AdditionalData.TryGetValue($"{imageType}", out _);
		if (imageType == ImageType.Thumb && !hasRequestTag)
		{
			imageType = ImageType.Primary;
		}

		if (!dto.ImageTags.AdditionalData.TryGetValue(imageType.ToString(), out var imageTagObj))
		{
			return null;
		}

		var imageTag = $"{imageTagObj}";
		var blurHashesForType = GetBlurHashesForType(dto, imageType);
		if (blurHashesForType?.AdditionalData.TryGetValue(imageTag, out var blurHashObj) != true)
		{
			return null;
		}

		blurHash = $"{blurHashObj}";
		if (string.IsNullOrEmpty(blurHash))
		{
			return null;
		}

		return $"{dto.Id:N}:{imageType}:{imageTag}:{height}:{blurHash}";
	}

	private static IAdditionalDataHolder? GetBlurHashesForType(BaseItemDto dto, ImageType imageType) =>
		imageType switch
		{
			ImageType.Art => dto.ImageBlurHashes?.Art,
			ImageType.Banner => dto.ImageBlurHashes?.Banner,
			ImageType.Backdrop => dto.ImageBlurHashes?.Backdrop,
			ImageType.Box => dto.ImageBlurHashes?.Box,
			ImageType.BoxRear => dto.ImageBlurHashes?.BoxRear,
			ImageType.Chapter => dto.ImageBlurHashes?.Chapter,
			ImageType.Disc => dto.ImageBlurHashes?.Disc,
			ImageType.Logo => dto.ImageBlurHashes?.Logo,
			ImageType.Menu => dto.ImageBlurHashes?.Menu,
			ImageType.Primary => dto.ImageBlurHashes?.Primary,
			ImageType.Profile => dto.ImageBlurHashes?.Profile,
			ImageType.Screenshot => dto.ImageBlurHashes?.Screenshot,
			ImageType.Thumb => dto.ImageBlurHashes?.Thumb,
			_ => null,
		};

	private static WriteableBitmap Decode(string blurHash)
	{
		var pixelData = new Pixel[20, 20];
		Blurhash.Core.Decode(blurHash, pixelData, 1);

		var bitmap = new WriteableBitmap(20, 20);
		using var stream = bitmap.PixelBuffer.AsStream();
		for (int row = 0; row < 20; row++)
		{
			for (int col = 0; col < 20; col++)
			{
				Pixel pixel = pixelData[row, col];
				stream.WriteByte((byte)MathUtils.LinearTosRgb(pixel.Blue));
				stream.WriteByte((byte)MathUtils.LinearTosRgb(pixel.Green));
				stream.WriteByte((byte)MathUtils.LinearTosRgb(pixel.Red));
				stream.WriteByte(255);
			}
		}

		return bitmap;
	}
}

