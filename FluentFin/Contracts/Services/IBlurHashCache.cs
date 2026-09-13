using Jellyfin.Sdk.Generated.Models;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FluentFin.Contracts.Services;

public interface IBlurHashCache
{
	WriteableBitmap? Get(BaseItemDto? item, ImageType imageType, double height);

	void Clear();
}

