using FluentFin.Contracts.Services;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FluentFin.Services;

public sealed class ImageSourceCache : IImageSourceCache
{
	private readonly Dictionary<string, BitmapImage> _cache = [];
	private readonly Lock _lock = new();

	public BitmapImage Get(Uri uri)
	{
		var key = uri.ToString();
		lock (_lock)
		{
			if (!_cache.TryGetValue(key, out var image))
			{
				image = new BitmapImage(uri);
				_cache[key] = image;
			}

			return image;
		}
	}

	public void Clear()
	{
		lock (_lock)
		{
			_cache.Clear();
		}
	}
}

